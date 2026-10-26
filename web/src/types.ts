export type Priority = 'low' | 'medium' | 'high';
export type Status = 'open' | 'in_progress' | 'pending_review' | 'resolved';

/** 三种身份：巡检员上报、修复者处理、管理员看全局并派单。 */
export type Role = 'inspector' | 'admin' | 'repairer';

/** 修复者能接的三类活，由 AI 从标题与描述里判断。 */
export type Category = 'wall' | 'device' | 'safety';

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

  /** AI 分诊结果，category 为空表示没判出来 */
  category: string;
  aiConfidence: number;
  aiReason: string;

  /** 派单与处理 */
  dispatchMode: string;
  assignee: string;
  assignedAt: string;
  assignedBy: string;
  submittedAt: string;

  /** 修复记录：原文必存，结构化结果转换失败时为空 */
  repairNote: string;
  repairSummary: string;
}

/** 一条流转留痕，抽屉里的时间线就按它渲染。 */
export interface IssueEvent {
  id: number;
  issueId: string;
  actor: string;
  action: string;
  fromStatus: string;
  toStatus: string;
  note: string;
  createdAt: string;
}

/** 详情接口返回：工单本体 + 全部留痕。 */
export interface IssueDetail {
  issue: Issue;
  events: IssueEvent[];
}

/** 人员列表里的一行。 */
export interface UserView {
  username: string;
  role: Role;
  specialties: string[];
  active: boolean;
  activeWork: number;
}

export const STATUS_ORDER: Status[] = ['open', 'in_progress', 'resolved'];

export const STATUS_LABEL: Record<Status, string> = {
  open: '待处理',
  in_progress: '处理中',
  pending_review: '待确认',
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
  repairer: '修复者',
};

export const CATEGORY_LABEL: Record<string, string> = {
  wall: '墙面',
  device: '设备',
  safety: '安全',
};

/** 流转动作的中文名，事件时间线上用。 */
export const ACTION_LABEL: Record<string, string> = {
  created: '提交',
  ai_dispatched: 'AI 派单',
  manual_dispatched: '人工派单',
  returned: '退回',
  reassigned: '改派',
  submitted: '提交处理',
  approved: '验收通过',
  rejected: '驳回',
  withdrawn: '撤回',
  reopened: '重开',
  closed: '关闭',
  ai_failed: 'AI 未能判定',
};
