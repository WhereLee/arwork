package main

import (
	"context"
	"log"
	"sort"
	"strconv"
	"time"
)

// 建议负载上限。超过仍然派单，只是会在事件里留一句提示，不让工单因为"师傅都忙"就没人管。
const maxSuggestedLoad = 5

// AutoDispatch 用 AI 判断能否派单，能派就在候选人里挑一个，直接改写 issue。
// 返回的事件交给调用方写库：派成功是 ai_dispatched，没派成是 ai_failed。
//
// 无论派不派得成，AI 的判断（类别、置信度、依据）都会写回工单，
// 人工派单时能看到"AI 认为这是设备类，但只有 0.4 的把握"。
func (a *API) AutoDispatch(ctx context.Context, issue *Issue, now time.Time) IssueEvent {
	triage, err := a.ai.Triage(ctx, issue.Title, issue.Description)

	if err != nil {
		return newEvent(issue.ID, ActorAI, ActionAIFailed, StatusOpen, StatusOpen,
			"AI 未能判定："+err.Error(), now)
	}

	issue.Category = triage.Category
	issue.AIConfidence = triage.Confidence
	issue.AIReason = triage.Reason

	if !triage.CanDispatch {
		return newEvent(issue.ID, ActorAI, ActionAIFailed, StatusOpen, StatusOpen,
			skipReason(triage), now)
	}

	candidates, err := a.store.ListRepairers(triage.Category)
	if err != nil {
		log.Printf("查询修复者失败: %v", err)
		return newEvent(issue.ID, ActorAI, ActionAIFailed, StatusOpen, StatusOpen,
			"查询可派人员失败，转人工派单", now)
	}

	// 把这条工单退回过的人排除掉，别又派回同一个坑
	returners, err := a.store.ReturnersOf(issue.ID)
	if err != nil {
		log.Printf("查询退回记录失败: %v", err)
	}

	picked, note := a.pickRepairer(candidates, returners, triage.Category)
	if picked == "" {
		return newEvent(issue.ID, ActorAI, ActionAIFailed, StatusOpen, StatusOpen, note, now)
	}

	issue.Assignee = picked
	issue.DispatchMode = DispatchAI
	issue.AssignedAt = now.UTC().Format(time.RFC3339Nano)
	issue.AssignedBy = ActorAI
	issue.Status = StatusInProgress
	issue.UpdatedAt = now

	reason := "AI 判定为 " + triage.Category + "（置信度 " + formatConfidence(triage.Confidence) + "），派给 " + picked
	if triage.Reason != "" {
		reason += "；依据：" + triage.Reason
	}
	if note != "" {
		reason += "；" + note
	}

	return newEvent(issue.ID, ActorAI, ActionAIDispatched, StatusOpen, StatusInProgress, reason, now)
}

type candidateScore struct {
	name    string
	load    int
	returns int
}

// pickRepairer 按「当前负载少的优先 → 历史上退回少的优先 → 用户名」排序取第一个。
// 最后一个条件保证同一批候选人每次选出来的人是固定的，便于排查。
func (a *API) pickRepairer(candidates []User, returners []string, category string) (string, string) {
	skip := make(map[string]bool, len(returners))
	for _, name := range returners {
		skip[name] = true
	}

	pool := make([]candidateScore, 0, len(candidates))
	for _, u := range candidates {
		if skip[u.Username] {
			continue
		}

		load, err := a.store.CountActiveWork(u.Username)
		if err != nil {
			log.Printf("统计负载失败 %s: %v", u.Username, err)
		}

		returns, err := a.store.CountReturns(u.Username, category)
		if err != nil {
			log.Printf("统计退回次数失败 %s: %v", u.Username, err)
		}

		pool = append(pool, candidateScore{name: u.Username, load: load, returns: returns})
	}

	if len(pool) == 0 {
		return "", "该类别暂无可用修复者"
	}

	sort.Slice(pool, func(i, j int) bool {
		if pool[i].load != pool[j].load {
			return pool[i].load < pool[j].load
		}
		if pool[i].returns != pool[j].returns {
			return pool[i].returns < pool[j].returns
		}
		return pool[i].name < pool[j].name
	})

	best := pool[0]

	note := ""
	if best.load >= maxSuggestedLoad {
		note = "已超出建议负载 " + strconv.Itoa(maxSuggestedLoad) + " 单"
	}

	return best.name, note
}

// skipReason 解释"AI 判了但没自动派"的原因，写进事件流。
func skipReason(t Triage) string {
	if t.Category == "" {
		return "描述过于笼统，AI 判不出类别，转人工派单"
	}

	return "AI 判断为 " + t.Category + "，但置信度只有 " +
		formatConfidence(t.Confidence) + "，转人工派单"
}

func formatConfidence(v float64) string {
	return strconv.FormatFloat(v, 'f', 2, 64)
}
