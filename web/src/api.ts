import type { Issue, Status } from './types';

const BASE = (import.meta.env.VITE_API_BASE ?? 'http://localhost:8080').replace(/\/$/, '');

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;

  try {
    response = await fetch(`${BASE}${path}`, {
      headers: { 'Content-Type': 'application/json' },
      ...init,
    });
  } catch {
    // fetch 只有在网络层失败时才 reject，这里给一句人能看懂的原因
    throw new Error(`无法连接后端 ${BASE}，请确认 Go 服务已启动`);
  }

  if (!response.ok) {
    let message = `请求失败（HTTP ${response.status}）`;
    try {
      const body = (await response.json()) as { error?: string };
      if (body.error) {
        message = body.error;
      }
    } catch {
      // 响应不是 JSON，保留默认文案
    }
    throw new Error(message);
  }

  return (await response.json()) as T;
}

export function listIssues(): Promise<Issue[]> {
  return request<Issue[]>('/issues');
}

export function updateIssueStatus(id: string, status: Status): Promise<Issue> {
  return request<Issue>(`/issues/${id}/status`, {
    method: 'PATCH',
    body: JSON.stringify({ status }),
  });
}
