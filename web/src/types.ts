export type Priority = 'low' | 'medium' | 'high';
export type Status = 'open' | 'in_progress' | 'resolved';

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
