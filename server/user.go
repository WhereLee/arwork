package main

import (
	"errors"
	"strings"

	"golang.org/x/crypto/bcrypt"
)

// 两种身份：巡检员只上报、只能看自己报的；管理员看全部并处置状态。
const (
	RoleInspector = "inspector"
	RoleAdmin     = "admin"
)

// User 是登录主体。PasswordHash 不出现在任何 JSON 里，明文密码不落地。
type User struct {
	Username     string
	PasswordHash string
	Role         string
}

var (
	ErrEmptyUsername = errors.New("用户名不能为空")
	ErrShortPassword = errors.New("密码至少 6 位")
	ErrBadLogin      = errors.New("用户名或密码不对")
)

func validRole(role string) bool {
	switch role {
	case RoleInspector, RoleAdmin:
		return true
	}
	return false
}

// NewUser 把明文密码换成 bcrypt 哈希，结构体里不保存明文。
func NewUser(username, password, role string) (User, error) {
	username = strings.TrimSpace(username)
	if username == "" {
		return User{}, ErrEmptyUsername
	}

	if len(password) < 6 {
		return User{}, ErrShortPassword
	}

	if !validRole(role) {
		return User{}, errors.New("角色必须是 inspector 或 admin")
	}

	hash, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	if err != nil {
		return User{}, err
	}

	return User{Username: username, PasswordHash: string(hash), Role: role}, nil
}

// CheckPassword 用 bcrypt 比对，不把密码回显给调用方。
func (u User) CheckPassword(password string) bool {
	return bcrypt.CompareHashAndPassword([]byte(u.PasswordHash), []byte(password)) == nil
}
