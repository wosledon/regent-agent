import { describe, expect, it } from 'vitest';
import { ApiError } from './api-client/api';
import { authErrorKey } from './authErrors';

describe('authErrorKey', () => {
  it('maps stable status and code metadata without exposing backend messages', () => {
    expect(authErrorKey(new ApiError(401, 'UNAUTHENTICATED', 'private backend detail'), 'password'))
      .toBe('auth.errors.invalidCredentials');
    expect(authErrorKey(new ApiError(403, 'FORBIDDEN', 'private backend detail'), 'password'))
      .toBe('auth.errors.forbidden');
    expect(authErrorKey(new ApiError(429, 'RATE_LIMITED', 'private backend detail'), 'dev'))
      .toBe('auth.errors.rateLimited');
    expect(authErrorKey(new ApiError(503, 'INTERNAL_ERROR', 'private backend detail'), 'emergency'))
      .toBe('auth.errors.unavailable');
  });

  it('uses the authentication flow to distinguish registration conflicts', () => {
    expect(authErrorKey(new ApiError(409, 'INVALID_STATE', 'Email is already registered.'), 'register'))
      .toBe('auth.errors.emailInUse');
    expect(authErrorKey(new ApiError(409, 'INVALID_STATE', 'private backend detail'), 'emergency'))
      .toBe('auth.errors.unexpected');
  });

  it('maps network failures and unknown errors to safe fallbacks', () => {
    expect(authErrorKey(new TypeError('Failed to fetch'), 'password')).toBe('auth.errors.network');
    expect(authErrorKey(new Error('arbitrary backend exception text'), 'password')).toBe('auth.errors.unexpected');
  });
});
