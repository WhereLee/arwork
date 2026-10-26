package main

import (
	"database/sql"
	"strings"
	"time"
)

// 工单流转的动作。每一个都对应一次状态变化，ai_failed 例外——它只记录"AI 没能判定"。
const (
	ActionCreated          = "created"
	ActionAIDispatched     = "ai_dispatched"
	ActionManualDispatched = "manual_dispatched"
	ActionReturned         = "returned"
	ActionReassigned       = "reassigned"
	ActionSubmitted        = "submitted"
	ActionApproved         = "approved"
	ActionRejected         = "rejected"
	ActionWithdrawn        = "withdrawn"
	ActionReopened         = "reopened"
	ActionClosed           = "closed"
	ActionAIFailed         = "ai_failed"
)

// 非人类操作者的标识，写进事件流的 actor 字段。
const (
	ActorAI     = "ai"
	ActorSystem = "system"
)

// IssueEvent 是一条留痕：谁、什么时候、把工单从什么状态改成了什么状态、为什么。
// 网页端的时间线就是按 id 顺序渲染这些记录。
type IssueEvent struct {
	ID         int64  `json:"id"`
	IssueID    string `json:"issueId"`
	Actor      string `json:"actor"`
	Action     string `json:"action"`
	FromStatus string `json:"fromStatus"`
	ToStatus   string `json:"toStatus"`
	Note       string `json:"note"`
	CreatedAt  string `json:"createdAt"`
}

func newEvent(issueID, actor, action, fromStatus, toStatus, note string, now time.Time) IssueEvent {
	return IssueEvent{
		IssueID:    issueID,
		Actor:      actor,
		Action:     action,
		FromStatus: fromStatus,
		ToStatus:   toStatus,
		Note:       strings.TrimSpace(note),
		CreatedAt:  now.UTC().Format(time.RFC3339Nano),
	}
}

// canTransition 判断一次「直接改状态」是否合法。
// 不包含 open → in_progress：那条路必须走派单，否则会出现处理中但没人负责的单。
func canTransition(from, to string) bool {
	if from == to {
		return false
	}

	switch from {
	case StatusOpen:
		return to == StatusResolved // 误报关闭
	case StatusInProgress:
		return to == StatusResolved // 强制关闭
	case StatusPendingReview:
		return to == StatusResolved || to == StatusInProgress // 确认 或 驳回
	case StatusResolved:
		return to == StatusInProgress // 重开
	}

	return false
}

// actionForStatusChange 把一次直接改状态映射成具体的动作名，让事件流能读懂。
func actionForStatusChange(from, to string) string {
	switch {
	case from == StatusPendingReview && to == StatusResolved:
		return ActionApproved
	case from == StatusPendingReview && to == StatusInProgress:
		return ActionRejected
	case from == StatusResolved && to == StatusInProgress:
		return ActionReopened
	case to == StatusResolved:
		return ActionClosed
	default:
		return ActionManualDispatched
	}
}

// statusLabel 给使用者看的中文状态名。
func statusLabel(status string) string {
	switch status {
	case StatusOpen:
		return "未处理"
	case StatusInProgress:
		return "处理中"
	case StatusPendingReview:
		return "待确认"
	case StatusResolved:
		return "已解决"
	default:
		return status
	}
}

// execer 让事件写入既能走连接也能走事务。
type execer interface {
	Exec(query string, args ...any) (sql.Result, error)
}

const insertEventSQL = `INSERT INTO issue_events
	(issue_id, actor, action, from_status, to_status, note, created_at)
	VALUES (?, ?, ?, ?, ?, ?, ?)`

func insertEvent(db execer, event IssueEvent) error {
	_, err := db.Exec(insertEventSQL,
		event.IssueID,
		event.Actor,
		event.Action,
		event.FromStatus,
		event.ToStatus,
		event.Note,
		event.CreatedAt,
	)
	return err
}
