package main

import (
	"encoding/json"
	"errors"
	"log"
	"net/http"
	"strings"
	"time"
)

// API 把 HTTP 动作接到实体与仓储上。
// ai 在启动时读一次配置，没有 key 时 Enabled() 为 false，自动派单直接降级。
type API struct {
	store *Store
	ai    AIConfig
}

func (a *API) Routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", a.handleHealth)
	mux.HandleFunc("POST /login", a.handleLogin)

	// 巡检员能上报、能看自己报的；只有管理员能处置状态。
	mux.HandleFunc("POST /issues",
		a.requireRole([]string{RoleInspector, RoleAdmin}, a.handleCreate))
	mux.HandleFunc("GET /issues",
		a.requireRole([]string{RoleInspector, RoleAdmin, RoleRepairer}, a.handleList))
	mux.HandleFunc("GET /issues/{id}",
		a.requireRole([]string{RoleInspector, RoleAdmin, RoleRepairer}, a.handleDetail))
	mux.HandleFunc("POST /issues/{id}/actions",
		a.requireRole([]string{RoleAdmin, RoleRepairer}, a.handleAction))
	mux.HandleFunc("PATCH /issues/{id}/status",
		a.requireRole([]string{RoleAdmin}, a.handleUpdateStatus))

	// 人员管理只有管理员能做
	mux.HandleFunc("GET /users",
		a.requireRole([]string{RoleAdmin}, a.handleListUsers))
	mux.HandleFunc("POST /users",
		a.requireRole([]string{RoleAdmin}, a.handleCreateUser))
	mux.HandleFunc("PATCH /users/{username}",
		a.requireRole([]string{RoleAdmin}, a.handleUpdateUser))

	return withCORS(mux)
}

// createRequest 只包含客户端能决定的字段，id、status 与时间由服务端生成。
type createRequest struct {
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    string   `json:"priority"`
	Position    Position `json:"position"`
}

type statusRequest struct {
	Status string `json:"status"`
}

// actionRequest 是一次流转动作。不同 action 用到的字段不同。
type actionRequest struct {
	Action   string `json:"action"`
	Assignee string `json:"assignee"`
	Note     string `json:"note"`
}

// detailResponse 是详情页要的东西：工单本体 + 全部留痕。
type detailResponse struct {
	Issue  Issue        `json:"issue"`
	Events []IssueEvent `json:"events"`
}

var (
	ErrForbidden = errors.New("当前账号没有这个操作权限")
	ErrNotYours  = errors.New("这条工单不在你名下")
)

type errorResponse struct {
	Error string `json:"error"`
}

type loginRequest struct {
	Username string `json:"username"`
	Password string `json:"password"`
}

type loginResponse struct {
	Token       string   `json:"token"`
	Username    string   `json:"username"`
	Role        string   `json:"role"`
	Specialties []string `json:"specialties"`
}

func (a *API) handleHealth(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
}

// handleLogin 校验账号密码，签发一个带身份和过期时间的 token。
// 用户名不存在和密码错误返回同一句话，不告诉调用方哪一个错了。
func (a *API) handleLogin(w http.ResponseWriter, r *http.Request) {
	var req loginRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}

	user, found, err := a.store.FindUser(strings.TrimSpace(req.Username))
	if err != nil {
		log.Printf("查询用户失败: %v", err)
		writeError(w, http.StatusInternalServerError, "登录失败，请稍后重试")
		return
	}

	if !found || !user.CheckPassword(req.Password) {
		writeError(w, http.StatusUnauthorized, ErrBadLogin.Error())
		return
	}

	writeJSON(w, http.StatusOK, loginResponse{
		Token:       SignToken(user, time.Now()),
		Username:    user.Username,
		Role:        user.Role,
		Specialties: user.Specialties,
	})
}

func (a *API) handleCreate(w http.ResponseWriter, r *http.Request) {
	claims, ok := claimsFrom(r.Context())
	if !ok {
		writeError(w, http.StatusUnauthorized, "请先登录")
		return
	}

	var req createRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}

	// 上报人取自登录身份，客户端传什么都不算
	now := time.Now()
	issue, err := NewIssue(req.Title, req.Description, req.Priority, req.Position, claims.Username, now)
	if err != nil {
		writeError(w, http.StatusBadRequest, err.Error())
		return
	}

	created := newEvent(issue.ID, claims.Username, ActionCreated, "", issue.Status, "提交问题", now)
	if err := a.store.Create(issue, created); err != nil {
		log.Printf("保存问题失败: %v", err)
		writeError(w, http.StatusInternalServerError, "保存问题失败")
		return
	}

	// 工单已经落库，再尝试自动派单。AI 挂了也只是转人工，不影响上报本身。
	dispatchEvent := a.AutoDispatch(r.Context(), &issue, time.Now())
	if applied, err := a.store.ApplyChange(issue, StatusOpen, dispatchEvent); err != nil {
		log.Printf("写回派单结果失败: %v", err)
	} else if !applied {
		log.Printf("写回派单结果时工单已被改动: %s", issue.ID)
	}

	writeJSON(w, http.StatusCreated, issue)
}

func (a *API) handleList(w http.ResponseWriter, r *http.Request) {
	claims, ok := claimsFrom(r.Context())
	if !ok {
		writeError(w, http.StatusUnauthorized, "请先登录")
		return
	}

	var (
		issues []Issue
		err    error
	)

	switch claims.Role {
	case RoleAdmin:
		issues, err = a.store.List()
	case RoleRepairer:
		issues, err = a.store.ListByAssignee(claims.Username)
	default:
		issues, err = a.store.ListByReporter(claims.Username)
	}

	if err != nil {
		log.Printf("查询问题列表失败: %v", err)
		writeError(w, http.StatusInternalServerError, "查询问题列表失败")
		return
	}

	writeJSON(w, http.StatusOK, issues)
}

// handleDetail 返回工单本体和它的全部留痕，给网页端的抽屉和时间线用。
func (a *API) handleDetail(w http.ResponseWriter, r *http.Request) {
	claims, ok := claimsFrom(r.Context())
	if !ok {
		writeError(w, http.StatusUnauthorized, "请先登录")
		return
	}

	id := r.PathValue("id")

	issue, found, err := a.store.Get(id)
	if err != nil {
		log.Printf("查询问题失败: %v", err)
		writeError(w, http.StatusInternalServerError, "查询问题失败")
		return
	}
	if !found {
		writeError(w, http.StatusNotFound, "问题不存在")
		return
	}

	if !canView(claims, issue) {
		writeError(w, http.StatusForbidden, ErrForbidden.Error())
		return
	}

	events, err := a.store.ListEvents(id)
	if err != nil {
		log.Printf("查询工单留痕失败: %v", err)
		writeError(w, http.StatusInternalServerError, "查询工单留痕失败")
		return
	}

	writeJSON(w, http.StatusOK, detailResponse{Issue: issue, Events: events})
}

// handleAction 是工单流转的唯一入口：派单、退回、提交、撤回、验收、驳回、重开、关闭。
// 所有动作先校验状态（用 from 状态做条件更新），被人抢先改过就回 409。
func (a *API) handleAction(w http.ResponseWriter, r *http.Request) {
	claims, ok := claimsFrom(r.Context())
	if !ok {
		writeError(w, http.StatusUnauthorized, "请先登录")
		return
	}

	id := r.PathValue("id")

	var req actionRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}

	issue, found, err := a.store.Get(id)
	if err != nil {
		log.Printf("查询问题失败: %v", err)
		writeError(w, http.StatusInternalServerError, "查询问题失败")
		return
	}
	if !found {
		writeError(w, http.StatusNotFound, "问题不存在")
		return
	}

	previous := issue.Status
	now := time.Now()

	event, err := a.applyAction(r.Context(), claims, &issue, req, now)
	if err != nil {
		status := http.StatusBadRequest
		if errors.Is(err, ErrForbidden) || errors.Is(err, ErrNotYours) {
			status = http.StatusForbidden
		}
		writeError(w, status, err.Error())
		return
	}

	applied, err := a.store.ApplyChange(issue, previous, event)
	if err != nil {
		log.Printf("更新工单失败: %v", err)
		writeError(w, http.StatusInternalServerError, "更新工单失败")
		return
	}

	if !applied {
		if current, found, err := a.store.Get(id); err == nil && found {
			issue = current
		}
		writeConflict(w, issue)
		return
	}

	events, err := a.store.ListEvents(id)
	if err != nil {
		log.Printf("查询工单留痕失败: %v", err)
		events = []IssueEvent{event}
	}

	writeJSON(w, http.StatusOK, detailResponse{Issue: issue, Events: events})
}

func (a *API) handleUpdateStatus(w http.ResponseWriter, r *http.Request) {
	claims, ok := claimsFrom(r.Context())
	if !ok {
		writeError(w, http.StatusUnauthorized, "请先登录")
		return
	}

	id := r.PathValue("id")

	var req statusRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}

	issue, found, err := a.store.Get(id)
	if err != nil {
		log.Printf("查询问题失败: %v", err)
		writeError(w, http.StatusInternalServerError, "查询问题失败")
		return
	}
	if !found {
		writeError(w, http.StatusNotFound, "问题不存在")
		return
	}

	previous := issue.Status
	now := time.Now()

	// 状态合法性由实体自己判断，handler 不再碰枚举
	if err := issue.ChangeStatus(req.Status, now); err != nil {
		writeError(w, http.StatusBadRequest, err.Error())
		return
	}

	// 直接改状态只能在合法流转之间走，不能跳过派单把人从"未处理"改成"处理中"
	if !canTransition(previous, issue.Status) {
		writeError(w, http.StatusConflict, "这条工单不能从「"+statusLabel(previous)+"」直接变成「"+statusLabel(issue.Status)+"」")
		return
	}

	event := newEvent(issue.ID, claims.Username, actionForStatusChange(previous, issue.Status),
		previous, issue.Status, "", now)

	applied, err := a.store.ApplyChange(issue, previous, event)
	if err != nil {
		log.Printf("更新状态失败: %v", err)
		writeError(w, http.StatusInternalServerError, "更新状态失败")
		return
	}

	if !applied {
		// 被别的请求抢先改了，把最新状态告诉调用方
		if current, found, err := a.store.Get(issue.ID); err == nil && found {
			issue = current
		}
		writeConflict(w, issue)
		return
	}

	writeJSON(w, http.StatusOK, issue)
}

func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)

	if err := json.NewEncoder(w).Encode(body); err != nil {
		log.Printf("写响应失败: %v", err)
	}
}

func writeError(w http.ResponseWriter, status int, message string) {
	writeJSON(w, status, errorResponse{Error: message})
}

// writeConflict 在并发冲突时返回 409，并把当前真实状态一并告诉调用方。
func writeConflict(w http.ResponseWriter, issue Issue) {
	writeJSON(w, http.StatusConflict, map[string]string{
		"error":  "这条工单刚被改动，请刷新后重试",
		"status": issue.Status,
	})
}

// withCORS 放行浏览器跨域，React 管理端才能直接访问后端。
func withCORS(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Access-Control-Allow-Origin", "*")
		w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PATCH, OPTIONS")
		w.Header().Set("Access-Control-Allow-Headers", "Content-Type, Authorization")

		if r.Method == http.MethodOptions {
			w.WriteHeader(http.StatusNoContent)
			return
		}

		next.ServeHTTP(w, r)
	})
}
