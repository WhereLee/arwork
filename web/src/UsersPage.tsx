import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { createUser, listUsers, UnauthorizedError, updateUser } from './api';
import { CATEGORY_LABEL, ROLE_LABEL, type Role, type UserView } from './types';

const ROLE_COLOR: Record<Role, string> = {
  admin: 'purple',
  inspector: 'default',
  repairer: 'blue',
};

const SKILL_OPTIONS = (['wall', 'device', 'safety'] as const).map((k) => ({
  value: k,
  label: CATEGORY_LABEL[k],
}));

interface Props {
  onLogout: () => void;
}

interface CreateForm {
  username: string;
  password: string;
  role: Role;
  specialties: string[];
}

export default function UsersPage({ onLogout }: Props) {
  const [users, setUsers] = useState<UserView[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [saving, setSaving] = useState<string | null>(null);
  const [form] = Form.useForm<CreateForm>();
  const role = Form.useWatch('role', form);

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
      setUsers(await listUsers());
    } catch (err) {
      guard(err);
    } finally {
      setLoading(false);
    }
  }, [guard]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleCreate = async (values: CreateForm) => {
    setSaving('create');
    setError(null);

    try {
      await createUser({
        username: values.username.trim(),
        password: values.password,
        role: values.role,
        specialties: values.specialties ?? [],
      });

      setCreating(false);
      form.resetFields();
      await load();
    } catch (err) {
      guard(err);
    } finally {
      setSaving(null);
    }
  };

  const toggleActive = async (user: UserView, active: boolean) => {
    setSaving(user.username);
    setError(null);

    try {
      const updated = await updateUser(user.username, { active });
      setUsers((prev) => prev.map((u) => (u.username === updated.username ? updated : u)));
    } catch (err) {
      guard(err);
    } finally {
      setSaving(null);
    }
  };

  const changeSkills = async (user: UserView, skills: string[]) => {
    setSaving(user.username);
    setError(null);

    try {
      const updated = await updateUser(user.username, { specialties: skills });
      setUsers((prev) => prev.map((u) => (u.username === updated.username ? updated : u)));
    } catch (err) {
      guard(err);
    } finally {
      setSaving(null);
    }
  };

  const columns = useMemo<ColumnsType<UserView>>(
    () => [
      {
        title: '账号',
        dataIndex: 'username',
        width: 140,
        render: (name: string) => <Typography.Text strong>{name}</Typography.Text>,
      },
      {
        title: '角色',
        dataIndex: 'role',
        width: 110,
        render: (r: Role) => <Tag color={ROLE_COLOR[r]}>{ROLE_LABEL[r]}</Tag>,
      },
      {
        title: '技能',
        dataIndex: 'specialties',
        render: (skills: string[], user) =>
          user.role === 'repairer' ? (
            <Select
              mode="multiple"
              size="small"
              style={{ minWidth: 200 }}
              value={skills}
              options={SKILL_OPTIONS}
              loading={saving === user.username}
              disabled={saving === user.username}
              onChange={(next: string[]) => void changeSkills(user, next)}
            />
          ) : (
            <Typography.Text type="secondary">—</Typography.Text>
          ),
      },
      {
        title: '手头工单',
        dataIndex: 'activeWork',
        width: 100,
        render: (n: number, user) =>
          user.role === 'repairer' ? (
            <Tag color={n >= 5 ? 'red' : n > 0 ? 'orange' : 'default'}>{n} 单</Tag>
          ) : (
            <Typography.Text type="secondary">—</Typography.Text>
          ),
      },
      {
        title: '在用',
        dataIndex: 'active',
        width: 80,
        render: (active: boolean, user) => (
          <Switch
            size="small"
            checked={active}
            loading={saving === user.username}
            onChange={(next) => void toggleActive(user, next)}
          />
        ),
      },
    ],
    // toggleActive / changeSkills 只依赖 setUsers 和 guard，不必进依赖
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [saving],
  );

  return (
    <>
      <div className="page-header">
        <div>
          <Typography.Title level={3} className="page-title">
            人员与技能
          </Typography.Title>
          <Typography.Text type="secondary">
            派单按技能匹配，停用前必须先把手上的工单退回
          </Typography.Text>
        </div>

        <Space>
          <Button onClick={() => void load()} loading={loading}>
            刷新
          </Button>
          <Button type="primary" onClick={() => setCreating(true)}>
            新建账号
          </Button>
        </Space>
      </div>

      {error && <Alert type="error" showIcon message={error} className="error-alert" />}

      <Table<UserView>
        rowKey="username"
        columns={columns}
        dataSource={users}
        loading={loading && users.length === 0}
        pagination={false}
        scroll={{ x: 700 }}
      />

      <Modal
        title="新建账号"
        open={creating}
        onCancel={() => setCreating(false)}
        onOk={() => form.submit()}
        confirmLoading={saving === 'create'}
        okText="创建"
        cancelText="取消"
        destroyOnClose
      >
        <Form<CreateForm>
          form={form}
          layout="vertical"
          onFinish={handleCreate}
          initialValues={{ role: 'repairer', specialties: [] }}
        >
          <Form.Item
            label="账号"
            name="username"
            rules={[{ required: true, message: '请输入账号' }]}
          >
            <Input placeholder="例如 repairer5" />
          </Form.Item>

          <Form.Item
            label="密码"
            name="password"
            rules={[
              { required: true, message: '请输入密码' },
              { min: 6, message: '至少 6 位' },
            ]}
          >
            <Input.Password placeholder="至少 6 位" />
          </Form.Item>

          <Form.Item label="角色" name="role" rules={[{ required: true }]}>
            <Select
              options={[
                { value: 'repairer', label: '修复者' },
                { value: 'inspector', label: '巡检员' },
                { value: 'admin', label: '管理员' },
              ]}
            />
          </Form.Item>

          {role === 'repairer' && (
            <Form.Item
              label="技能"
              name="specialties"
              rules={[{ required: true, message: '修复者至少要有一个技能' }]}
            >
              <Select mode="multiple" options={SKILL_OPTIONS} placeholder="可多选" />
            </Form.Item>
          )}
        </Form>
      </Modal>
    </>
  );
}
