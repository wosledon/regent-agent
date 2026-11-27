import { describe, expect, it } from 'vitest';
import i18n from './i18n';
import { localizedErrorMessage } from './errorMessages';
import { ApiError } from './api-client/api';

describe('localized error messages', () => {
  it.each([
    [{ status: 401, code: 'UNAUTHENTICATED', message: 'raw session detail' }, 'Your session has expired. Please sign in again.'],
    [{ status: 403, code: 'FORBIDDEN', message: 'raw authorization detail' }, 'Permission denied. You do not have access to this resource.'],
    [{ status: 409, code: 'CONFLICT', message: 'raw conflict detail' }, 'The data has changed. Refresh and try again.'],
    [{ status: 429, code: 'RATE_LIMITED', message: 'raw rate detail' }, 'Too many requests. Please wait and try again.'],
    [{ status: 503, code: 'SERVICE_UNAVAILABLE', message: 'raw stack detail' }, 'The service is temporarily unavailable. Please try again later.']
  ])('maps stable API failures without exposing backend details', async (error, expected) => {
    await i18n.changeLanguage('en-US');
    expect(localizedErrorMessage(error, i18n.t.bind(i18n))).toBe(expected);
  });

  it('preserves non-sensitive local messages when no stable mapping exists', async () => {
    await i18n.changeLanguage('en-US');
    expect(localizedErrorMessage(new Error('Impact refresh failed'), i18n.t.bind(i18n))).toBe('Impact refresh failed');
    expect(localizedErrorMessage('Local validation failed', i18n.t.bind(i18n))).toBe('Local validation failed');
  });

  it.each([
    ['en-US', 'The operation could not be completed. Please try again.'],
    ['zh-CN', '操作未能完成，请重试。']
  ] as const)('hides raw backend details for unrecognized API failures in %s', async (locale, expected) => {
    await i18n.changeLanguage(locale);
    const error = new ApiError(418, 'UNRECOGNIZED_API_CODE', 'SENSITIVE_BACKEND_MARKER stack trace');
    expect(localizedErrorMessage(error, i18n.t.bind(i18n))).toBe(expected);
    expect(localizedErrorMessage({ status: 418, code: 'UNRECOGNIZED_API_CODE', message: 'SENSITIVE_BACKEND_MARKER' }, i18n.t.bind(i18n))).toBe(expected);
  });

  it('localizes permission text and network failures', async () => {
    await i18n.changeLanguage('zh-CN');
    expect(localizedErrorMessage(new Error('Permission denied.'), i18n.t.bind(i18n))).toBe('权限不足，无法访问此资源。');
    expect(localizedErrorMessage({ status: 403, code: 'UNAUTHORIZED', message: 'raw detail' }, i18n.t.bind(i18n))).toBe('权限不足，无法访问此资源。');
    expect(localizedErrorMessage({ status: 400, code: 'validation-error', message: 'raw detail' }, i18n.t.bind(i18n))).toBe('请求信息无效，请检查后重试。');
    expect(localizedErrorMessage(new TypeError('fetch failed'), i18n.t.bind(i18n))).toContain('无法连接服务');
  });
});
