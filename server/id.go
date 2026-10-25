package main

import (
	"crypto/rand"
	"encoding/hex"
)

// newID 生成 32 位十六进制的唯一标识。
func newID() (string, error) {
	buf := make([]byte, 16)
	if _, err := rand.Read(buf); err != nil {
		return "", err
	}

	return hex.EncodeToString(buf), nil
}
