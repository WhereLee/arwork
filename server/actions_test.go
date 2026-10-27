package main

import (
	"context"
	"strings"
	"testing"
	"time"
)

// 状态机是这套系统里最容易出错的地方：每个动作都有前提状态和角色要求，
// 用一张表把所有「该拒绝」的组合跑一遍，比逐个写 if 断言可靠。
func TestActionRejects(t *testing.T) {
	tests := []struct {
		name     string
		status   string
		action   string
		role     string
		assignee string
		wantErr  string
	}{
		// 角色限制：重开和关闭在 action 层自己卡，派单/提交靠 handler 层卡
		{"修复者不能重开", StatusResolved, "reopen", RoleRepairer, "repairer1", "没有这个操作权限"},
		{"修复者不能关闭", StatusOpen, "close", RoleRepairer, "repairer1", "没有这个操作权限"},

		// 派单的前置条件
		{"派单必须指定处理人", StatusOpen, "dispatch", RoleAdmin, "", "必须指定处理人"},

		// 状态前提：不属于自己的单不能动
		{"别人的单不能提交", StatusInProgress, "submit", RoleRepairer, "repairer9", "不在你名下"},
		{"别人的单不能退回", StatusInProgress, "return", RoleRepairer, "repairer9", "不在你名下"},

		// 状态前提：非法状态
		{"未处理不能提交", StatusOpen, "submit", RoleRepairer, "repairer1", "只有处理中的工单可以提交"},
		{"未处理不能退回", StatusOpen, "return", RoleRepairer, "repairer1", "只有处理中的工单可以退回"},
		{"未处理不能验收", StatusOpen, "approve", RoleAdmin, "", "只有待确认的工单可以验收"},
		{"已解决不能验收", StatusResolved, "approve", RoleAdmin, "", "只有待确认的工单可以验收"},
		{"处理中不能验收", StatusInProgress, "approve", RoleAdmin, "", "只有待确认的工单可以验收"},
		{"处理中不能撤回", StatusInProgress, "withdraw", RoleRepairer, "repairer1", "只有待确认的工单可以撤回"},
		{"待确认不能提交", StatusPendingReview, "submit", RoleRepairer, "repairer1", "只有处理中的工单可以提交"},
		{"已解决不能关闭", StatusResolved, "close", RoleAdmin, "", "只有未处理或处理中的工单可以关闭"},

		// 必填参数
		{"退回必须写原因", StatusInProgress, "return", RoleRepairer, "repairer1", "退回时必须写原因"},
		{"驳回必须写原因", StatusPendingReview, "reject", RoleAdmin, "", "驳回时必须写原因"},
		{"重开必须写原因", StatusResolved, "reopen", RoleAdmin, "repairer1", "重开时必须写原因"},
		{"关闭必须写原因", StatusOpen, "close", RoleAdmin, "", "关闭时必须写原因"},
		{"提交必须写处理记录", StatusInProgress, "submit", RoleRepairer, "repairer1", "提交时必须写处理记录"},

		// 不存在的动作
		{"不支持的动作", StatusOpen, "explode", RoleAdmin, "", "不支持的动作"},
	}

	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			api := &API{store: newTestStore(t)}

			issue := mustIssue(t, "测试工单", "inspector1")
			issue.Status = tc.status
			issue.Assignee = tc.assignee

			claims := Claims{Username: "someone", Role: tc.role}
			if tc.assignee != "" && strings.Contains(tc.wantErr, "不在你名下") {
				claims.Username = "another-repairer"
			}
			if tc.role == RoleRepairer && !strings.Contains(tc.wantErr, "不在你名下") {
				claims.Username = tc.assignee
			}

			_, err := api.applyAction(context.Background(), claims, &issue,
				actionRequest{Action: tc.action, Note: ""}, time.Now())

			if err == nil {
				t.Fatalf("期望被拒绝，实际通过了")
			}
			if !strings.Contains(err.Error(), tc.wantErr) {
				t.Fatalf("错误信息不对\n期望包含: %s\n实际: %s", tc.wantErr, err.Error())
			}
		})
	}
}

// 管理员派单要能落到具体人身上，并且状态从 open 变成 in_progress。
func TestDispatchAssignsRepairer(t *testing.T) {
	store := newTestStore(t)
	api := &API{store: store}

	// 新库里没有用户，先建一个修复者，否则派单会报「处理人不存在」
	repairer, err := NewUser("repairer2", "pass123456", RoleRepairer, []string{"device"})
	if err != nil {
		t.Fatalf("造修复者失败: %v", err)
	}
	if err := store.CreateUser(repairer, time.Now()); err != nil {
		t.Fatalf("写修复者失败: %v", err)
	}

	issue := mustIssue(t, "门禁读卡器无响应", "inspector1")
	mustCreate(t, store, issue)

	claims := Claims{Username: "admin", Role: RoleAdmin}
	event, err := api.applyAction(context.Background(), claims, &issue,
		actionRequest{Action: "dispatch", Assignee: "repairer2"}, time.Now())
	if err != nil {
		t.Fatalf("派单失败: %v", err)
	}

	if issue.Assignee != "repairer2" {
		t.Fatalf("处理人应为 repairer2，实际 %q", issue.Assignee)
	}
	if issue.Status != StatusInProgress {
		t.Fatalf("状态应为 in_progress，实际 %q", issue.Status)
	}
	if event.Action != ActionManualDispatched {
		t.Fatalf("留痕类型应为 manual_dispatched，实际 %q", event.Action)
	}
}

// 修复者提交后进待确认，管理员审批后才算解决。
func TestSubmitThenApprove(t *testing.T) {
	store := newTestStore(t)
	api := &API{store: store}

	issue := mustIssue(t, "顶灯不亮", "inspector1")
	mustCreate(t, store, issue)
	issue.Status = StatusInProgress
	issue.Assignee = "repairer2"

	repairer := Claims{Username: "repairer2", Role: RoleRepairer}
	_, err := api.applyAction(context.Background(), repairer, &issue,
		actionRequest{Action: "submit", Note: "已更换镇流器，通电测试正常"}, time.Now())
	if err != nil {
		t.Fatalf("提交失败: %v", err)
	}
	if issue.Status != StatusPendingReview {
		t.Fatalf("提交后应为 pending_review，实际 %q", issue.Status)
	}
	if issue.RepairNote == "" {
		t.Fatal("处理记录没写进工单")
	}

	admin := Claims{Username: "admin", Role: RoleAdmin}
	_, err = api.applyAction(context.Background(), admin, &issue,
		actionRequest{Action: "approve"}, time.Now())
	if err != nil {
		t.Fatalf("审批失败: %v", err)
	}
	if issue.Status != StatusResolved {
		t.Fatalf("审批后应为 resolved，实际 %q", issue.Status)
	}
}

// AI 没配 key 时必须安静降级：不派单、写一条 ai_failed、状态仍是 open。
func TestAutoDispatchDegradesWithoutKey(t *testing.T) {
	store := newTestStore(t)
	api := &API{store: store, ai: AIConfig{}} // 没有 APIKey，Enabled() 为 false

	issue := mustIssue(t, "墙面有裂缝", "inspector1")

	event := api.AutoDispatch(context.Background(), &issue, time.Now())

	if issue.Status != StatusOpen {
		t.Fatalf("AI 不可用时状态不应变化，实际 %q", issue.Status)
	}
	if issue.Assignee != "" {
		t.Fatalf("AI 不可用时不应派单，实际派给了 %q", issue.Assignee)
	}
	if event.Action != ActionAIFailed {
		t.Fatalf("应写 ai_failed 留痕，实际 %q", event.Action)
	}
	if event.Note == "" {
		t.Fatal("降级原因要写进留痕，否则事后查不出为什么没派")
	}
}
