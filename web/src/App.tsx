import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Empty,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { LogoutOutlined, ReloadOutlined } from '@ant-design/icons';
import { listIssues, UnauthorizedError, updateIssueStatus } from './api';
import { clearSession, loadSession } from './auth';
import LoginPage from './LoginPage';
import {
  PRIORITY_LABEL,
  ROLE_LABEL,
  STATUS_LABEL,
  STATUS_ORDER,
  type Issue,
  type Priority,
  type Session,
  type Status,
} from './types';

const PRIORITY_COLOR: Record<Priority, string> = {
  high: 'red',
  medium: 'orange',
  low: 'default',
};

const STATUS_COLOR: Record<Status, string> = {
  open: 'default',
  in_progress: 'processing',
  resolved: 'success',
};

function formatTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }

  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function formatPosition(position: Issue['position']): string {
  return `${position.x.toFixed(2)}, ${position.y.toFixed(2)}, ${position.z.toFixed(2)}`;
}

interface IssuesPageProps {
  session: Session;
  onLogout: () => void;
}

function IssuesPage({ session, onLogout }: IssuesPageProps) {
  const [issues, setIssues] = useState<Issue[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [updatingId, setUpdatingId] = useState<string | null>(null);
  const [loadedAt, setLoadedAt] = useState<string | null>(null);

  const isAdmin = session.role === 'admin';

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const data = await listIssues();
      setIssues(data);
      setLoadedAt(new Date().toLocaleTimeString('zh-CN', { hour12: false }));
    } catch (err) {
      if (err instanceof UnauthorizedError) {
        onLogout();
        return;
      }

      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [onLogout]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleStatusChange = useCallback(
    async (issue: Issue, next: Status) => {
      if (next === issue.status) {
        return;
      }

      setUpdatingId(issue.id);
      setError(null);

      try {
        const updated = await updateIssueStatus(issue.id, next);
        setIssues((prev) => prev.map((item) => (item.id === updated.id ? updated : item)));
      } catch (err) {
        if (err instanceof UnauthorizedError) {
          onLogout();
          return;
        }

        // 下拉框的值来自 issues，请求失败时不改 state，选项会自己回到原值
        setError(err instanceof Error ? err.message : String(err));
      } finally {
        setUpdatingId(null);
      }
    },
    [onLogout],
  );

  const openCount = useMemo(
    () => issues.filter((issue) => issue.status === 'open').length,
    [issues],
  );

  const columns = useMemo<ColumnsType<Issue>>(() => {
    const base: ColumnsType<Issue> = [
      {
        title: '问题',
        dataIndex: 'title',
        render: (_value, issue) => (
          <div>
            <div className="cell-title">{issue.title}</div>
            {issue.description && (
              <Typography.Text type="secondary" className="cell-desc">
                {issue.description}
              </Typography.Text>
            )}
          </div>
        ),
      },
      {
        title: '优先级',
        dataIndex: 'priority',
        width: 90,
        render: (priority: Priority) => (
          <Tag color={PRIORITY_COLOR[priority]}>{PRIORITY_LABEL[priority]}</Tag>
        ),
      },
      {
        title: '状态',
        dataIndex: 'status',
        width: 100,
        render: (status: Status) => (
          <Tag color={STATUS_COLOR[status]}>{STATUS_LABEL[status]}</Tag>
        ),
      },
      {
        title: 'AR 坐标',
        dataIndex: 'position',
        width: 170,
        render: (position: Issue['position']) => (
          <Typography.Text code>{formatPosition(position)}</Typography.Text>
        ),
      },
      {
        title: '创建时间',
        dataIndex: 'createdAt',
        width: 110,
        render: (iso: string) => (
          <Tooltip title={new Date(iso).toLocaleString('zh-CN')}>
            <Typography.Text type="secondary">{formatTime(iso)}</Typography.Text>
          </Tooltip>
        ),
      },
    ];

    // 管理员才需要知道是谁报的、才需要处置；巡检员看自己的列表，这两列没有意义
    if (isAdmin) {
      base.splice(3, 0, {
        title: '上报人',
        dataIndex: 'reporter',
        width: 110,
        render: (reporter: string) =>
          reporter ? (
            <Typography.Text>{reporter}</Typography.Text>
          ) : (
            <Typography.Text type="secondary">（早期记录）</Typography.Text>
          ),
      });

      base.push({
        title: '处置',
        key: 'action',
        width: 120,
        // 固定在右侧，不然窄屏上这一列会被横向滚动区裁掉，最常用的操作反而看不见
        fixed: 'right',
        render: (_value, issue) => (
          <Select
            size="small"
            value={issue.status}
            loading={updatingId === issue.id}
            disabled={updatingId === issue.id}
            options={STATUS_ORDER.map((status) => ({
              value: status,
              label: STATUS_LABEL[status],
            }))}
            onChange={(value: Status) => void handleStatusChange(issue, value)}
            style={{ width: 104 }}
          />
        ),
      });
    }

    return base;
  }, [handleStatusChange, isAdmin, updatingId]);

  const emptyText = isAdmin
    ? '暂无问题。在手机端对准平面标记并提交后，问题会出现在这里'
    : '你还没有上报过问题';

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <Typography.Title level={3} className="page-title">
            园区巡检 · 问题处置
          </Typography.Title>
          <Typography.Text type="secondary">
            {isAdmin
              ? '数据来自 Go 后端，与手机端 AR 标记共用同一份记录'
              : '这里是你在手机端上报过的问题'}
          </Typography.Text>
        </div>

        <Space size="middle" wrap>
          <Tag color="geekblue">
            {session.username} · {ROLE_LABEL[session.role]}
          </Tag>
          <Tag color="blue">共 {issues.length} 条</Tag>
          <Tag color="orange">待处理 {openCount} 条</Tag>
          {loadedAt && <Typography.Text type="secondary">刷新于 {loadedAt}</Typography.Text>}
          <Button icon={<ReloadOutlined />} onClick={() => void load()} loading={loading}>
            刷新
          </Button>
          <Button icon={<LogoutOutlined />} onClick={onLogout}>
            退出
          </Button>
        </Space>
      </header>

      {error && (
        <Alert
          type="error"
          showIcon
          message={error}
          action={
            <Button size="small" onClick={() => void load()}>
              重试
            </Button>
          }
          className="error-alert"
        />
      )}

      <Table<Issue>
        rowKey="id"
        columns={columns}
        dataSource={issues}
        loading={loading && issues.length === 0}
        pagination={false}
        scroll={{ x: 860 }}
        locale={{ emptyText: <Empty description={emptyText} /> }}
      />

      <footer className="page-footer">手机端 Unity AR · Go 后端 · React 管理端 —— 同一份数据</footer>
    </div>
  );
}

export default function App() {
  const [session, setSession] = useState<Session | null>(() => loadSession());

  if (!session) {
    return <LoginPage onLogin={setSession} />;
  }

  return (
    <IssuesPage
      session={session}
      onLogout={() => {
        clearSession();
        setSession(null);
      }}
    />
  );
}
