package main

import (
	"path/filepath"
	"testing"
	"time"
)

// newTestStore 在临时目录建库，测试之间互不干扰，也不碰开发用的 data/issues.db。
func newTestStore(t *testing.T) *Store {
	t.Helper()

	store, err := NewStore(filepath.Join(t.TempDir(), "test.db"))
	if err != nil {
		t.Fatalf("建库失败: %v", err)
	}

	t.Cleanup(func() { store.Close() })

	return store
}

func mustIssue(t *testing.T, title, reporter string) Issue {
	t.Helper()

	issue, err := NewIssue(title, "现场看到的描述", "high", Position{X: 1, Y: 2, Z: 3}, reporter, time.Now())
	if err != nil {
		t.Fatalf("造工单失败: %v", err)
	}

	return issue
}

func mustCreate(t *testing.T, store *Store, issue Issue) {
	t.Helper()

	event := newEvent(issue.ID, issue.Reporter, ActionCreated, "", StatusOpen, "提交问题", time.Now())
	if err := store.Create(issue, event); err != nil {
		t.Fatalf("写入工单失败: %v", err)
	}
}

func TestCreateAndGet(t *testing.T) {
	store := newTestStore(t)
	issue := mustIssue(t, "墙面裂缝", "inspector1")

	mustCreate(t, store, issue)

	got, ok, err := store.Get(issue.ID)
	if err != nil {
		t.Fatalf("读回失败: %v", err)
	}
	if !ok {
		t.Fatal("刚写进去的工单读不到")
	}

	if got.Title != "墙面裂缝" {
		t.Fatalf("标题不对: %q", got.Title)
	}
	if got.Status != StatusOpen {
		t.Fatalf("新工单状态应为 open，实际 %q", got.Status)
	}
	if got.Position.X != 1 {
		t.Fatalf("坐标没存住: %+v", got.Position)
	}
}

func TestCreateWritesEvent(t *testing.T) {
	store := newTestStore(t)
	issue := mustIssue(t, "道闸不抬杆", "inspector1")

	mustCreate(t, store, issue)

	events, err := store.ListEvents(issue.ID)
	if err != nil {
		t.Fatalf("读留痕失败: %v", err)
	}
	if len(events) != 1 {
		t.Fatalf("建单应产生 1 条留痕，实际 %d 条", len(events))
	}
	if events[0].Action != ActionCreated {
		t.Fatalf("留痕类型应为 created，实际 %q", events[0].Action)
	}
}

// 三个角色看到的列表不同：巡检员看自己报的，修复者看派给自己的。
func TestListByRole(t *testing.T) {
	store := newTestStore(t)

	first := mustIssue(t, "甲报的", "inspector1")
	second := mustIssue(t, "乙报的", "inspector2")
	mustCreate(t, store, first)
	mustCreate(t, store, second)

	mine, err := store.ListByReporter("inspector1")
	if err != nil {
		t.Fatalf("按上报人查失败: %v", err)
	}
	if len(mine) != 1 || mine[0].Title != "甲报的" {
		t.Fatalf("巡检员应只看到自己那条，实际 %d 条", len(mine))
	}

	// 把第二条派给 repairer1，再看修复者的列表
	second.Assignee = "repairer1"
	second.Status = StatusInProgress
	ok, err := store.ApplyChange(second, StatusOpen,
		newEvent(second.ID, ActorAI, ActionAIDispatched, StatusOpen, StatusInProgress, "派单", time.Now()))
	if err != nil || !ok {
		t.Fatalf("派单失败: ok=%v err=%v", ok, err)
	}

	assigned, err := store.ListByAssignee("repairer1")
	if err != nil {
		t.Fatalf("按处理人查失败: %v", err)
	}
	if len(assigned) != 1 || assigned[0].Title != "乙报的" {
		t.Fatalf("修复者应只看到派给自己的那条，实际 %d 条", len(assigned))
	}
}

// ApplyChange 是并发保护的核心：状态对不上就不改，返回 false。
func TestApplyChangeRejectsStaleStatus(t *testing.T) {
	store := newTestStore(t)
	issue := mustIssue(t, "配电房堆物", "inspector1")
	mustCreate(t, store, issue)

	issue.Status = StatusInProgress
	issue.Assignee = "repairer1"

	// 先真的把库里的状态改成 in_progress
	ok, err := store.ApplyChange(issue, StatusOpen,
		newEvent(issue.ID, "admin", ActionManualDispatched, StatusOpen, StatusInProgress, "派单", time.Now()))
	if err != nil || !ok {
		t.Fatalf("首次派单应成功: ok=%v err=%v", ok, err)
	}

	// 再拿一份「以为还是 open」的旧快照去改，应当被拒绝
	stale := issue
	stale.Status = StatusResolved
	ok, err = store.ApplyChange(stale, StatusOpen,
		newEvent(issue.ID, "admin", ActionApproved, StatusOpen, StatusResolved, "", time.Now()))
	if err != nil {
		t.Fatalf("不该报错，只该返回 false: %v", err)
	}
	if ok {
		t.Fatal("用陈旧的期望状态修改时不应写入成功")
	}

	// 用正确的期望值再改一次，应当成功。
	// 注意：action 层的做法是先把 issue.Status 改成目标状态再落库，这里要模拟同样的顺序。
	issue.Status = StatusPendingReview
	ok, err = store.ApplyChange(issue, StatusInProgress,
		newEvent(issue.ID, "repairer1", ActionSubmitted, StatusInProgress, StatusPendingReview, "修好了", time.Now()))
	if err != nil || !ok {
		t.Fatalf("状态对得上时应成功: ok=%v err=%v", ok, err)
	}

	got, _, _ := store.Get(issue.ID)
	if got.Status != StatusPendingReview {
		t.Fatalf("状态应变成 pending_review，实际 %q", got.Status)
	}
}

// 并发来的两次修改只能成功一次，另一个必须失败。
func TestApplyChangeOnlyOneWins(t *testing.T) {
	store := newTestStore(t)
	issue := mustIssue(t, "顶灯不亮", "inspector1")
	mustCreate(t, store, issue)

	const workers = 4
	results := make(chan bool, workers)

	for i := 0; i < workers; i++ {
		go func() {
			// 每个 goroutine 都拿同一份快照去改
			snapshot := issue
			snapshot.Status = StatusResolved
			ok, _ := store.ApplyChange(snapshot, StatusOpen,
				newEvent(issue.ID, "admin", ActionApproved, StatusOpen, StatusResolved, "", time.Now()))
			results <- ok
		}()
	}

	wins := 0
	for i := 0; i < workers; i++ {
		if <-results {
			wins++
		}
	}

	if wins != 1 {
		t.Fatalf("并发修改应该只有一个成功，实际成功 %d 次", wins)
	}
}
