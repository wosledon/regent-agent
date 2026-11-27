import { beforeEach, describe, expect, it } from 'vitest';
import i18n from './i18n';
import { localizedOpsErrorMessage } from './errorMessages';

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('localized Ops errors', () => {
  it.each([
    [401, 'common.errors.sessionExpired'],
    [403, 'common.errors.permissionDenied'],
    [404, 'common.errors.notFound'],
    [408, 'common.errors.timeout'],
    [409, 'common.errors.conflict'],
    [422, 'common.errors.invalidRequest'],
    [429, 'common.errors.rateLimited'],
    [500, 'common.errors.unavailable'],
    [503, 'common.errors.unavailable']
  ])('maps HTTP %i to safe resource copy', (status, key) => {
    expect(localizedOpsErrorMessage({ status, message: 'private backend detail' }, i18n.t)).toBe(i18n.t(key));
  });

  it('maps network and timeout failures without exposing exception text', () => {
    expect(localizedOpsErrorMessage(new TypeError('Failed to fetch'), i18n.t)).toBe(i18n.t('common.errors.network'));
    expect(localizedOpsErrorMessage({ name: 'TimeoutError', message: 'private timeout detail' }, i18n.t))
      .toBe(i18n.t('common.errors.timeout'));
    expect(localizedOpsErrorMessage({ status: 408, message: 'private timeout detail' }, i18n.t))
      .toBe(i18n.t('common.errors.timeout'));
    expect(localizedOpsErrorMessage({ message: 'Failed to fetch' }, i18n.t)).toBe(i18n.t('common.errors.network'));
    expect(localizedOpsErrorMessage({ message: 'Request timeout' }, i18n.t)).toBe(i18n.t('common.errors.timeout'));
  });

  it.each([
    [{ status: 403, message: 'private timeout detail' }, 'common.errors.permissionDenied'],
    [{ status: 401, message: 'private network detail' }, 'common.errors.sessionExpired'],
    [{ status: 409, code: 'PermissionDenied', message: 'private timeout detail' }, 'common.errors.conflict'],
    [{ status: 500, code: 'NOT_FOUND', message: 'private permission detail' }, 'common.errors.unavailable']
  ])('prioritizes stable HTTP status over conflicting metadata %#', (error, key) => {
    expect(localizedOpsErrorMessage(error, i18n.t)).toBe(i18n.t(key));
  });

  it.each([
    ['permissionDenied', 'common.errors.permissionDenied'],
    ['Permission-Denied', 'common.errors.permissionDenied'],
    ['permission_denied', 'common.errors.permissionDenied'],
    ['TooManyRequests', 'common.errors.rateLimited'],
    ['notFound', 'common.errors.notFound'],
    ['RequestTimeout', 'common.errors.timeout'],
    ['ServiceUnavailable', 'common.errors.unavailable']
  ])('normalizes recognized code %s before message heuristics', (code, key) => {
    expect(localizedOpsErrorMessage({ code, message: 'private timeout detail' }, i18n.t)).toBe(i18n.t(key));
  });

  it('uses generic copy for unrecognized API errors', () => {
    const message = localizedOpsErrorMessage({ status: 418, code: 'TEAPOT', message: 'secret backend detail' }, i18n.t);
    expect(message).toBe(i18n.t('common.errors.unexpected'));
    expect(message).not.toContain('secret backend detail');
  });

  it('localizes permission errors after a language switch', async () => {
    await i18n.changeLanguage('zh-CN');
    expect(localizedOpsErrorMessage({ status: 403 }, i18n.t)).toBe('权限不足，无法执行此操作。');
  });
});
