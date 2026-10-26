export type Priority = 'low' | 'medium' | 'high';
export type Status = 'open' | 'in_progress' | 'resolved';

/** 两种身份：巡检员只上报和看自己的，管理员看全部并处置。 */
export type Role = 'inspector' | 'admin';

/** 登录成功后拿到的东西，存 localStorage。 */
export interface Session {
  token: string;
  username: string;
  role: Role;
}

export interface Position {
  x: number;
  y: number;
  z: number;
}

/**
 * 与 Go 后端返回的 JSON 字段一一对应。
 * 后端枚举严格按小写字面值校验，所以这里用联合类型把约束带到编译期。
 */
export interface Issue {
  id: string;
  title: string;
  description: string;
  priority: Priority;
  status: Status;
  position: Position;
  reporter: string;
  createdAt: string;
  updatedAt: string;
}

export const STATUS_ORDER: Status[] = ['open', 'in_progress', 'resolved'];

export const STATUS_LABEL: Record<Status, string> = {
  open: '待处理',
  in_progress: '处理中',
  resolved: '已解决',
};

export const PRIORITY_LABEL: Record<Priority, string> = {
  high: '高',
  medium: '中',
  low: '低',
};

export const ROLE_LABEL: Record<Role, string> = {
  inspector: '巡检员',
  admin: '管理员',
};
