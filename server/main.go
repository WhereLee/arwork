package main

import (
	"log"
	"net/http"
	"os"
	"time"
)

func main() {
	addr := envOr("ADDR", ":8080")
	dbPath := envOr("DB_PATH", "data/issues.db")

	if err := os.MkdirAll("data", 0o755); err != nil {
		log.Fatalf("创建数据目录失败: %v", err)
	}

	store, err := NewStore(dbPath)
	if err != nil {
		log.Fatalf("打开数据库失败: %v", err)
	}
	defer store.Close()

	if err := seedUsers(store); err != nil {
		log.Fatalf("初始化账号失败: %v", err)
	}

	api := &API{store: store}

	log.Printf("巡检问题服务已启动 http://localhost%s （数据库 %s）", addr, dbPath)
	if err := http.ListenAndServe(addr, api.Routes()); err != nil {
		log.Fatalf("服务异常退出: %v", err)
	}
}

func envOr(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

// seedUsers 只在账号不存在时写入，重启不会把改过的密码盖回去。
func seedUsers(store *Store) error {
	seeds := []struct {
		username string
		password string
		role     string
	}{
		{"admin", "admin123", RoleAdmin},
		{"inspector1", "inspector123", RoleInspector},
		{"inspector2", "inspector123", RoleInspector},
		{"inspector3", "inspector123", RoleInspector},
	}

	now := time.Now()
	for _, seed := range seeds {
		if _, found, err := store.FindUser(seed.username); err != nil {
			return err
		} else if found {
			continue
		}

		user, err := NewUser(seed.username, seed.password, seed.role)
		if err != nil {
			return err
		}

		if err := store.EnsureUser(user, now); err != nil {
			return err
		}
	}

	return nil
}
