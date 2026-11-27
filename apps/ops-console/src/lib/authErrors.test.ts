import { describe, expect, it } from 'vitest';
import { ApiError } from './api';
import { opsAuthErrorKey } from './authErrors';

describe('opsAuthErrorKey', () => {
  it('maps stable API metadata to safe resource keys', () => {
    expect(opsAuthErrorKey(new ApiError(403, 'FORBIDDEN'))).toBe('auth.errors.forbidden');
    expect(opsAuthErrorKey(new ApiError(429, 'RATE_LIMITED'))).toBe('auth.errors.rateLimited');
    expect(opsAuthErrorKey(new ApiError(503, 'INTERNAL_ERROR'))).toBe('auth.errors.unavailable');
  });

  it('maps network and unknown failures without using exception text', () => {
    expect(opsAuthErrorKey(new TypeError('Failed to fetch'))).toBe('auth.errors.network');
    expect(opsAuthErrorKey(new Error('arbitrary backend exception text'))).toBe('auth.errors.unexpected');
  });
});
