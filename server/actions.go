package main

import (
	"context"
	"errors"
	"strings"
	"time"
)

// ErrStateConflict 表示当前状态不允许这个动作。
// handler 据此返回 409，与参数写错的 400 区分开：前者是「工单已经不在这个状态」，
// 后者是「你传的东西不对」，客户端处理方式不同。
type ErrStateConflict struct {
	Message string
}

func (e ErrStateConflict) Error() string {
	return e.Message
}

func stateConflict(message string) error {
	return ErrStateConflict{Message: message}
}

// canView 判断这个账号能不能看这条工单。
// 巡检员看自己报的，修复者看派给自己的（退回后仍能看到），管理员看全部。
func canView(claims Claims, issue Issue) bool {
	switch claims.Role {
	case RoleAdmin:
		return true
	case RoleRepairer:
		return issue.Assignee == claims.Username
	default:
		return issue.Reporter == claims.Username
	}
}

// applyAction 校验权限与必填项，就地修改 issue，返回要记的事件。
// 返回值 error 只用于失败；成功时 from/to 状态已经反映在 issue 和事件里。
func (a *API) applyAction(ctx context.Context, claims Claims, issue *Issue, req actionRequest, now time.Time) (IssueEvent, error) {
	action := strings.TrimSpace(req.Action)
	note := strings.TrimSpace(req.Note)

	switch action {
	case "dispatch":
		return a.actionDispatch(claims, issue, req, note, now)
	case "return":
		return a.actionReturn(claims, issue, note, now)
	case "submit":
		return a.actionSubmit(ctx, claims, issue, note, now)
	case "withdraw":
		return a.actionWithdraw(claims, issue, now)
	case "approve":
		return a.actionApprove(claims, issue, now)
	case "reject":
		return a.actionReject(claims, issue, note, now)
	case "reopen":
		return a.actionReopen(claims, issue, note, now)
	case "close":
		return a.actionClose(claims, issue, note, now)
	default:
		return IssueEvent{}, errors.New("不支持的动作：" + action)
	}
}

// actionDispatch 管理员派单或改派。AI 派不动的工单就靠它落地。
func (a *API) actionDispatch(claims Claims, issue *Issue, req actionRequest, note string, now time.Time) (IssueEvent, error) {
	if claims.Role != RoleAdmin {
		return IssueEvent{}, ErrForbidden
	}

	if issue.Status != StatusOpen && issue.Status != StatusInProgress {
		return IssueEvent{}, stateConflict("只有未处理或处理中的工单可以派单")
	}

	assignee := strings.TrimSpace(req.Assignee)
	if assignee == "" {
		return IssueEvent{}, errors.New("必须指定处理人")
	}

	target, found, err := a.store.FindUser(assignee)
	if err != nil {
		return IssueEvent{}, err
	}

	if !found || target.Role != RoleRepairer || !target.Active {
		return IssueEvent{}, errors.New("指定的处理人不存在、不是修复者或已停用")
	}

	from := issue.Status
	action := ActionManualDispatched
	if from == StatusInProgress {
		action = ActionReassigned
	}

	// 技能不匹配不拦：管理员可能有别的安排，但要在留痕里说清楚
	if issue.Category != "" && !target.HasSkill(issue.Category) {
		if note == "" {
			note = "指派给 " + assignee
		}
		note += "（注意：该修复者没有 " + issue.Category + " 技能）"
	}

	if note == "" {
		note = "指派给 " + assignee
	}

	issue.Assignee = assignee
	issue.DispatchMode = DispatchManual
	issue.AssignedAt = now.UTC().Format(time.RFC3339Nano)
	issue.AssignedBy = claims.Username
	issue.Status = StatusInProgress
	issue.UpdatedAt = now

	return newEvent(issue.ID, claims.Username, action, from, StatusInProgress, note, now), nil
}

// actionReturn 修复者退回：这活不归我。退回后回到未处理，等管理员重新派。
func (a *API) actionReturn(claims Claims, issue *Issue, note string, now time.Time) (IssueEvent, error) {
	if err := requireOwner(claims, issue); err != nil {
		return IssueEvent{}, err
	}

	if issue.Status != StatusInProgress {
		return IssueEvent{}, stateConflict("只有处理中的工单可以退回")
	}

	if note == "" {
		return IssueEvent{}, errors.New("退回时必须写原因")
	}

	from := issue.Status
	issue.Status = StatusOpen
	issue.UpdatedAt = now

	// assignee 保留：留痕要能看出这条曾派给谁，退回次数的统计也靠它
	return newEvent(issue.ID, claims.Username, ActionReturned, from, StatusOpen, note, now), nil
}

// actionSubmit 修复者交活，写处理记录，进入待确认。
func (a *API) actionSubmit(ctx context.Context, claims Claims, issue *Issue, note string, now time.Time) (IssueEvent, error) {
	if err := requireOwner(claims, issue); err != nil {
		return IssueEvent{}, err
	}

	if issue.Status != StatusInProgress {
		return IssueEvent{}, stateConflict("只有处理中的工单可以提交")
	}

	if note == "" {
		return IssueEvent{}, errors.New("提交时必须写处理记录")
	}

	from := issue.Status
	issue.Status = StatusPendingReview
	issue.SubmittedAt = now.UTC().Format(time.RFC3339Nano)
	issue.RepairNote = note
	issue.UpdatedAt = now

	// 结构化是增强项，失败就留空，原文已经存好了
	issue.RepairSummary = a.summarizeRepair(ctx, note)

	return newEvent(issue.ID, claims.Username, ActionSubmitted, from, StatusPendingReview, note, now), nil
}

// actionWithdraw 修复者撤回已提交的处理记录，回去继续改。
func (a *API) actionWithdraw(claims Claims, issue *Issue, now time.Time) (IssueEvent, error) {
	if err := requireOwner(claims, issue); err != nil {
		return IssueEvent{}, err
	}

	if issue.Status != StatusPendingReview {
		return IssueEvent{}, stateConflict("只有待确认的工单可以撤回")
	}

	from := issue.Status
	issue.Status = StatusInProgress
	issue.UpdatedAt = now

	return newEvent(issue.ID, claims.Username, ActionWithdrawn, from, StatusInProgress, "撤回后重新处理", now), nil
}

// actionApprove 管理员验收通过。
func (a *API) actionApprove(claims Claims, issue *Issue, now time.Time) (IssueEvent, error) {
	if claims.Role != RoleAdmin {
		return IssueEvent{}, ErrForbidden
	}

	if issue.Status != StatusPendingReview {
		return IssueEvent{}, stateConflict("只有待确认的工单可以验收")
	}

	from := issue.Status
	issue.Status = StatusResolved
	issue.UpdatedAt = now

	return newEvent(issue.ID, claims.Username, ActionApproved, from, StatusResolved, "验收通过", now), nil
}

// actionReject 管理员驳回，打回处理中让修复者重做。
func (a *API) actionReject(claims Claims, issue *Issue, note string, now time.Time) (IssueEvent, error) {
	if claims.Role != RoleAdmin {
		return IssueEvent{}, ErrForbidden
	}

	if issue.Status != StatusPendingReview {
		return IssueEvent{}, stateConflict("只有待确认的工单可以驳回")
	}

	if note == "" {
		return IssueEvent{}, errors.New("驳回时必须写原因")
	}

	from := issue.Status
	issue.Status = StatusInProgress
	issue.UpdatedAt = now

	return newEvent(issue.ID, claims.Username, ActionRejected, from, StatusInProgress, note, now), nil
}

// actionReopen 管理员重开已解决的工单。
func (a *API) actionReopen(claims Claims, issue *Issue, note string, now time.Time) (IssueEvent, error) {
	if claims.Role != RoleAdmin {
		return IssueEvent{}, ErrForbidden
	}

	if issue.Status != StatusResolved {
		return IssueEvent{}, stateConflict("只有已解决的工单可以重开")
	}

	if note == "" {
		return IssueEvent{}, errors.New("重开时必须写原因")
	}

	from := issue.Status
	issue.Status = StatusInProgress
	issue.UpdatedAt = now

	if issue.Assignee == "" {
		return IssueEvent{}, stateConflict("这条工单没有处理人，请先派单")
	}

	return newEvent(issue.ID, claims.Username, ActionReopened, from, StatusInProgress, note, now), nil
}

// actionClose 管理员直接把工单关掉：误报、重复、不用修。
func (a *API) actionClose(claims Claims, issue *Issue, note string, now time.Time) (IssueEvent, error) {
	if claims.Role != RoleAdmin {
		return IssueEvent{}, ErrForbidden
	}

	if issue.Status != StatusOpen && issue.Status != StatusInProgress {
		return IssueEvent{}, stateConflict("只有未处理或处理中的工单可以关闭")
	}

	if note == "" {
		return IssueEvent{}, errors.New("关闭时必须写原因")
	}

	from := issue.Status
	issue.Status = StatusResolved
	issue.UpdatedAt = now

	return newEvent(issue.ID, claims.Username, ActionClosed, from, StatusResolved, note, now), nil
}

// requireOwner 只有当前处理人能动这条工单。
func requireOwner(claims Claims, issue *Issue) error {
	if claims.Username != issue.Assignee {
		return ErrNotYours
	}
	return nil
}
