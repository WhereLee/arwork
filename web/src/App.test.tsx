import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { listIssues, listUsers } from './api';
import { loadSession } from './auth';

// 不打真网络：接口和会话都换成可控的假实现。
vi.mock('./api', () => ({
  listIssues: vi.fn(),
  listUsers: vi.fn(),
  UnauthorizedError: class UnauthorizedError extends Error {},
}));

vi.mock('./auth', () => ({
  loadSession: vi.fn(),
  clearSession: vi.fn(),
}));

const adminSession = {
  username: 'admin',
  role: 'admin',
  token: 'fake-token',
  specialties: [] as string[],
};

const sampleIssue = {
  id: 'abc123',
  title: '二号楼东侧墙面裂缝',
  description: '雨水渗入导致内墙起皮',
  priority: 'high',
  status: 'in_progress',
  position: { x: 1, y: 0, z: 2 },
  reporter: 'inspector1',
  createdAt: '2026-10-28T00:00:00Z',
  updatedAt: '2026-10-28T00:00:00Z',
  category: 'wall',
  assignee: 'repairer1',
};

describe('管理端', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('没有登录态时显示登录页，不是空白', () => {
    vi.mocked(loadSession).mockReturnValue(null);

    render(<App />);

    expect(screen.getByRole('button', { name: /登\s*录/ })).toBeInTheDocument();
  });

  it('登录后把工单渲染成列表', async () => {
    vi.mocked(loadSession).mockReturnValue(adminSession as never);
    vi.mocked(listIssues).mockResolvedValue([sampleIssue] as never);
    vi.mocked(listUsers).mockResolvedValue([]);

    render(<App />);

    // 标题、优先级、状态三列是任务书 3.2 的硬要求
    expect(await screen.findByText('二号楼东侧墙面裂缝')).toBeInTheDocument();
    expect(screen.getByText('高')).toBeInTheDocument();
    expect(screen.getByText('处理中')).toBeInTheDocument();
  });

  // 任务书 3.2 第 4 条：后端不可用时要给提示，不能白屏
  it('后端出错时显示错误提示而不是白屏', async () => {
    vi.mocked(loadSession).mockReturnValue(adminSession as never);
    vi.mocked(listIssues).mockRejectedValue(new Error('连不上后端'));
    vi.mocked(listUsers).mockResolvedValue([]);

    render(<App />);

    expect(await screen.findByText(/连不上后端/)).toBeInTheDocument();
  });
});
