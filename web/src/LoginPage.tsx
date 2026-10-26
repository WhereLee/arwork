import { useState } from 'react';
import { Alert, Button, Card, Form, Input, Typography } from 'antd';
import { login } from './api';
import { saveSession } from './auth';
import type { Session } from './types';

interface Props {
  onLogin: (session: Session) => void;
}

interface FormValues {
  username: string;
  password: string;
}

export default function LoginPage({ onLogin }: Props) {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleFinish = async (values: FormValues) => {
    setLoading(true);
    setError(null);

    try {
      const session = await login(values.username.trim(), values.password);
      saveSession(session);
      onLogin(session);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-page">
      <Card className="login-card">
        <Typography.Title level={3} className="page-title">
          园区巡检 · 问题处置
        </Typography.Title>
        <Typography.Paragraph type="secondary">
          登录后查看手机端上报的问题。巡检员只能看到自己报的那几条。
        </Typography.Paragraph>

        {error && <Alert type="error" showIcon message={error} className="error-alert" />}

        <Form<FormValues> layout="vertical" onFinish={handleFinish} requiredMark={false}>
          <Form.Item
            label="账号"
            name="username"
            rules={[{ required: true, message: '请输入账号' }]}
          >
            <Input size="large" autoComplete="username" placeholder="请输入账号" />
          </Form.Item>

          <Form.Item
            label="密码"
            name="password"
            rules={[{ required: true, message: '请输入密码' }]}
          >
            <Input.Password size="large" autoComplete="current-password" placeholder="请输入密码" />
          </Form.Item>

          <Button type="primary" htmlType="submit" size="large" block loading={loading}>
            登录
          </Button>
        </Form>

        <Typography.Paragraph type="secondary" className="login-hint">
          演示账号：管理员 admin / admin123，巡检员 inspector1 ~ inspector3 / inspector123
        </Typography.Paragraph>
      </Card>
    </div>
  );
}
