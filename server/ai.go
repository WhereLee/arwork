package main

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"strconv"
	"strings"
	"time"
)

// Triage 是 AI 的唯一产出：能不能自动派、派给哪一类。
// 它不判断优先级，也不决定具体派给谁。
type Triage struct {
	Category    string
	CanDispatch bool
	Confidence  float64
	Reason      string
}

// AIError 是"这次调用没能拿到可用结果"的原因，会被写进事件流方便排查。
type AIError struct {
	Reason string
}

func (e AIError) Error() string { return e.Reason }

var errBadOutput = errors.New("AI 输出不是合法 JSON")

// AIConfig 从环境变量读，没有配置 key 时 Enabled() 为 false，调用方应直接降级。
type AIConfig struct {
	APIKey    string
	BaseURL   string
	Model     string
	Threshold float64
	Timeout   time.Duration
}

func LoadAIConfig() AIConfig {
	return AIConfig{
		APIKey:    strings.TrimSpace(os.Getenv("DEEPSEEK_API_KEY")),
		BaseURL:   envOr("DEEPSEEK_BASE_URL", "https://api.deepseek.com"),
		Model:     envOr("DEEPSEEK_MODEL", "deepseek-flash"),
		Threshold: envFloat("AI_CONFIDENCE_THRESHOLD", 0.7),
		Timeout:   time.Duration(envInt("AI_TIMEOUT_SECONDS", 15)) * time.Second,
	}
}

func (c AIConfig) Enabled() bool {
	return c.APIKey != ""
}

const triagePrompt = `你是园区巡检工单的分诊助手。根据巡检员填写的标题和描述，判断这条工单该派给哪一类修复者。

可选类别只有三种：
- wall：墙面破损、裂缝、渗水、脱落、涂料起皮
- device：设备异常，如门禁、照明、空调、水泵、电梯等
- safety：安全隐患，如漏电、积水、堵塞、松动、遮挡消防设施

判断规则：
1. 只根据文本中明确提到的信息判断，不要猜测。
2. 如果描述过于笼统（例如"有问题""坏了""看一下"）无法判断类别，canDispatch 返回 false。
3. 如果同时涉及多类，选最需要先处理的那一类。
4. confidence 表示你对类别判断的把握，0 到 1 之间。
5. 只输出 JSON，不要任何解释文字。

输出格式：
{"category":"wall","canDispatch":true,"confidence":0.82,"reason":"一句话说明依据"}`

type chatMessage struct {
	Role    string `json:"role"`
	Content string `json:"content"`
}

type chatRequest struct {
	Model          string            `json:"model"`
	Messages       []chatMessage     `json:"messages"`
	ResponseFormat map[string]string `json:"response_format"`
	Temperature    float64           `json:"temperature"`
}

type chatResponse struct {
	Choices []struct {
		Message chatMessage `json:"message"`
	} `json:"choices"`
}

type triagePayload struct {
	Category    string  `json:"category"`
	CanDispatch bool    `json:"canDispatch"`
	Confidence  float64 `json:"confidence"`
	Reason      string  `json:"reason"`
}

// Triage 调一次模型拿分诊结果。
// 输出不是合法 JSON 时重试一次（模型偶尔会多说一句话），其余错误直接返回。
func (c AIConfig) Triage(ctx context.Context, title, description string) (Triage, error) {
	if !c.Enabled() {
		return Triage{}, AIError{Reason: "未配置 AI，转人工派单"}
	}

	var lastErr error
	for attempt := 0; attempt < 2; attempt++ {
		result, err := c.triageOnce(ctx, title, description)
		if err == nil {
			return result, nil
		}

		lastErr = err

		// 只有"模型说了话但不是 JSON"值得重试，网络和 HTTP 错误重试也是一样的结果
		if !errors.Is(err, errBadOutput) {
			break
		}
	}

	return Triage{}, lastErr
}

func (c AIConfig) triageOnce(parent context.Context, title, description string) (Triage, error) {
	content, err := c.postChat(parent, triagePrompt, buildTriageInput(title, description))
	if err != nil {
		return Triage{}, err
	}

	var out triagePayload
	if err := json.Unmarshal([]byte(content), &out); err != nil {
		return Triage{}, errBadOutput
	}

	return validateTriage(out, c.Threshold)
}

// postChat 发一次对话请求，返回模型给出的正文（已去掉代码块包裹）。
func (c AIConfig) postChat(parent context.Context, systemPrompt, userPrompt string) (string, error) {
	ctx, cancel := context.WithTimeout(parent, c.Timeout)
	defer cancel()

	body := chatRequest{
		Model: c.Model,
		Messages: []chatMessage{
			{Role: "system", Content: systemPrompt},
			{Role: "user", Content: userPrompt},
		},
		ResponseFormat: map[string]string{"type": "json_object"},
		Temperature:    0,
	}

	raw, err := json.Marshal(body)
	if err != nil {
		return "", AIError{Reason: "构造请求失败"}
	}

	url := strings.TrimRight(c.BaseURL, "/") + "/chat/completions"
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(raw))
	if err != nil {
		return "", AIError{Reason: "构造请求失败"}
	}

	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+c.APIKey)

	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return "", AIError{Reason: "调用失败：" + err.Error()}
	}
	defer resp.Body.Close()

	payload, err := io.ReadAll(io.LimitReader(resp.Body, 1<<20))
	if err != nil {
		return "", AIError{Reason: "读取响应失败"}
	}

	if resp.StatusCode != http.StatusOK {
		return "", AIError{Reason: fmt.Sprintf("接口返回 %d：%s", resp.StatusCode, truncate(string(payload), 200))}
	}

	var parsed chatResponse
	if err := json.Unmarshal(payload, &parsed); err != nil || len(parsed.Choices) == 0 {
		return "", AIError{Reason: "响应结构不符合预期"}
	}

	return stripCodeFence(parsed.Choices[0].Message.Content), nil
}

// validateTriage 只认三类技能和 0~1 的置信度，任何越界都当失败处理，绝不把半截数据写进库。
func validateTriage(p triagePayload, threshold float64) (Triage, error) {
	if !p.CanDispatch {
		return Triage{
			Category:    p.Category,
			CanDispatch: false,
			Confidence:  clamp01(p.Confidence),
			Reason:      strings.TrimSpace(p.Reason),
		}, nil
	}

	if !ValidCategory(p.Category) {
		return Triage{}, AIError{Reason: "AI 给的类别不在允许范围内：" + p.Category}
	}

	if p.Confidence < 0 || p.Confidence > 1 {
		return Triage{}, AIError{Reason: "AI 给的置信度超出 0~1"}
	}

	// 类别判出来了但没把握：记下判断，但不自动派
	if p.Confidence < threshold {
		return Triage{
			Category:    p.Category,
			CanDispatch: false,
			Confidence:  p.Confidence,
			Reason:      strings.TrimSpace(p.Reason),
		}, nil
	}

	return Triage{
		Category:    p.Category,
		CanDispatch: true,
		Confidence:  p.Confidence,
		Reason:      strings.TrimSpace(p.Reason),
	}, nil
}

const repairPrompt = `你是园区巡检的工单整理助手。维修人员用自然语言写下了处理过程，请把它整理成结构化记录。

规则：
1. 只提取描述里明确写了的信息，没写就留空字符串。
2. action 用一句话概括做了什么；result 写处理结果；parts 列出更换或使用的材料备件，没有就给空数组；followUp 写需要后续跟进的事项，没有就留空。
3. 只输出 JSON，不要任何解释文字。

输出格式：
{"action":"","result":"","parts":[],"followUp":""}`

type repairSummary struct {
	Action   string   `json:"action"`
	Result   string   `json:"result"`
	Parts    []string `json:"parts"`
	FollowUp string   `json:"followUp"`
}

// summarizeRepair 把处理记录转成结构化 JSON。
// 任何失败都返回空串：原文已经在 repair_note 里，转换只是增强，不能因为 AI 挂了就交不了活。
func (a *API) summarizeRepair(ctx context.Context, note string) string {
	if !a.ai.Enabled() || strings.TrimSpace(note) == "" {
		return ""
	}

	content, err := a.ai.postChat(ctx, repairPrompt, note)
	if err != nil {
		log.Printf("处理记录结构化失败: %v", err)
		return ""
	}

	var out repairSummary
	if err := json.Unmarshal([]byte(content), &out); err != nil {
		log.Printf("处理记录结构化输出不合法: %v", err)
		return ""
	}

	raw, err := json.Marshal(out)
	if err != nil {
		return ""
	}

	return string(raw)
}

func buildTriageInput(title, description string) string {
	description = strings.TrimSpace(description)
	if description == "" {
		description = "（未填写）"
	}

	return "标题：" + strings.TrimSpace(title) + "\n描述：" + description
}

// stripCodeFence 去掉模型偶尔包在外面的 ```json 代码块。
func stripCodeFence(s string) string {
	s = strings.TrimSpace(s)
	if !strings.HasPrefix(s, "```") {
		return s
	}

	s = strings.TrimPrefix(s, "```")
	s = strings.TrimPrefix(s, "json")
	s = strings.TrimSuffix(s, "```")

	return strings.TrimSpace(s)
}

func clamp01(v float64) float64 {
	if v < 0 {
		return 0
	}
	if v > 1 {
		return 1
	}
	return v
}

func truncate(s string, max int) string {
	runes := []rune(s)
	if len(runes) <= max {
		return s
	}
	return string(runes[:max]) + "…"
}

func envFloat(key string, fallback float64) float64 {
	if v := os.Getenv(key); v != "" {
		if f, err := strconv.ParseFloat(v, 64); err == nil {
			return f
		}
	}
	return fallback
}

func envInt(key string, fallback int) int {
	if v := os.Getenv(key); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			return n
		}
	}
	return fallback
}
