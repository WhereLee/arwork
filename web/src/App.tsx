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
import { ReloadOutlined } from '@ant-design/icons';
import { listIssues, updateIssueStatus } from './api';
import {
  PRIORITY_LABEL,
  STATUS_LABEL,
  STATUS_ORDER,
  type Issue,
  type Priority,
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

  const handleStatusChange = useCallback(async (issue: Issue, next: Status) => {
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
  }, []);

  const openCount = useMemo(
    () => issues.filter((issue) => issue.status === 'open').length,
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
        width: 200,
        render: (position: Issue['position']) => (
          <Typography.Text code>{formatPosition(position)}</Typography.Text>
        ),
      },
      {
        title: '创建时间',
        dataIndex: 'createdAt',
        width: 120,
        render: (iso: string) => (
          <Tooltip title={new Date(iso).toLocaleString('zh-CN')}>
            <Typography.Text type="secondary">{formatTime(iso)}</Typography.Text>
          </Tooltip>
        ),
      },
      {
        title: '处置',
        key: 'action',
        width: 120,
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
      },
    ],
    [handleStatusChange, updatingId],
  );

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <Typography.Title level={3} className="page-title">
            园区巡检 · 问题处置
          </Typography.Title>
          <Typography.Text type="secondary">
            数据来自 Go 后端，与手机端 AR 标记共用同一份记录
          </Typography.Text>
        </div>

        <Space size="middle" wrap>
          <Tag color="blue">共 {issues.length} 条</Tag>
          <Tag color="orange">待处理 {openCount} 条</Tag>
          {loadedAt && <Typography.Text type="secondary">刷新于 {loadedAt}</Typography.Text>}
          <Button icon={<ReloadOutlined />} onClick={() => void load()} loading={loading}>
            刷新
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
        locale={{
          emptyText: (
            <Empty description="暂无问题。在手机端对准平面标记并提交后，问题会出现在这里" />
          ),
        }}
      />

      <footer className="page-footer">手机端 Unity AR · Go 后端 · React 管理端 —— 同一份数据</footer>
    </div>
  );
}
