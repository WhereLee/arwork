import { clearSession, loadSession } from './auth';
import type { Issue, Session, Status } from './types';

const BASE = (import.meta.env.VITE_API_BASE ?? 'http://localhost:8080').replace(/\/$/, '');

/** 后端返回 401 时抛这个，调用方据此清掉登录态并回登录页。 */
export class UnauthorizedError extends Error {}

async function readErrorMessage(response: Response): Promise<string> {
  let message = `请求失败（HTTP ${response.status}）`;

  try {
    const body = (await response.json()) as { error?: string };
    if (body.error) {
      message = body.error;
    }
  } catch {
    // 响应不是 JSON，保留默认文案
  }

  return message;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const session = loadSession();
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };

  if (session) {
    headers.Authorization = `Bearer ${session.token}`;
  }

  let response: Response;

  try {
    response = await fetch(`${BASE}${path}`, { ...init, headers });
  } catch {
    // fetch 只有在网络层失败时才 reject，这里给一句人能看懂的原因
    throw new Error(`无法连接后端 ${BASE}，请确认 Go 服务已启动`);
  }

  if (response.status === 401) {
    clearSession();
    throw new UnauthorizedError(await readErrorMessage(response));
  }

  if (!response.ok) {
    throw new Error(await readErrorMessage(response));
  }

  return (await response.json()) as T;
}

/** 登录请求不带 token，单独实现：此时本来就没有会话，401 不该触发清登录态。 */
export async function login(username: string, password: string): Promise<Session> {
  let response: Response;

  try {
    response = await fetch(`${BASE}/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });
  } catch {
    throw new Error(`无法连接后端 ${BASE}，请确认 Go 服务已启动`);
  }

  if (!response.ok) {
    throw new Error(await readErrorMessage(response));
  }

  return (await response.json()) as Session;
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
