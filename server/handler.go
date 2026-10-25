package main

import (
	"encoding/json"
	"log"
	"net/http"
	"time"
)

// API 把 HTTP 动作接到实体与仓储上。
type API struct {
	store *Store
}

func (a *API) Routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", a.handleHealth)
	mux.HandleFunc("POST /issues", a.handleCreate)
	mux.HandleFunc("GET /issues", a.handleList)
	mux.HandleFunc("PATCH /issues/{id}/status", a.handleUpdateStatus)
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

type errorResponse struct {
	Error string `json:"error"`
}

func (a *API) handleHealth(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
}

func (a *API) handleCreate(w http.ResponseWriter, r *http.Request) {
	var req createRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}

	issue, err := NewIssue(req.Title, req.Description, req.Priority, req.Position, time.Now())
	if err != nil {
		writeError(w, http.StatusBadRequest, err.Error())
		return
	}

	if err := a.store.Create(issue); err != nil {
		log.Printf("保存问题失败: %v", err)
		writeError(w, http.StatusInternalServerError, "保存问题失败")
		return
	}

	writeJSON(w, http.StatusCreated, issue)
}

func (a *API) handleList(w http.ResponseWriter, _ *http.Request) {
	issues, err := a.store.List()
	if err != nil {
		log.Printf("查询问题列表失败: %v", err)
		writeError(w, http.StatusInternalServerError, "查询问题列表失败")
		return
	}

	writeJSON(w, http.StatusOK, issues)
}

func (a *API) handleUpdateStatus(w http.ResponseWriter, r *http.Request) {
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

	// 状态合法性由实体自己判断，handler 不再碰枚举
	if err := issue.ChangeStatus(req.Status, time.Now()); err != nil {
		writeError(w, http.StatusBadRequest, err.Error())
		return
	}

	if _, err := a.store.Update(issue); err != nil {
		log.Printf("更新状态失败: %v", err)
		writeError(w, http.StatusInternalServerError, "更新状态失败")
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

// withCORS 放行浏览器跨域，React 管理端才能直接访问后端。
func withCORS(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Access-Control-Allow-Origin", "*")
		w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PATCH, OPTIONS")
		w.Header().Set("Access-Control-Allow-Headers", "Content-Type")

		if r.Method == http.MethodOptions {
			w.WriteHeader(http.StatusNoContent)
			return
		}

		next.ServeHTTP(w, r)
	})
}
