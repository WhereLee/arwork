import { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Button,
  Descriptions,
  Drawer,
  Input,
  Modal,
  Select,
  Space,
  Spin,
  Tag,
  Timeline,
  Typography,
} from 'antd';
import { getIssue, performAction, UnauthorizedError } from './api';
import { formatDateTime, formatPosition, formatTime } from './format';
import {
  ACTION_LABEL,
  CATEGORY_LABEL,
  PRIORITY_LABEL,
  STATUS_LABEL,
  type IssueDetail,
  type UserView,
} from './types';

const STATUS_COLOR: Record<string, string> = {
  open: 'default',
  in_progress: 'processing',
  pending_review: 'warning',
  resolved: 'success',
};

const PRIORITY_COLOR: Record<string, string> = {
  high: 'red',
  medium: 'orange',
  low: 'default',
};

/** 事件在时间线上的颜色：失败和退回红、AI 蓝、通过绿、其余灰。 */
function eventColor(action: string): string {
  switch (action) {
    case 'ai_failed':
    case 'rejected':
    case 'returned':
      return 'red';
    case 'ai_dispatched':
      return 'blue';
    case 'approved':
      return 'green';
    case 'closed':
      return 'gray';
    default:
      return 'gray';
  }
}

type ActionKey = 'dispatch' | 'close' | 'approve' | 'reject' | 'reopen';

interface ActionMeta {
  label: string;
  needAssignee: boolean;
  needNote: boolean;
  danger?: boolean;
}

/** 按当前状态决定能做什么。改派和派单在接口上是同一个 action。 */
function availableActions(status: string): ActionKey[] {
  switch (status) {
    case 'open':
      return ['dispatch', 'close'];
    case 'in_progress':
      return ['dispatch', 'close'];
    case 'pending_review':
      return ['approve', 'reject'];
    case 'resolved':
      return ['reopen'];
    default:
      return [];
  }
}

function actionMeta(key: ActionKey, status: string): ActionMeta {
  switch (key) {
    case 'dispatch':
      return {
        label: status === 'in_progress' ? '改派' : '派单',
        needAssignee: true,
        needNote: status === 'in_progress',
      };
    case 'close':
      return { label: '关闭工单', needAssignee: false, needNote: true, danger: true };
    case 'approve':
      return { label: '验收通过', needAssignee: false, needNote: false };
    case 'reject':
      return { label: '驳回', needAssignee: false, needNote: true, danger: true };
    case 'reopen':
      return { label: '重开', needAssignee: false, needNote: true };
  }
}

interface Props {
  issueId: string | null;
  users: UserView[];
  canOperate: boolean;
  onClose: () => void;
  onChanged: () => void;
}

export default function IssueDrawer({ issueId, users, canOperate, onClose, onChanged }: Props) {
  const [detail, setDetail] = useState<IssueDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [pending, setPending] = useState<ActionKey | null>(null);
  const [assignee, setAssignee] = useState<string>('');
  const [note, setNote] = useState<string>('');
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async () => {
    if (!issueId) {
      return;
    }

    setLoading(true);
    setError(null);

    try {
      setDetail(await getIssue(issueId));
    } catch (err) {
      if (err instanceof UnauthorizedError) {
        onClose();
        return;
      }
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [issueId, onClose]);

  useEffect(() => {
    setDetail(null);
    setPending(null);
    setAssignee('');
    setNote('');
    void load();
  }, [load]);

  const submit = async () => {
    if (!issueId || !pending) {
      return;
    }

    const meta = actionMeta(pending, detail?.issue.status ?? '');

    if (meta.needAssignee && !assignee) {
      setError('请选择处理人');
      return;
    }

    if (meta.needNote && !note.trim()) {
      setError('请填写原因');
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      const result = await performAction(issueId, {
        action: pending,
        assignee: meta.needAssignee ? assignee : undefined,
        note: note.trim(),
      });

      setDetail(result);
      setPending(null);
      setAssignee('');
      setNote('');
      onChanged();
    } catch (err) {
      if (err instanceof UnauthorizedError) {
        onClose();
        return;
      }
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmitting(false);
    }
  };

  const issue = detail?.issue;
  const repairers = users.filter((u) => u.role === 'repairer' && u.active);
  const meta = pending && issue ? actionMeta(pending, issue.status) : null;

  return (
    <Drawer
      title={issue ? issue.title : '工单详情'}
      width={560}
      open={Boolean(issueId)}
      onClose={onClose}
      destroyOnClose
    >
      {error && <Alert type="error" showIcon message={error} className="error-alert" />}

      {loading && !detail && (
        <div className="drawer-loading">
          <Spin />
        </div>
      )}

      {issue && (
        <>
          <Descriptions size="small" column={1} bordered>
            <Descriptions.Item label="状态">
              <Tag color={STATUS_COLOR[issue.status]}>{STATUS_LABEL[issue.status]}</Tag>
            </Descriptions.Item>
            <Descriptions.Item label="优先级">
              <Tag color={PRIORITY_COLOR[issue.priority]}>{PRIORITY_LABEL[issue.priority]}</Tag>
            </Descriptions.Item>
            <Descriptions.Item label="AI 判定">
              {issue.category ? (
                <Space size={6} wrap>
                  <Tag color="blue">{CATEGORY_LABEL[issue.category] ?? issue.category}</Tag>
                  <Typography.Text type="secondary">
                    把握 {issue.aiConfidence.toFixed(2)}
                  </Typography.Text>
                  {issue.aiReason && (
                    <Typography.Text type="secondary">· {issue.aiReason}</Typography.Text>
                  )}
                </Space>
              ) : (
                <Typography.Text type="secondary">未判定，等人工派单</Typography.Text>
              )}
            </Descriptions.Item>
            <Descriptions.Item label="处理人">
              {issue.assignee || <Typography.Text type="secondary">未指派</Typography.Text>}
              {issue.dispatchMode && (
                <Typography.Text type="secondary">
                  （{issue.dispatchMode === 'ai' ? 'AI 派单' : '人工派单'}）
                </Typography.Text>
              )}
            </Descriptions.Item>
            <Descriptions.Item label="上报人">{issue.reporter}</Descriptions.Item>
            <Descriptions.Item label="描述">
              {issue.description || <Typography.Text type="secondary">未填写</Typography.Text>}
            </Descriptions.Item>
            <Descriptions.Item label="AR 坐标">
              <Typography.Text code>{formatPosition(issue.position)}</Typography.Text>
            </Descriptions.Item>
            <Descriptions.Item label="创建时间">{formatDateTime(issue.createdAt)}</Descriptions.Item>
          </Descriptions>

          {issue.repairNote && (
            <>
              <Typography.Title level={5} className="drawer-section">
                处理记录
              </Typography.Title>
              <Typography.Paragraph className="repair-note">
                {issue.repairNote}
              </Typography.Paragraph>
            </>
          )}

          <Typography.Title level={5} className="drawer-section">
            流转记录
          </Typography.Title>

          {detail.events.length === 0 ? (
            <Typography.Text type="secondary">暂无记录</Typography.Text>
          ) : (
            <Timeline
              items={detail.events.map((e) => ({
                color: eventColor(e.action),
                children: (
                  <div>
                    <Space size={6} wrap>
                      <Tag>{ACTION_LABEL[e.action] ?? e.action}</Tag>
                      <Typography.Text type="secondary">{e.actor}</Typography.Text>
                      <Typography.Text type="secondary">{formatTime(e.createdAt)}</Typography.Text>
                    </Space>
                    {e.note && <div className="timeline-note">{e.note}</div>}
                  </div>
                ),
              }))}
            />
          )}

          {canOperate && (
            <Space wrap className="drawer-actions">
              {availableActions(issue.status).map((key) => {
                const m = actionMeta(key, issue.status);
                return (
                  <Button
                    key={key}
                    danger={m.danger}
                    type={key === 'approve' ? 'primary' : 'default'}
                    onClick={() => {
                      setPending(key);
                      setAssignee(issue.assignee || '');
                      setNote('');
                      setError(null);
                    }}
                  >
                    {m.label}
                  </Button>
                );
              })}
            </Space>
          )}
        </>
      )}

      <Modal
        title={meta?.label}
        open={Boolean(pending)}
        onCancel={() => setPending(null)}
        onOk={() => void submit()}
        confirmLoading={submitting}
        okText="确定"
        cancelText="取消"
        okButtonProps={{ danger: meta?.danger }}
      >
        <Space direction="vertical" style={{ width: '100%' }}>
          {meta?.needAssignee && (
            <Select
              style={{ width: '100%' }}
              placeholder="选择处理人"
              value={assignee || undefined}
              onChange={setAssignee}
              options={repairers.map((u) => ({
                value: u.username,
                label: `${u.username}（${u.specialties
                  .map((s) => CATEGORY_LABEL[s] ?? s)
                  .join('/')}，手上 ${u.activeWork} 单）`,
              }))}
            />
          )}

          {meta?.needNote && (
            <Input.TextArea
              rows={3}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="写清原因，会留在时间线上"
            />
          )}
        </Space>
      </Modal>
    </Drawer>
  );
}
