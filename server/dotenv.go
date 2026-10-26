package main

import (
	"bufio"
	"os"
	"strings"
)

// LoadEnvFile 读取 .env 并写入环境变量。
//
// 两条约定：
//   - 文件不存在不算错误，本地开发可以完全不建这个文件；
//   - 已经存在的环境变量不覆盖，所以部署时用系统环境变量优先，.env 只服务本地。
func LoadEnvFile(path string) error {
	file, err := os.Open(path)
	if err != nil {
		if os.IsNotExist(err) {
			return nil
		}
		return err
	}
	defer file.Close()

	scanner := bufio.NewScanner(file)
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())

		// 空行与注释
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}

		key, value, ok := strings.Cut(line, "=")
		if !ok {
			continue
		}

		key = strings.TrimSpace(key)
		if key == "" {
			continue
		}

		value = trimQuotes(strings.TrimSpace(value))

		if _, exists := os.LookupEnv(key); exists {
			continue
		}

		if err := os.Setenv(key, value); err != nil {
			return err
		}
	}

	return scanner.Err()
}

// trimQuotes 只去掉首尾成对的一对引号，不碰值内部的引号。
func trimQuotes(s string) string {
	if len(s) < 2 {
		return s
	}

	first, last := s[0], s[len(s)-1]
	if (first == '"' && last == '"') || (first == '\'' && last == '\'') {
		return s[1 : len(s)-1]
	}

	return s
}
