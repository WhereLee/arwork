package main

import (
	"context"
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"errors"
	"net/http"
	"os"
	"strconv"
	"strings"
	"time"
)

// token 12 小时有效：够一次演示，也不至于永久有效。
const tokenTTL = 12 * time.Hour

var ErrInvalidToken = errors.New("登录已失效，请重新登录")

// Claims 是签名 token 里携带的身份信息。
type Claims struct {
	Username string
	Role     string
}

type contextKey string

const claimsKey contextKey = "claims"

func authSecret() []byte {
	if v := os.Getenv("AUTH_SECRET"); v != "" {
		return []byte(v)
	}

	// 开发默认值。部署时设 AUTH_SECRET，换机器后旧 token 才会失效。
	return []byte("arwork-dev-secret")
}

func sign(payload []byte) []byte {
	mac := hmac.New(sha256.New, authSecret())
	mac.Write(payload)
	return mac.Sum(nil)
}

// SignToken 把「用户名|角色|过期时间」签名后拼成 token。
// 不在服务端存 token，所以重启服务后已发出的 token 仍然有效。
func SignToken(u User, now time.Time) string {
	payload := u.Username + "|" + u.Role + "|" + strconv.FormatInt(now.Add(tokenTTL).Unix(), 10)
	encoded := base64.RawURLEncoding.EncodeToString([]byte(payload))
	signature := base64.RawURLEncoding.EncodeToString(sign([]byte(payload)))
	return encoded + "." + signature
}

// ParseToken 验签并检查过期时间。
func ParseToken(token string, now time.Time) (Claims, error) {
	parts := strings.Split(token, ".")
	if len(parts) != 2 {
		return Claims{}, ErrInvalidToken
	}

	payload, err := base64.RawURLEncoding.DecodeString(parts[0])
	if err != nil {
		return Claims{}, ErrInvalidToken
	}

	signature, err := base64.RawURLEncoding.DecodeString(parts[1])
	if err != nil {
		return Claims{}, ErrInvalidToken
	}

	if !hmac.Equal(signature, sign(payload)) {
		return Claims{}, ErrInvalidToken
	}

	fields := strings.Split(string(payload), "|")
	if len(fields) != 3 {
		return Claims{}, ErrInvalidToken
	}

	expires, err := strconv.ParseInt(fields[2], 10, 64)
	if err != nil {
		return Claims{}, ErrInvalidToken
	}

	if now.Unix() >= expires {
		return Claims{}, ErrInvalidToken
	}

	return Claims{Username: fields[0], Role: fields[1]}, nil
}

func withClaims(ctx context.Context, claims Claims) context.Context {
	return context.WithValue(ctx, claimsKey, claims)
}

func claimsFrom(ctx context.Context) (Claims, bool) {
	claims, ok := ctx.Value(claimsKey).(Claims)
	return claims, ok
}

// requireRole 是鉴权中间件：没带 token 返回 401，角色不对返回 403。
func (a *API) requireRole(roles []string, next http.HandlerFunc) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		header := r.Header.Get("Authorization")

		if !strings.HasPrefix(header, "Bearer ") {
			writeError(w, http.StatusUnauthorized, "请先登录")
			return
		}

		claims, err := ParseToken(strings.TrimPrefix(header, "Bearer "), time.Now())
		if err != nil {
			writeError(w, http.StatusUnauthorized, err.Error())
			return
		}

		allowed := false
		for _, role := range roles {
			if role == claims.Role {
				allowed = true
				break
			}
		}

		if !allowed {
			writeError(w, http.StatusForbidden, "当前账号没有这个操作权限")
			return
		}

		next(w, r.WithContext(withClaims(r.Context(), claims)))
	}
}
