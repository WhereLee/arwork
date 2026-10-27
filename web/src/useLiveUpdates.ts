import { useEffect, useRef, useState } from 'react';

export type LiveStatus = 'connecting' | 'live' | 'offline';

/**
 * 订阅后端的工单变更通知。
 *
 * 后端只推「有东西变了」，不推具体内容——三个角色能看的范围不同，
 * 推内容就得按人过滤，推空壳让调用方按原有权限重拉最简单。
 * 断线后指数退避重连，最长等 15 秒。
 */
export function useLiveUpdates(
  token: string | null,
  onChange: () => void,
): LiveStatus {
  const [status, setStatus] = useState<LiveStatus>('connecting');

  // 用 ref 存回调：重连时不该因为父组件重新渲染就重建连接
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  useEffect(() => {
    if (!token) {
      setStatus('offline');
      return;
    }

    let socket: WebSocket | null = null;
    let retry = 0;
    let timer: number | undefined;
    let stopped = false;

    const connect = () => {
      if (stopped) {
        return;
      }

      setStatus('connecting');

      // 前端经 nginx 访问，用当前页面的协议和 host 拼出同源地址
      const scheme = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
      const url = `${scheme}//${window.location.host}/api/ws?token=${encodeURIComponent(token)}`;

      socket = new WebSocket(url);

      socket.onopen = () => {
        retry = 0;
        setStatus('live');
      };

      socket.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data) as { type?: string };
          if (data.type === 'issues_changed') {
            onChangeRef.current();
          }
        } catch {
          // 不认识的消息直接忽略，不要因为一条脏数据把连接搞崩
        }
      };

      socket.onclose = () => {
        if (stopped) {
          return;
        }

        setStatus('offline');

        // 1s、2s、4s…最多 15s。后端重启时大家不会同时猛敲
        const delay = Math.min(1000 * 2 ** retry, 15000);
        retry += 1;
        timer = window.setTimeout(connect, delay);
      };

      socket.onerror = () => {
        socket?.close();
      };
    };

    connect();

    return () => {
      stopped = true;
      window.clearTimeout(timer);
      socket?.close();
    };
  }, [token]);

  return status;
}
