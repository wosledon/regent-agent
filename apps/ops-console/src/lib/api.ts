import { trackBrowserApiOutcome } from '@regent/ui-shared';

export interface TenantAccount {
  id: string;
  name: string;
  status: string;
  employees: number;
  runnerCount: number;
}

export interface SupportGrant {
  id: string;
  tenantId: string;
  operatorId: string;
  scope: string;
  expiresAt: string;
  status: string;
}

export interface BackupProfile {
  id: string;
  deploymentId: string;
  enabled: boolean;
  components: string;
  retentionDays: number;
  postgresFull?: boolean;
  postgresPitr?: boolean;
  rustFs?: boolean;
  keyVault?: boolean;
  offsite?: boolean;
  restoreDrills?: boolean;
  schedule?: string;
  targetRef?: string;
  version?: number;
}

export interface BackupCoverage {
  profileId: string;
  enabled: boolean;
  status: string;
  enabledComponents: string[];
  lastSuccessfulAt?: string;
  effectiveRpoMinutes?: number;
  effectiveRtoMinutes?: number;
  rpoStatus: string;
  rtoStatus: string;
}

export interface BackupJob {
  id: string;
  component: string;
  status: string;
  startedAt: string;
  completedAt?: string;
  sha256: string;
  error?: string;
}

export interface RestoreJob {
  id: string;
  backupIds: string[];
  scope: string;
  approvalRef: string;
  requester: string;
  approver?: string;
  status: string;
  requestedAt: string;
  verifiedAt?: string;
  rpoActualMinutes?: number;
  rtoActualMinutes?: number;
}

export interface OpsAudit {
  id: string;
  operatorId: string;
  action: string;
  target: string;
  createdAt: string;
}

export interface PlatformComponentStatus {
  name: string;
  status: string;
  latencyMs: number;
  checkedAt: string;
  message: string;
}

export interface PlatformIncident {
  id: string;
  title: string;
  severity: string;
  status: string;
  message: string;
  startedAt: string;
  resolvedAt?: string;
}

export interface PlatformStatus {
  overallStatus: string;
  checkedAt: string;
  components: PlatformComponentStatus[];
  incidents: PlatformIncident[];
}

export interface ObservabilityProfile {
  mode: string;
  serviceName: string;
  elasticEnabled: boolean;
  metricIntervalSeconds: number;
}

export interface ElasticsearchIndex {
  name: string;
  documents?: number;
  storeSize?: string;
}

export interface ElasticsearchStatus {
  configured: boolean;
  reachable: boolean;
  clusterName?: string;
  status?: string;
  version?: string;
  nodes?: number;
  activePrimaryShards?: number;
  indices: ElasticsearchIndex[];
  checkedAt: string;
  error?: string;
}

export interface SloDefinition {
  name: string;
  metric: string;
  target: number;
  windowDays: number;
}

export interface SloEvaluation {
  name: string;
  actual: number;
  target: number;
  passed: boolean;
  errorBudgetRemaining: number;
  level: string;
  alertId?: string;
}

export interface AlertRecord {
  id: string;
  tenantId: string;
  serviceName: string;
  sloName: string;
  fingerprint: string;
  level: string;
  status: string;
  message: string;
  runbook: string;
  owner: string;
  createdAt: string;
  updatedAt: string;
  acknowledgedAt?: string;
  resolvedAt?: string;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { credentials: 'include', ...init, headers: { 'Content-Type': 'application/json', ...init?.headers } });
  trackBrowserApiOutcome(response.status, response.headers.get('X-Request-Id') ?? undefined);
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }
  return response.status === 204 ? undefined as T : await response.json() as T;
}

export const api = {
  login: () => request<{ display_name: string }>('/auth/dev-login', { method: 'POST' }),
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
  tenants: () => request<TenantAccount[]>('/ops/tenants'),
  grants: () => request<SupportGrant[]>('/ops/support-grants'),
  createGrant: (tenantId: string) => request<SupportGrant>('/ops/support-grants', { method: 'POST', body: JSON.stringify({ tenantId, scope: 'read-only', minutes: 30 }) }),
  approveGrant: (id: string) => request<SupportGrant>(`/ops/support-grants/${id}/approve`, { method: 'POST' }),
  revokeGrant: (id: string) => request<void>(`/ops/support-grants/${id}/revoke`, { method: 'POST' }),
  setTenantStatus: (id: string, status: string) => request<TenantAccount>(`/ops/tenants/${id}/status`, { method: 'PUT', body: JSON.stringify({ status }) }),
  status: () => request<PlatformStatus>('/public-status'),
  refreshStatus: () => request<PlatformStatus>('/ops/status/refresh', { method: 'POST' }),
  createIncident: (title: string, severity: string, message: string) => request<PlatformIncident>('/ops/status/incidents', {
    method: 'POST', body: JSON.stringify({ title, severity, message })
  }),
  resolveIncident: (id: string) => request<PlatformIncident>(`/ops/status/incidents/${id}/resolve`, { method: 'POST' }),
  observability: () => request<ObservabilityProfile>('/ops/observability/profile'),
  elasticsearch: () => request<ElasticsearchStatus>('/ops/observability/elasticsearch'),
  saveObservability: (profile: ObservabilityProfile) => request<ObservabilityProfile>('/ops/observability/profile', { method: 'PUT', body: JSON.stringify(profile) }),
  saveSlo: (slo: SloDefinition) => request<SloDefinition>('/ops/observability/slos', { method: 'POST', body: JSON.stringify(slo) }),
  evaluateSlos: () => request<SloEvaluation[]>('/ops/observability/evaluation'),
  alerts: () => request<AlertRecord[]>('/ops/observability/alerts'),
  acknowledgeAlert: (id: string) => request<void>(`/ops/observability/alerts/${id}/acknowledge`, { method: 'POST', body: JSON.stringify({ operator: 'Demo Operator' }) }),
  resolveAlert: (id: string, resolution: string) => request<void>(`/ops/observability/alerts/${id}/resolve`, { method: 'POST', headers: { 'X-Resolution': resolution } }),
  silenceAlert: (fingerprint: string, reason: string, minutes: number) => request<{ id: string }>('/ops/observability/alerts/silence', {
    method: 'POST', body: JSON.stringify({ fingerprint, reason, approvedBy: 'Demo Operator', minutes })
  }),
  backups: () => request<BackupProfile[]>('/ops/backups'),
  setBackup: (enabled: boolean) => request<BackupProfile[]>('/ops/backups', { method: 'PUT', body: JSON.stringify({ enabled, components: enabled ? 'postgres,rustfs,keyvault' : 'none' }) }),
  saveBackupProfile: (profile: BackupProfile) => request<BackupProfile>('/ops/backup-profile', { method: 'PUT', body: JSON.stringify(profile) }),
  backupCoverage: () => request<BackupCoverage>('/ops/backup-coverage/default'),
  runBackup: () => request<BackupJob[]>('/ops/backups/run', { method: 'POST', body: JSON.stringify({ profileId: 'default' }) }),
  backupJobs: () => request<BackupJob[]>('/ops/backup-jobs'),
  requestRestore: (backupIds: string[]) => request<RestoreJob>('/ops/restores', {
    method: 'POST', body: JSON.stringify({ backupIds, scope: 'Full', approvalRef: `drill-${crypto.randomUUID()}`, requester: 'Demo Operator' })
  }),
  approveRestore: (id: string) => request<RestoreJob>(`/ops/restores/${id}/approve`, {
    method: 'POST', body: JSON.stringify({ approver: 'Second SRE', resolution: 'Isolated local manifest verification passed' })
  }),
  restoreJobs: () => request<RestoreJob[]>('/ops/restores'),
  audit: () => request<OpsAudit[]>('/ops/audit')
};
