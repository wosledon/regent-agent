import type { components } from './schema';
import type {
  BenchmarkSuite,
  ChatMessage,
  ComplianceProfile,
  Conversation,
  EvaluationRun,
  KpiSnapshot,
  MemoryClaim,
  NotificationItem,
  WorkflowDefinition,
  WorkflowRun
} from '../domain-types';

type Schema = components['schemas'];

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly code: string, message: string) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: 'include',
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers
    }
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: { code?: string; message?: string } } | null;
    throw new ApiError(response.status, body?.error?.code ?? 'UNKNOWN', body?.error?.message ?? response.statusText);
  }
  return response.status === 204 ? undefined as T : await response.json() as T;
}

export const api = {
  devLogin: (email: string) => request<{ email: string; display_name: string }>('/auth/dev-login', {
    method: 'POST', body: JSON.stringify({ email } satisfies Schema['DevLoginRequest'])
  }),
  dashboard: () => request<Schema['DashboardSummary']>('/v1/dashboard/summary'),
  projects: () => request<Schema['ProjectSummary'][]>('/v1/projects'),
  project: (id: string) => request<Schema['ProjectDetail']>(`/v1/projects/${id}`),
  employees: () => request<Schema['EmployeeSummary'][]>('/v1/employees'),
  employeeTimeline: (id: string) => request<Schema['EmployeeTimeline']>(`/v1/employees/${id}/timeline`),
  approval: (id: string) => request<Schema['ApprovalDto']>(`/v1/approvals/${id}`),
  decideApproval: (id: string, decision: Schema['ApprovalDecision'], idempotencyKey: string) =>
    request<Schema['ApprovalDto']>(`/v1/approvals/${id}/decision`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(decision)
    }),
  workflows: () => request<WorkflowDefinition[]>('/v1/workflows'),
  startWorkflow: (id: string) => request<WorkflowRun>(`/v1/workflows/${id}/runs`, { method: 'POST' }),
  advanceWorkflow: (id: string) => request<WorkflowRun>(`/v1/workflow-runs/${id}/advance`, { method: 'POST' }),
  memories: (query = '') => request<MemoryClaim[]>(`/v1/memories?query=${encodeURIComponent(query)}`),
  correctMemory: (id: string, content: string, reason: string) =>
    request<MemoryClaim>(`/v1/memories/${id}/correct`, { method: 'POST', body: JSON.stringify({ content, reason }) }),
  kpis: () => request<KpiSnapshot[]>('/v1/kpis'),
  conversations: () => request<Conversation[]>('/v1/conversations'),
  messages: (id: string) => request<ChatMessage[]>(`/v1/conversations/${id}/messages`),
  sendMessage: (id: string, body: string, priority = 'P2') =>
    request<ChatMessage>(`/v1/conversations/${id}/messages`, { method: 'POST', body: JSON.stringify({ body, priority }) }),
  evaluationSuites: () => request<BenchmarkSuite[]>('/v1/evaluation-suites'),
  evaluations: () => request<EvaluationRun[]>('/v1/evaluations/runs'),
  runEvaluation: (suiteId: string, candidateEmployeeId: string, seed = 42) =>
    request<EvaluationRun>('/v1/evaluations/runs', { method: 'POST', body: JSON.stringify({ suiteId, candidateEmployeeId, seed }) }),
  compliance: () => request<ComplianceProfile>('/v1/compliance/profile'),
  updateCompliance: (profile: ComplianceProfile) =>
    request<ComplianceProfile>('/v1/compliance/profile', { method: 'PUT', body: JSON.stringify(profile) }),
  notifications: () => request<NotificationItem[]>('/v1/notifications'),
  notificationAction: (id: string, action: 'read' | 'acknowledge' | 'archive') =>
    request<void>(`/v1/notifications/${id}/${action}`, { method: 'POST' })
};
