package main

import (
	"log"
	"net/http"
	"os"
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
