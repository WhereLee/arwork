package main

import (
	"encoding/json"
	"log"
	"net"
	"net/http"
	"net/url"
	"sync"
	"time"

	"github.com/gorilla/websocket"
)

// wsMessage 只告诉客户端「有东西变了」，不推具体内容。
// 三个角色能看的工单范围不一样，推内容就得按人过滤；
// 推「变了」让客户端自己按原有权限重拉，简单且不会串数据。
type wsMessage struct {
	Type    string `json:"type"`
	IssueID string `json:"issueId,omitempty"`
}

// Hub 维护所有在线连接。
type Hub struct {
	mu      sync.Mutex
	clients map[*wsClient]struct{}
}

func NewHub() *Hub {
	return &Hub{clients: make(map[*wsClient]struct{})}
}

// Publish 广播一条变更通知。发不出去的连接直接踢掉，
// 绝不阻塞——否则一个卡死的手机能把所有人拖住。
func (h *Hub) Publish(issueID string) {
	if h == nil {
		return
	}

	payload, err := json.Marshal(wsMessage{Type: "issues_changed", IssueID: issueID})
	if err != nil {
		return
	}

	h.mu.Lock()
	targets := make([]*wsClient, 0, len(h.clients))
	for c := range h.clients {
		targets = append(targets, c)
	}
	h.mu.Unlock()

	for _, c := range targets {
		select {
		case c.send <- payload:
		default:
			// 这个客户端积压了三条以上还没消化，断开让它重连
			h.remove(c)
		}
	}
}

func (h *Hub) add(c *wsClient) int {
	h.mu.Lock()
	defer h.mu.Unlock()

	h.clients[c] = struct{}{}
	return len(h.clients)
}

func (h *Hub) remove(c *wsClient) {
	h.mu.Lock()
	defer h.mu.Unlock()

	if _, ok := h.clients[c]; !ok {
		return
	}

	delete(h.clients, c)
	close(c.send)
}

func (h *Hub) count() int {
	h.mu.Lock()
	defer h.mu.Unlock()

	return len(h.clients)
}

type wsClient struct {
	conn *websocket.Conn
	send chan []byte
}

var upgrader = websocket.Upgrader{
	ReadBufferSize:  1024,
	WriteBufferSize: 1024,
	// 只允许同源：前端经 nginx 代理过来，Origin 和 Host 应该指向同一台主机。
	// 只比主机名不比端口：nginx 的 proxy_set_header Host $host 不带端口，
	// 而浏览器的 Origin 带着 :9400，直接比 Host 会永远不相等。
	// 非浏览器客户端（curl、测试）不带 Origin，这里放行。
	CheckOrigin: func(r *http.Request) bool {
		origin := r.Header.Get("Origin")
		if origin == "" {
			return true
		}

		parsed, err := url.Parse(origin)
		if err != nil {
			return false
		}

		requestHost := r.Host
		if host, _, err := net.SplitHostPort(requestHost); err == nil {
			requestHost = host
		}

		return parsed.Hostname() == requestHost
	},
}

// handleWS 升级成 WebSocket 并保持连接。
// 浏览器没法给 WebSocket 设 Authorization 头，所以 token 走查询参数。
func (a *API) handleWS(w http.ResponseWriter, r *http.Request) {
	claims, err := ParseToken(r.URL.Query().Get("token"), time.Now())
	if err != nil {
		writeError(w, http.StatusUnauthorized, ErrInvalidToken.Error())
		return
	}

	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		// Upgrade 失败时自己已经写过响应了，这里只记日志
		log.Printf("[ws] 升级失败: %v", err)
		return
	}

	client := &wsClient{conn: conn, send: make(chan []byte, 8)}
	online := a.hub.add(client)
	log.Printf("[ws] %s（%s）接入，当前在线 %d", claims.Username, claims.Role, online)

	go client.writeLoop()

	// 客户端不会发业务消息，读循环只用来感知断开
	conn.SetReadLimit(512)
	conn.SetReadDeadline(time.Now().Add(60 * time.Second))
	conn.SetPongHandler(func(string) error {
		return conn.SetReadDeadline(time.Now().Add(60 * time.Second))
	})

	for {
		if _, _, err := conn.ReadMessage(); err != nil {
			break
		}
	}

	a.hub.remove(client)
	conn.Close()
	log.Printf("[ws] %s 断开，当前在线 %d", claims.Username, a.hub.count())
}

// writeLoop 单独一个 goroutine 负责写，避免心跳和业务消息并发写坏帧。
func (c *wsClient) writeLoop() {
	ticker := time.NewTicker(25 * time.Second)
	defer func() {
		ticker.Stop()
		c.conn.Close()
	}()

	for {
		select {
		case msg, ok := <-c.send:
			if !ok {
				// Hub 已经把这个连接踢了
				c.conn.WriteMessage(websocket.CloseMessage, []byte{})
				return
			}

			if err := c.conn.WriteMessage(websocket.TextMessage, msg); err != nil {
				return
			}
		case <-ticker.C:
			// 25 秒一次心跳，比 nginx 默认 60 秒超时长，连接不会被中间层掐断
			if err := c.conn.WriteMessage(websocket.PingMessage, nil); err != nil {
				return
			}
		}
	}
}
