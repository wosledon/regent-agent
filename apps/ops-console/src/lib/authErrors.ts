export type OpsAuthErrorKey =
  | 'auth.errors.invalidRequest'
  | 'auth.errors.forbidden'
  | 'auth.errors.notFound'
  | 'auth.errors.rateLimited'
  | 'auth.errors.unavailable'
  | 'auth.errors.network'
  | 'auth.errors.unexpected';

interface ErrorMetadata {
  status?: unknown;
  code?: unknown;
}

export function opsAuthErrorKey(cause: unknown): OpsAuthErrorKey {
  const metadata: ErrorMetadata = typeof cause === 'object' && cause !== null ? cause as ErrorMetadata : {};
  const code = typeof metadata.code === 'string' ? metadata.code.toUpperCase() : '';
  const status = typeof metadata.status === 'number' ? metadata.status : 0;

  if (cause instanceof TypeError || code === 'NETWORK_ERROR') return 'auth.errors.network';
  if (code === 'INVALID_REQUEST' || code === 'VALIDATION_ERROR' || status === 400 || status === 422) {
    return 'auth.errors.invalidRequest';
  }
  if (code === 'NOT_FOUND' || status === 404) return 'auth.errors.notFound';
  if (code === 'RATE_LIMITED' || code === 'TOO_MANY_REQUESTS' || status === 429) return 'auth.errors.rateLimited';
  if (code === 'FORBIDDEN' || code === 'UNAUTHENTICATED' || code === 'UNAUTHORIZED' || status === 401 || status === 403) {
    return 'auth.errors.forbidden';
  }
  if (code === 'INTERNAL_ERROR' || code === 'SERVICE_UNAVAILABLE' || status >= 500) return 'auth.errors.unavailable';
  return 'auth.errors.unexpected';
}
