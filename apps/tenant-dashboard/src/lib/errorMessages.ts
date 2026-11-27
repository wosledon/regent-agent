import type { TFunction } from 'i18next';

interface ErrorMetadata {
  status?: unknown;
  code?: unknown;
  message?: unknown;
}

function metadata(error: unknown): ErrorMetadata {
  return typeof error === 'object' && error !== null ? error as ErrorMetadata : {};
}

export function localizedErrorMessage(error: unknown, t: TFunction): string {
  if (error === null || error === undefined) return t('common.errors.unexpected');
  const { status: rawStatus, code: rawCode, message: rawMessage } = metadata(error);
  const status = typeof rawStatus === 'number' ? rawStatus : 0;
  const code = typeof rawCode === 'string' ? rawCode.toUpperCase().replace(/[^A-Z0-9]+/g, '_') : '';
  const message = typeof error === 'string' ? error.trim() : typeof rawMessage === 'string' ? rawMessage.trim() : '';
  if (error instanceof TypeError || code === 'NETWORK_ERROR' || code === 'FETCH_ERROR') return t('common.errors.network');
  if (status === 401) return t('common.errors.sessionExpired');
  if (status === 403 || ['FORBIDDEN', 'PERMISSION_DENIED'].includes(code) || /permission denied|\b403\b|forbidden/i.test(message)) {
    return t('common.errors.permissionDenied');
  }
  if (['UNAUTHENTICATED', 'UNAUTHORIZED', 'SESSION_EXPIRED'].includes(code)) return t('common.errors.sessionExpired');
  if (status === 404 || code === 'NOT_FOUND') return t('common.errors.notFound');
  if (status === 409 || ['CONFLICT', 'VERSION_CONFLICT', 'IDEMPOTENCY_CONFLICT'].includes(code)) return t('common.errors.conflict');
  if (status === 429 || ['RATE_LIMITED', 'TOO_MANY_REQUESTS'].includes(code)) return t('common.errors.rateLimited');
  if (status === 400 || status === 422 || ['INVALID_REQUEST', 'VALIDATION_ERROR'].includes(code)) return t('common.errors.invalidRequest');
  if (status >= 500 || ['INTERNAL_ERROR', 'SERVICE_UNAVAILABLE'].includes(code)) return t('common.errors.unavailable');
  if (typeof error === 'string') return error || t('common.errors.unexpected');
  if (error instanceof Error && rawStatus === undefined && rawCode === undefined) return message || t('common.errors.unexpected');
  return t('common.errors.unexpected');
}
