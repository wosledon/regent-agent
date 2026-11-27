import type { TFunction } from 'i18next';

interface ErrorMetadata {
  status?: unknown;
  code?: unknown;
  message?: unknown;
  name?: unknown;
}

type OpsErrorResource =
  | 'common.errors.sessionExpired'
  | 'common.errors.permissionDenied'
  | 'common.errors.notFound'
  | 'common.errors.conflict'
  | 'common.errors.invalidRequest'
  | 'common.errors.rateLimited'
  | 'common.errors.unavailable'
  | 'common.errors.network'
  | 'common.errors.timeout'
  | 'common.errors.unexpected';

function metadata(error: unknown): ErrorMetadata {
  return typeof error === 'object' && error !== null ? error as ErrorMetadata : {};
}

function normalizeErrorCode(value: unknown): string {
  if (typeof value !== 'string') return '';
  return value.trim()
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1_$2')
    .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
    .replace(/[^A-Za-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .toUpperCase();
}

function httpStatus(value: unknown): number {
  if (typeof value === 'number') return Number.isFinite(value) ? value : 0;
  if (typeof value === 'string' && /^\d{3}$/.test(value.trim())) return Number(value);
  return 0;
}

function statusErrorResource(status: number): OpsErrorResource | null {
  if (status === 401) return 'common.errors.sessionExpired';
  if (status === 403) return 'common.errors.permissionDenied';
  if (status === 404) return 'common.errors.notFound';
  if (status === 408) return 'common.errors.timeout';
  if (status === 409) return 'common.errors.conflict';
  if (status === 400 || status === 422) return 'common.errors.invalidRequest';
  if (status === 429) return 'common.errors.rateLimited';
  if (status >= 500) return 'common.errors.unavailable';
  return null;
}

function codeErrorResource(code: string): OpsErrorResource | null {
  if (['UNAUTHENTICATED', 'UNAUTHORIZED', 'SESSION_EXPIRED'].includes(code)) return 'common.errors.sessionExpired';
  if (['FORBIDDEN', 'PERMISSION_DENIED'].includes(code)) return 'common.errors.permissionDenied';
  if (code === 'NOT_FOUND') return 'common.errors.notFound';
  if (['TIMEOUT', 'ETIMEDOUT', 'REQUEST_TIMEOUT'].includes(code)) return 'common.errors.timeout';
  if (['CONFLICT', 'VERSION_CONFLICT', 'IDEMPOTENCY_CONFLICT'].includes(code)) return 'common.errors.conflict';
  if (['INVALID_REQUEST', 'VALIDATION_ERROR'].includes(code)) return 'common.errors.invalidRequest';
  if (['RATE_LIMITED', 'TOO_MANY_REQUESTS'].includes(code)) return 'common.errors.rateLimited';
  if (['NETWORK_ERROR', 'FETCH_ERROR'].includes(code)) return 'common.errors.network';
  if (['INTERNAL_ERROR', 'SERVICE_UNAVAILABLE'].includes(code)) return 'common.errors.unavailable';
  return null;
}

export function localizedOpsErrorMessage(error: unknown, t: TFunction): string {
  if (error === null || error === undefined) return t('common.errors.unexpected');
  const { status: rawStatus, code: rawCode, message: rawMessage, name: rawName } = metadata(error);
  const statusResource = statusErrorResource(httpStatus(rawStatus));
  if (statusResource) return t(statusResource);

  const codeResource = codeErrorResource(normalizeErrorCode(rawCode));
  if (codeResource) return t(codeResource);

  const name = normalizeErrorCode(rawName);
  const message = typeof error === 'string' ? error : typeof rawMessage === 'string' ? rawMessage : '';
  if (error instanceof TypeError || name === 'TYPE_ERROR') return t('common.errors.network');
  if (name === 'TIMEOUT_ERROR') return t('common.errors.timeout');
  if (/timed?\s*out|timeout/i.test(message)) return t('common.errors.timeout');
  if (/failed to fetch|fetch failed|network(?:\s+error)?|networkerror|load failed/i.test(message)) return t('common.errors.network');
  return t('common.errors.unexpected');
}
