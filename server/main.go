package main

import (
	"log"
	"net/http"
	"os"
	"time"
)

func main() {
	// 本地开发从 .env 读配置；文件不存在或已存在同名环境变量都不算错
	if err := LoadEnvFile(envOr("ENV_FILE", ".env")); err != nil {
		log.Printf("读取 .env 失败: %v", err)
	}

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

	api := &API{store: store, ai: LoadAIConfig()}

	log.Printf("巡检问题服务已启动 http://localhost%s （数据库 %s，AI 分诊 %s）",
		addr, dbPath, enabledLabel(api.ai.Enabled()))
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

func enabledLabel(enabled bool) string {
	if enabled {
		return "已启用"
	}
	return "未配置，全部转人工"
}

// seedUsers 只在账号不存在时写入，重启不会把改过的密码盖回去。
func seedUsers(store *Store) error {
	seeds := []struct {
		username string
		password string
		role     string
		skills   []string
	}{
		{"admin", "admin123", RoleAdmin, nil},
		{"inspector1", "inspector123", RoleInspector, nil},
		{"inspector2", "inspector123", RoleInspector, nil},
		{"inspector3", "inspector123", RoleInspector, nil},
		{"repairer1", "repairer123", RoleRepairer, []string{CategoryWall}},
		{"repairer2", "repairer123", RoleRepairer, []string{CategoryDevice}},
		{"repairer3", "repairer123", RoleRepairer, []string{CategorySafety}},
		{"repairer4", "repairer123", RoleRepairer, []string{CategoryWall, CategorySafety}},
	}

	now := time.Now()
	for _, seed := range seeds {
		if _, found, err := store.FindUser(seed.username); err != nil {
			return err
		} else if found {
			continue
		}

		user, err := NewUser(seed.username, seed.password, seed.role, seed.skills)
		if err != nil {
			return err
		}

		if err := store.EnsureUser(user, now); err != nil {
			return err
		}
	}

	return nil
}
