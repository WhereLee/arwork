package main

import (
	"errors"
	"strings"
	"time"
)

// 优先级与状态的枚举值，与作业任务书 §4 的数据结构保持一致。
const (
	PriorityLow    = "low"
	PriorityMedium = "medium"
	PriorityHigh   = "high"

	StatusOpen       = "open"
	StatusInProgress = "in_progress"
	StatusResolved   = "resolved"
)

// Position 是标记在 AR 会话中的位置快照，只是 Issue 的值对象。
type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

// Issue 是本题唯一的实体，规则都落在它身上。
// Reporter 记录是谁报的，巡检员只能看到自己的那几条。
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
}

var (
	ErrEmptyTitle      = errors.New("标题不能为空")
	ErrInvalidPriority = errors.New("优先级必须是 low、medium 或 high")
	ErrInvalidStatus   = errors.New("状态必须是 open、in_progress 或 resolved")
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
	case StatusOpen, StatusInProgress, StatusResolved:
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
