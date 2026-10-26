import type { Session } from './types';

const KEY = 'arwork.session';

/** 读取本地登录态。内容不完整时当作没登录，避免带着坏数据去请求。 */
export function loadSession(): Session | null {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as Partial<Session>;
    if (!parsed.token || !parsed.username || !parsed.role) {
      return null;
    }

    return { token: parsed.token, username: parsed.username, role: parsed.role };
  } catch {
    return null;
  }
}

export function saveSession(session: Session): void {
  localStorage.setItem(KEY, JSON.stringify(session));
}

export function clearSession(): void {
  localStorage.removeItem(KEY);
}
