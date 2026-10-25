import { useCallback, useEffect, useMemo, useState } from 'react';
import { listIssues, updateIssueStatus } from './api';
import {
  PRIORITY_LABEL,
  STATUS_LABEL,
  STATUS_ORDER,
  type Issue,
  type Status,
} from './types';

function formatTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }

  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function formatPosition(position: Issue['position']): string {
  return `x ${position.x.toFixed(2)} · y ${position.y.toFixed(2)} · z ${position.z.toFixed(2)}`;
}

export default function App() {
  const [issues, setIssues] = useState<Issue[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [updatingId, setUpdatingId] = useState<string | null>(null);
  const [loadedAt, setLoadedAt] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const data = await listIssues();
      setIssues(data);
      setLoadedAt(new Date().toLocaleTimeString('zh-CN', { hour12: false }));
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const openCount = useMemo(
    () => issues.filter((issue) => issue.status === 'open').length,
    [issues],
  );

  async function handleStatusChange(issue: Issue, next: Status) {
    if (next === issue.status) {
      return;
    }

    setUpdatingId(issue.id);
    setError(null);

    try {
      const updated = await updateIssueStatus(issue.id, next);
      setIssues((prev) => prev.map((item) => (item.id === updated.id ? updated : item)));
    } catch (err) {
      // 下拉框的 value 来自 issues，请求失败时不改 state，选项会自己回到原值
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setUpdatingId(null);
    }
  }

  return (
    <div className="page">
      <header className="topbar">
        <div>
          <h1>园区巡检 · 问题处置</h1>
          <p className="subtitle">数据来自 Go 后端，与手机端 AR 标记共用同一份记录</p>
        </div>
        <div className="meta">
          <span className="counter">共 {issues.length} 条</span>
          <span className="counter counter-open">待处理 {openCount} 条</span>
          {loadedAt && !error && <span className="loaded-at">刷新于 {loadedAt}</span>}
          <button className="refresh" onClick={() => void load()} disabled={loading}>
            {loading ? '刷新中…' : '刷新'}
          </button>
        </div>
      </header>

      {error && (
        <div className="banner" role="alert">
          <span>{error}</span>
          <button onClick={() => void load()}>重试</button>
        </div>
      )}

      {loading && issues.length === 0 ? (
        <div className="placeholder">正在读取问题列表…</div>
      ) : issues.length === 0 ? (
        <div className="placeholder">
          <strong>暂无问题</strong>
          <span>在手机端对准平面标记并提交后，问题会出现在这里</span>
        </div>
      ) : (
        <ul className="issue-list">
          {issues.map((issue) => (
            <li
              key={issue.id}
              className={updatingId === issue.id ? 'issue issue-updating' : 'issue'}
            >
              <div className="issue-main">
                <h2 className="issue-title">{issue.title}</h2>
                {issue.description && <p className="issue-desc">{issue.description}</p>}
                <div className="issue-meta">
                  <span className="position">{formatPosition(issue.position)}</span>
                  <span className="time" title={issue.createdAt}>
                    建于 {formatTime(issue.createdAt)}
                  </span>
                  {issue.updatedAt !== issue.createdAt && (
                    <span className="time" title={issue.updatedAt}>
                      更新于 {formatTime(issue.updatedAt)}
                    </span>
                  )}
                </div>
              </div>

              <div className="issue-side">
                <span className={`badge priority-${issue.priority}`} title={issue.priority}>
                  {PRIORITY_LABEL[issue.priority]}
                </span>
                <span className={`badge status-${issue.status}`} title={issue.status}>
                  {STATUS_LABEL[issue.status]}
                </span>
                <span className="side-divider" aria-hidden="true" />
                <select
                  className="status-select"
                  value={issue.status}
                  disabled={updatingId === issue.id}
                  onChange={(event) => void handleStatusChange(issue, event.target.value as Status)}
                >
                  {STATUS_ORDER.map((status) => (
                    <option key={status} value={status}>
                      {STATUS_LABEL[status]}
                    </option>
                  ))}
                </select>
              </div>
            </li>
          ))}
        </ul>
      )}

      <footer className="footer">手机端 Unity AR · Go 后端 · React 管理端 —— 同一份数据</footer>
    </div>
  );
}
