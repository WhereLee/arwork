package main

import (
	"encoding/json"
	"errors"
	"log"
	"net/http"
	"strings"
	"time"
)

// userView 是人员列表里的一行。密码哈希绝不出现在这里。
type userView struct {
	Username    string   `json:"username"`
	Role        string   `json:"role"`
	Specialties []string `json:"specialties"`
	Active      bool     `json:"active"`
	ActiveWork  int      `json:"activeWork"`
}

type createUserRequest struct {
	Username    string   `json:"username"`
	Password    string   `json:"password"`
	Role        string   `json:"role"`
	Specialties []string `json:"specialties"`
}

// updateUserRequest 用指针区分"没传这个字段"和"传了空值"。
type updateUserRequest struct {
	Specialties *[]string `json:"specialties"`
	Active      *bool     `json:"active"`
}

func (a *API) handleListUsers(w http.ResponseWriter, _ *http.Request) {
	users, err := a.store.ListUsers()
	if err != nil {
		log.Printf("查询账号失败: %v", err)
		writeError(w, http.StatusInternalServerError, "查询账号失败")
		return
	}

	views := make([]userView, 0, len(users))
	for _, u := range users {
		work := 0
		if u.Role == RoleRepairer {
			if n, err := a.store.CountActiveWork(u.Username); err == nil {
				work = n
			}
		}

		views = append(views, userView{
			Username:    u.Username,
			Role:        u.Role,
			Specialties: u.Specialties,
			Active:      u.Active,
			ActiveWork:  work,
		})
	}

	writeJSON(w, http.StatusOK, views)
}

func (a *API) handleCreateUser(w http.ResponseWriter, r *http.Request) {
	var req createUserRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}

	user, err := NewUser(req.Username, req.Password, req.Role, req.Specialties)
	if err != nil {
		writeError(w, http.StatusBadRequest, err.Error())
		return
	}

	// 修复者必须有至少一个技能，否则派单永远轮不到他
	if user.Role == RoleRepairer && len(user.Specialties) == 0 {
		writeError(w, http.StatusBadRequest, "修复者至少要有一个技能：wall、device 或 safety")
		return
	}

	if err := a.store.CreateUser(user, time.Now()); err != nil {
		if errors.Is(err, ErrUserExists) {
			writeError(w, http.StatusConflict, err.Error())
			return
		}

		log.Printf("新建账号失败: %v", err)
		writeError(w, http.StatusInternalServerError, "新建账号失败")
		return
	}

	writeJSON(w, http.StatusCreated, userView{
		Username:    user.Username,
		Role:        user.Role,
		Specialties: user.Specialties,
		Active:      user.Active,
		ActiveWork:  0,
	})
}

func (a *API) handleUpdateUser(w http.ResponseWriter, r *http.Request) {
	username := strings.TrimSpace(r.PathValue("username"))

	target, found, err := a.store.FindUser(username)
	if err != nil {
		log.Printf("查询账号失败: %v", err)
		writeError(w, http.StatusInternalServerError, "查询账号失败")
		return
	}
	if !found {
		writeError(w, http.StatusNotFound, "账号不存在")
		return
	}

	var req updateUserRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}

	if req.Specialties != nil {
		skills, err := NormalizeSpecialties(*req.Specialties)
		if err != nil {
			writeError(w, http.StatusBadRequest, err.Error())
			return
		}

		if err := a.store.UpdateUserSkills(username, skills); err != nil {
			log.Printf("更新技能失败: %v", err)
			writeError(w, http.StatusInternalServerError, "更新技能失败")
			return
		}

		target.Specialties = skills
	}

	if req.Active != nil {
		// 停用前必须把手上的活清掉，否则工单会跟着一个人"消失"
		if !*req.Active {
			work, err := a.store.CountActiveWork(username)
			if err != nil {
				log.Printf("统计负载失败: %v", err)
				writeError(w, http.StatusInternalServerError, "统计负载失败")
				return
			}

			if work > 0 {
				writeError(w, http.StatusConflict, ErrRepairerHasWork.Error())
				return
			}
		}

		if err := a.store.SetUserActive(username, *req.Active); err != nil {
			log.Printf("更新启用状态失败: %v", err)
			writeError(w, http.StatusInternalServerError, "更新启用状态失败")
			return
		}

		target.Active = *req.Active
	}

	work := 0
	if target.Role == RoleRepairer {
		if n, err := a.store.CountActiveWork(username); err == nil {
			work = n
		}
	}

	writeJSON(w, http.StatusOK, userView{
		Username:    target.Username,
		Role:        target.Role,
		Specialties: target.Specialties,
		Active:      target.Active,
		ActiveWork:  work,
	})
}
