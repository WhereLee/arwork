import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Empty,
  Space,
  Table,
  Tabs,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { LogoutOutlined, ReloadOutlined } from '@ant-design/icons';
import { listIssues, listUsers, UnauthorizedError } from './api';
import { clearSession, loadSession } from './auth';
import { formatPosition, formatTime } from './format';
import IssueDrawer from './IssueDrawer';
import LoginPage from './LoginPage';
import UsersPage from './UsersPage';
import {
  CATEGORY_LABEL,
  PRIORITY_LABEL,
  ROLE_LABEL,
  STATUS_LABEL,
  type Issue,
  type Priority,
  type Session,
  type UserView,
} from './types';

const PRIORITY_COLOR: Record<Priority, string> = {
  high: 'red',
  medium: 'orange',
  low: 'default',
};

const STATUS_COLOR: Record<string, string> = {
  open: 'default',
  in_progress: 'processing',
  pending_review: 'warning',
  resolved: 'success',
};

interface IssuesTabProps {
  isAdmin: boolean;
  session: Session;
  onLogout: () => void;
}

function IssuesTab({ isAdmin, session, onLogout }: IssuesTabProps) {
  const [issues, setIssues] = useState<Issue[]>([]);
  const [users, setUsers] = useState<UserView[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [loadedAt, setLoadedAt] = useState<string | null>(null);
  const [drawerId, setDrawerId] = useState<string | null>(null);

  const guard = useCallback(
    (err: unknown) => {
      if (err instanceof UnauthorizedError) {
        onLogout();
        return;
      }
      setError(err instanceof Error ? err.message : String(err));
    },
    [onLogout],
  );

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const data = await listIssues();
      setIssues(data);
      setLoadedAt(new Date().toLocaleTimeString('zh-CN', { hour12: false }));

      // 派单要在抽屉里选人，顺手把人员也拉一份
      if (isAdmin) {
        setUsers(await listUsers());
      }
    } catch (err) {
      guard(err);
    } finally {
      setLoading(false);
    }
  }, [guard, isAdmin]);

  useEffect(() => {
    void load();
  }, [load]);

  const pendingCount = useMemo(
    () => issues.filter((i) => i.status === 'open').length,
    [issues],
  );

  const waitingReview = useMemo(
    () => issues.filter((i) => i.status === 'pending_review').length,
    [issues],
  );

  const columns = useMemo<ColumnsType<Issue>>(
    () => [
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
        title: '类型',
        dataIndex: 'category',
        width: 90,
        render: (category: string) =>
          category ? (
            <Tag color="blue">{CATEGORY_LABEL[category] ?? category}</Tag>
          ) : (
            <Typography.Text type="secondary">未判定</Typography.Text>
          ),
      },
      {
        title: '优先级',
        dataIndex: 'priority',
        width: 80,
        render: (priority: Priority) => (
          <Tag color={PRIORITY_COLOR[priority]}>{PRIORITY_LABEL[priority]}</Tag>
        ),
      },
      {
        title: '状态',
        dataIndex: 'status',
        width: 90,
        render: (status: string) => (
          <Tag color={STATUS_COLOR[status]}>{STATUS_LABEL[status as keyof typeof STATUS_LABEL] ?? status}</Tag>
        ),
      },
      {
        title: '处理人',
        dataIndex: 'assignee',
        width: 140,
        // 待处理还带着处理人，说明这条被退回过。单独标出来，不然会以为派单没生效
        render: (assignee: string, issue) => {
          if (!assignee) {
            return <Typography.Text type="secondary">未指派</Typography.Text>;
          }

          return (
            <Space size={4}>
              <span>{assignee}</span>
              {issue.status === 'open' && (
                <Typography.Text type="secondary">（退回）</Typography.Text>
              )}
            </Space>
          );
        },
      },
      {
        title: 'AR 坐标',
        dataIndex: 'position',
        width: 160,
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
    ],
    [],
  );

  return (
    <>
      <div className="page-header">
        <div>
          <Typography.Title level={3} className="page-title">
            园区巡检 · 工单
          </Typography.Title>
          <Typography.Text type="secondary">
            {isAdmin
              ? '点任意一行看详情、派单和验收；待派单的行有底色标记'
              : '这里是你上报过的问题'}
          </Typography.Text>
        </div>

        <Space size="middle" wrap>
          <Tag color="geekblue">
            {session.username} · {ROLE_LABEL[session.role]}
          </Tag>
          <Tag color="blue">共 {issues.length} 条</Tag>
          <Tag color="orange">待派单 {pendingCount}</Tag>
          <Tag color="magenta">待确认 {waitingReview}</Tag>
          {loadedAt && <Typography.Text type="secondary">刷新于 {loadedAt}</Typography.Text>}
          <Button icon={<ReloadOutlined />} onClick={() => void load()} loading={loading}>
            刷新
          </Button>
          <Button icon={<LogoutOutlined />} onClick={onLogout}>
            退出
          </Button>
        </Space>
      </div>

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
        scroll={{ x: 900 }}
        rowClassName={(record) => (record.status === 'open' ? 'row-pending' : '')}
        onRow={(record) => ({ onClick: () => setDrawerId(record.id) })}
        locale={{
          emptyText: (
            <Empty
              description={
                isAdmin
                  ? '暂无工单。手机端对准平面标记并提交后，工单会出现在这里'
                  : '你还没有上报过问题'
              }
            />
          ),
        }}
      />

      <IssueDrawer
        issueId={drawerId}
        users={users}
        canOperate={isAdmin}
        onClose={() => setDrawerId(null)}
        onChanged={() => void load()}
      />

      <footer className="page-footer">手机端 Unity AR · Go 后端 · React 管理端 —— 同一份数据</footer>
    </>
  );
}

interface WorkspaceProps {
  session: Session;
  onLogout: () => void;
}

function Workspace({ session, onLogout }: WorkspaceProps) {
  const isAdmin = session.role === 'admin';
  const [tab, setTab] = useState('issues');

  // 巡检员和修复者只有工单一页，不显示人员管理
  const items = isAdmin
    ? [
        { key: 'issues', label: '工单', children: <IssuesTab isAdmin session={session} onLogout={onLogout} /> },
        { key: 'users', label: '人员', children: <UsersPage onLogout={onLogout} /> },
      ]
    : [
        {
          key: 'issues',
          label: '我的工单',
          children: <IssuesTab isAdmin={false} session={session} onLogout={onLogout} />,
        },
      ];

  return (
    <div className="page">
      {isAdmin ? (
        <Tabs activeKey={tab} onChange={setTab} items={items} destroyOnHidden />
      ) : (
        items[0].children
      )}
    </div>
  );
}

export default function App() {
  const [session, setSession] = useState<Session | null>(() => loadSession());

  if (!session) {
    return <LoginPage onLogin={setSession} />;
  }

  return (
    <Workspace
      session={session}
      onLogout={() => {
        clearSession();
        setSession(null);
      }}
    />
  );
}
