package main

import (
	"errors"
	"strings"
	"time"
)

// 优先级与状态的枚举值，与作业任务书 §4 的数据结构保持一致。
// pending_review 是题面三态之外的扩展：修复者交活后到管理员确认之间需要这个中间态。
const (
	PriorityLow    = "low"
	PriorityMedium = "medium"
	PriorityHigh   = "high"

	StatusOpen          = "open"
	StatusInProgress    = "in_progress"
	StatusPendingReview = "pending_review"
	StatusResolved      = "resolved"
)

// 修复者能接的三类活。类别由 AI 从标题与描述里判断，巡检员不用选。
const (
	CategoryWall   = "wall"
	CategoryDevice = "device"
	CategorySafety = "safety"
)

// 派单方式：AI 自动派，还是管理员人工派。
const (
	DispatchAI     = "ai"
	DispatchManual = "manual"
)

// Position 是标记在 AR 会话中的位置快照，只是 Issue 的值对象。
type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

// Issue 是本题唯一的实体，规则都落在它身上。
// Reporter 记录是谁报的，巡检员只能看到自己的那几条。
// Category 及其下面的 AI 字段由服务端写，客户端传什么都不算。
type Issue struct {
	ID          string    `json:"id"`
	Title       string    `json:"title"`
	Description string    `json:"description"`
	Priority    string    `json:"priority"`
	Status      string    `json:"status"`
	Position    Position  `json:"position"`
	Reporter    string    `json:"reporter"`
	CreatedAt   time.Time `json:"createdAt"`
	UpdatedAt   time.Time `json:"updatedAt"`

	// AI 分诊结果。category 为空表示没判出来。
	Category     string  `json:"category"`
	AIConfidence float64 `json:"aiConfidence"`
	AIReason     string  `json:"aiReason"`

	// 派单与处理。时间字段用 RFC3339 字符串，空串表示还没发生。
	DispatchMode string `json:"dispatchMode"`
	Assignee     string `json:"assignee"`
	AssignedAt   string `json:"assignedAt"`
	AssignedBy   string `json:"assignedBy"`
	SubmittedAt  string `json:"submittedAt"`

	// 修复记录：原文必存，结构化结果转换失败时为空。
	RepairNote    string `json:"repairNote"`
	RepairSummary string `json:"repairSummary"`
}

var (
	ErrEmptyTitle      = errors.New("标题不能为空")
	ErrInvalidPriority = errors.New("优先级必须是 low、medium 或 high")
	ErrInvalidStatus   = errors.New("状态必须是 open、in_progress、pending_review 或 resolved")
)

func validPriority(p string) bool {
	switch p {
	case PriorityLow, PriorityMedium, PriorityHigh:
		return true
	}
	return false
}

func validStatus(s string) bool {
	switch s {
	case StatusOpen, StatusInProgress, StatusPendingReview, StatusResolved:
		return true
	}
	return false
}

// ValidCategory 判断 AI 返的类别是否在允许的三类里。
func ValidCategory(c string) bool {
	switch c {
	case CategoryWall, CategoryDevice, CategorySafety:
		return true
	}
	return false
}

// NewIssue 按实体不变量构造新问题：id 与时间由服务端生成，状态固定为 open，
// reporter 由登录身份决定，客户端不能自己填。
// 标题与描述去掉首尾空格（人输入）；枚举值严格按字面匹配，不接受大小写变体。
func NewIssue(title, description, priority string, position Position, reporter string, now time.Time) (Issue, error) {
	title = strings.TrimSpace(title)
	if title == "" {
		return Issue{}, ErrEmptyTitle
	}

	if !validPriority(priority) {
		return Issue{}, ErrInvalidPriority
	}

	id, err := newID()
	if err != nil {
		return Issue{}, err
	}

	now = now.UTC()
	return Issue{
		ID:          id,
		Title:       title,
		Description: strings.TrimSpace(description),
		Priority:    priority,
		Status:      StatusOpen,
		Position:    position,
		Reporter:    strings.TrimSpace(reporter),
		CreatedAt:   now,
		UpdatedAt:   now,
	}, nil
}

// ChangeStatus 是实体自己的状态变更：校验枚举、刷新更新时间。
// 题面没有限制迁移方向，三个状态可以任意互改。
func (i *Issue) ChangeStatus(status string, now time.Time) error {
	if !validStatus(status) {
		return ErrInvalidStatus
	}

	i.Status = status
	i.UpdatedAt = now.UTC()
	return nil
}
