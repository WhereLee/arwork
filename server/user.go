package main

import (
	"errors"
	"strings"

	"golang.org/x/crypto/bcrypt"
)

// 三种身份：巡检员上报、修复者处理、管理员看全局并派单。
const (
	RoleInspector = "inspector"
	RoleAdmin     = "admin"
	RoleRepairer  = "repairer"
)

// User 是登录主体。PasswordHash 不出现在任何 JSON 里，明文密码不落地。
// Specialties 只在 repairer 上有意义，取值见 Category* 常量。
type User struct {
	Username     string
	PasswordHash string
	Role         string
	Specialties  []string
	Active       bool
}

var (
	ErrEmptyUsername   = errors.New("用户名不能为空")
	ErrShortPassword   = errors.New("密码至少 6 位")
	ErrBadLogin        = errors.New("用户名或密码不对")
	ErrInvalidRole     = errors.New("角色必须是 inspector、admin 或 repairer")
	ErrInvalidSkill    = errors.New("技能只能是 wall、device 或 safety")
	ErrUserExists      = errors.New("用户名已存在")
	ErrRepairerHasWork = errors.New("该修复者名下还有未完成的工单，请先把工单退回再停用")
)

func validRole(role string) bool {
	switch role {
	case RoleInspector, RoleAdmin, RoleRepairer:
		return true
	}
	return false
}

// HasSkill 判断这个修复者能不能接某一类活。
func (u User) HasSkill(category string) bool {
	for _, s := range u.Specialties {
		if s == category {
			return true
		}
	}
	return false
}

// NewUser 把明文密码换成 bcrypt 哈希；技能逐个校验，重复的去掉。
func NewUser(username, password, role string, specialties []string) (User, error) {
	username = strings.TrimSpace(username)
	if username == "" {
		return User{}, ErrEmptyUsername
	}

	if len(password) < 6 {
		return User{}, ErrShortPassword
	}

	if !validRole(role) {
		return User{}, ErrInvalidRole
	}

	skills, err := NormalizeSpecialties(specialties)
	if err != nil {
		return User{}, err
	}

	hash, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	if err != nil {
		return User{}, err
	}

	return User{
		Username:     username,
		PasswordHash: string(hash),
		Role:         role,
		Specialties:  skills,
		Active:       true,
	}, nil
}

// NormalizeSpecialties 校验并去重，空列表允许（先建账号后补技能）。
func NormalizeSpecialties(list []string) ([]string, error) {
	seen := make(map[string]bool)
	result := make([]string, 0, len(list))

	for _, raw := range list {
		s := strings.TrimSpace(raw)
		if s == "" || seen[s] {
			continue
		}

		if !ValidCategory(s) {
			return nil, ErrInvalidSkill
		}

		seen[s] = true
		result = append(result, s)
	}

	return result, nil
}

// JoinSpecialties 把技能列表存成逗号分隔的字符串。
func JoinSpecialties(list []string) string {
	return strings.Join(list, ",")
}

// SplitSpecialties 解析存下来的逗号分隔字符串。
func SplitSpecialties(raw string) []string {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return []string{}
	}

	parts := strings.Split(raw, ",")
	result := make([]string, 0, len(parts))

	for _, p := range parts {
		if s := strings.TrimSpace(p); s != "" {
			result = append(result, s)
		}
	}

	return result
}

// CheckPassword 用 bcrypt 比对，不把密码回显给调用方。
func (u User) CheckPassword(password string) bool {
	return bcrypt.CompareHashAndPassword([]byte(u.PasswordHash), []byte(password)) == nil
}
