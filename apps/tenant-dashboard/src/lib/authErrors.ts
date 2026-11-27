import type { AuthMode } from '../pages/authModes';

export type AuthErrorKey =
  | 'auth.errors.invalidCredentials'
  | 'auth.errors.emailInUse'
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

function errorMetadata(cause: unknown): ErrorMetadata {
  return typeof cause === 'object' && cause !== null ? cause as ErrorMetadata : {};
}

export function authErrorKey(cause: unknown, mode: AuthMode): AuthErrorKey {
  const { status, code: rawCode } = errorMetadata(cause);
  const code = typeof rawCode === 'string' ? rawCode.toUpperCase() : '';
  const httpStatus = typeof status === 'number' ? status : 0;

  if (cause instanceof TypeError || code === 'NETWORK_ERROR' || httpStatus === 0 && code === 'FETCH_ERROR') {
    return 'auth.errors.network';
  }
  if (mode === 'register' && ['EMAIL_ALREADY_REGISTERED', 'EMAIL_EXISTS', 'ACCOUNT_EXISTS'].includes(code)) {
    return 'auth.errors.emailInUse';
  }
  if (mode === 'register' && httpStatus === 409) return 'auth.errors.emailInUse';
  if (code === 'RATE_LIMITED' || code === 'TOO_MANY_REQUESTS' || httpStatus === 429) return 'auth.errors.rateLimited';
  if (code === 'INVALID_REQUEST' || code === 'VALIDATION_ERROR' || httpStatus === 400 || httpStatus === 422) {
    return 'auth.errors.invalidRequest';
  }
  if (code === 'NOT_FOUND' || httpStatus === 404) return 'auth.errors.notFound';
  if (['FORBIDDEN', 'LOCAL_LOGIN_DISABLED', 'SSO_REQUIRED'].includes(code)) return 'auth.errors.forbidden';
  if (code === 'INVALID_CREDENTIALS' || code === 'UNAUTHENTICATED' || code === 'UNAUTHORIZED' || httpStatus === 401) {
    return 'auth.errors.invalidCredentials';
  }
  if (httpStatus === 403) return mode === 'register' ? 'auth.errors.forbidden' : 'auth.errors.invalidCredentials';
  if (code === 'INTERNAL_ERROR' || code === 'SERVICE_UNAVAILABLE' || httpStatus >= 500) return 'auth.errors.unavailable';
  return 'auth.errors.unexpected';
}
