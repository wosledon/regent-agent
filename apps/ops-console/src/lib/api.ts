import { createRequestScopedApi, createRequestSignal, createUuidV4, getActiveRequestSignal, trackBrowserApiOutcome } from '@regent/ui-shared';

export const OPS_UNAUTHORIZED_EVENT = 'regent:ops:unauthorized';
export const OPS_PERMISSION_DENIED_EVENT = 'regent:ops:permission-denied';

export interface OperatorSession {
  email: string;
  display_name: string;
  role: string;
  auth_method: string;
  session_kind: string;
  capabilities?: string[];
}

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly code: string) {
    super(`Request failed: ${status}`);
  }
}

export function isPermissionDenied(error: unknown): boolean {
  return typeof error === 'object' && error !== null && 'status' in error
    && (error as { status?: unknown }).status === 403;
}

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
  approvalRef?: string;
  approvedBy?: string;
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
  componentRtoMinutes?: Record<string, number | null>;
}

export interface BackupJob {
  id: string;
  profileId: string;
  backupSetId?: string | null;
  profileVersion: number;
  component: string;
  status: string;
  startedAt: string;
  completedAt?: string;
  sha256: string;
  error?: string;
}

export type RestoreScope = 'Full' | 'Tenant';

export interface RestoreJob {
  id: string;
  profileId: string;
  backupIds: string[];
  scope: RestoreScope;
  approvalRef: string;
  requester: string;
  approver?: string;
  status: string;
  requestedAt: string;
  verifiedAt?: string;
  rpoActualMinutes?: number;
  rtoActualMinutes?: number;
  error?: string;
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

const IDEMPOTENCY_KEY_TTL_MS = 5 * 60_000;
const MAX_IDEMPOTENCY_KEYS = 500;
const idempotencyKeys = new Map<string, { key: string; expiresAt: number }>();

function stableIdempotencyKey(path: string, init?: RequestInit): string {
  const body = typeof init?.body === 'string' ? init.body : '';
  const cacheKey = `${(init?.method ?? 'GET').toUpperCase()}:${path}:${body}`;
  const now = Date.now();
  const existing = idempotencyKeys.get(cacheKey);
  if (existing && existing.expiresAt > now) return existing.key;
  if (existing) idempotencyKeys.delete(cacheKey);

  for (const [cachedKey, value] of idempotencyKeys) {
    if (value.expiresAt <= now) idempotencyKeys.delete(cachedKey);
  }
  while (idempotencyKeys.size >= MAX_IDEMPOTENCY_KEYS) {
    const oldestKey = idempotencyKeys.keys().next().value;
    if (oldestKey === undefined) break;
    idempotencyKeys.delete(oldestKey);
  }

  const key = createUuidV4();
  idempotencyKeys.set(cacheKey, { key, expiresAt: now + IDEMPOTENCY_KEY_TTL_MS });
  return key;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const requestSignal = createRequestSignal([init?.signal, getActiveRequestSignal()]);
  try {
    const method = (init?.method ?? 'GET').toUpperCase();
    const headers = new Headers(init?.headers);
    if (!headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
    if (!headers.has('X-Request-Id')) {
      try { headers.set('X-Request-Id', createUuidV4()); } catch { /* The server can correlate requests without this optional header. */ }
    }
    if (path.startsWith('/ops') && !['GET', 'HEAD', 'OPTIONS'].includes(method)) {
      if (!headers.has('Idempotency-Key')) headers.set('Idempotency-Key', stableIdempotencyKey(path, init));
      if (['PUT', 'PATCH', 'DELETE'].includes(method) && !headers.has('If-Match')) {
        let version: unknown;
        try { version = typeof init?.body === 'string' ? JSON.parse(init.body).version : undefined; } catch { /* server validates JSON */ }
        headers.set('If-Match', typeof version === 'number' ? `"${version}"` : '*');
      }
    }
    const response = await fetch(path, { credentials: 'include', ...init, signal: requestSignal.signal, headers });
    if (typeof window !== 'undefined' && response.status === 401) {
      window.dispatchEvent(new Event(OPS_UNAUTHORIZED_EVENT));
    }
    if (typeof window !== 'undefined' && response.status === 403) {
      window.dispatchEvent(new Event(OPS_PERMISSION_DENIED_EVENT));
    }
    trackBrowserApiOutcome(response.status, response.headers.get('X-Request-Id') ?? undefined);
    if (!response.ok) {
      const body = await response.json().catch(() => null) as { error?: { code?: unknown } } | null;
      const code = typeof body?.error?.code === 'string' ? body.error.code : 'UNKNOWN';
      throw new ApiError(response.status, code);
    }
    return response.status === 204 ? undefined as T : await response.json() as T;
  } catch (error) {
    if (requestSignal.timedOut()) throw new ApiError(0, 'TIMEOUT');
    throw error;
  } finally {
    requestSignal.dispose();
  }
}

const apiMethods = {
  me: () => request<OperatorSession>('/ops/auth/me'),
  login: () => request<{ display_name: string }>('/auth/dev-login', { method: 'POST' }),
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
  tenants: () => request<TenantAccount[]>('/ops/tenants'),
  grants: () => request<SupportGrant[]>('/ops/support-grants'),
  createGrant: (tenantId: string, approvalRef: string) => request<SupportGrant>('/ops/support-grants', { method: 'POST', body: JSON.stringify({ tenantId, scope: 'read-only', minutes: 30, approvalRef }) }),
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
  acknowledgeAlert: (id: string) => request<void>(`/ops/observability/alerts/${id}/acknowledge`, { method: 'POST' }),
  resolveAlert: (id: string, resolution: string) => request<void>(`/ops/observability/alerts/${id}/resolve`, { method: 'POST', headers: { 'X-Resolution': resolution } }),
  silenceAlert: (fingerprint: string, reason: string, minutes: number) => request<{ id: string }>('/ops/observability/alerts/silence', {
    method: 'POST', body: JSON.stringify({ fingerprint, reason, minutes })
  }),
  backups: () => request<BackupProfile[]>('/ops/backups'),
  setBackup: (enabled: boolean) => request<BackupProfile[]>('/ops/backups', { method: 'PUT', body: JSON.stringify({ enabled }) }),
  saveBackupProfile: (profile: BackupProfile) => request<BackupProfile>('/ops/backup-profile', { method: 'PUT', body: JSON.stringify(profile) }),
  backupCoverage: (profileId = 'default') => request<BackupCoverage>(`/ops/backup-coverage/${encodeURIComponent(profileId)}`),
  runBackup: (profileId = 'default') => request<BackupJob[]>('/ops/backups/run', { method: 'POST', body: JSON.stringify({ profileId }) }),
  backupJobs: () => request<BackupJob[]>('/ops/backup-jobs'),
  requestRestore: (backupIds: string[], scope: RestoreScope = 'Full') => request<RestoreJob>('/ops/restores', {
    method: 'POST', body: JSON.stringify({ backupIds, scope })
  }),
  approveRestore: (id: string, resolution = 'Isolated local manifest verification passed') => request<RestoreJob>(`/ops/restores/${id}/approve`, {
    method: 'POST', body: JSON.stringify({ resolution })
  }),
  restoreJobs: () => request<RestoreJob[]>('/ops/restores'),
  audit: () => request<OpsAudit[]>('/ops/audit')
};

export const api = createRequestScopedApi(apiMethods);
