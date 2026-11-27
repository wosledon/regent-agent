import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

interface StartupCase {
  saved: string | null;
  browser: string;
  expected: 'zh-CN' | 'en-US';
}

async function initializeI18n({ saved, browser }: StartupCase) {
  localStorage.clear();
  if (saved) localStorage.setItem('regent.ui.locale', saved);
  vi.spyOn(navigator, 'language', 'get').mockReturnValue(browser);
  vi.resetModules();
  return (await import('./i18n')).default;
}

function resourceKeys(value: unknown, prefix = ''): string[] {
  if (typeof value === 'string') return [prefix];
  if (!value || typeof value !== 'object' || Array.isArray(value)) return [];
  return Object.entries(value).flatMap(([key, child]) => resourceKeys(child, prefix ? `${prefix}.${key}` : key));
}

beforeEach(() => {
  localStorage.clear();
  document.documentElement.lang = '';
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('Ops Console i18n', () => {
  it.each<StartupCase>([
    { saved: 'en-US', browser: 'zh-CN', expected: 'en-US' },
    { saved: 'zh-CN', browser: 'en-US', expected: 'zh-CN' },
    { saved: 'fr-FR', browser: 'en-GB', expected: 'en-US' },
    { saved: null, browser: 'en-GB', expected: 'en-US' },
    { saved: null, browser: 'fr-FR', expected: 'zh-CN' }
  ])('initializes from saved preference, then browser language, then zh-CN ($expected)', async startup => {
    const i18n = await initializeI18n(startup);
    expect(i18n.language).toBe(startup.expected);
    expect(document.documentElement.lang).toBe(startup.expected);
    expect(i18n.options.supportedLngs).toEqual(expect.arrayContaining(['zh-CN', 'en-US']));
    expect(i18n.options.fallbackLng).toEqual(['zh-CN']);
  });

  it('guards unavailable storage during startup and language changes', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('storage denied'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('storage denied'); });
    const i18n = await initializeI18n({ saved: null, browser: 'en-US', expected: 'en-US' });

    expect(i18n.language).toBe('en-US');
    await expect(i18n.changeLanguage('zh-CN')).resolves.toBeDefined();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('persists languageChanged events and updates document lang', async () => {
    const i18n = await initializeI18n({ saved: null, browser: 'zh-CN', expected: 'zh-CN' });
    await i18n.changeLanguage('en-US');
    expect(localStorage.getItem('regent.ui.locale')).toBe('en-US');
    expect(document.documentElement.lang).toBe('en-US');
  });

  it('provides recursive zh-CN/en-US parity for auth, shell and business resources', async () => {
    const i18n = await initializeI18n({ saved: null, browser: 'en-US', expected: 'en-US' });
    const resourceKeysFor = (locale: string) => resourceKeys(i18n.getResourceBundle(locale, 'translation')).sort();
    expect(resourceKeysFor('zh-CN')).toEqual(resourceKeysFor('en-US'));
    expect(resourceKeysFor('en-US')).toEqual(expect.arrayContaining([
      'auth.errors.network', 'shellLogin.links.observability', 'common.errors.permissionDenied',
      'overviewPage.title', 'tenantsPage.title', 'supportPage.title', 'backupsPage.title',
      'auditPage.title', 'statusPage.title', 'observabilityPage.title'
    ]));
    expect(i18n.t('auth.action')).toBe('Enter local demo Operator');
    expect(i18n.t('shellLogin.logout')).toBe('Logout');
    await i18n.changeLanguage('zh-CN');
    expect(i18n.t('auth.action')).toBe('进入本地演示 Operator');
    expect(i18n.t('shellLogin.logout')).toBe('退出登录');
    expect(i18n.t('auth.notice')).toContain('不提供生产 SSO 或 MFA');
  });

  it('renders localized non-empty fallback copy for missing keys', async () => {
    const i18n = await initializeI18n({ saved: null, browser: 'en-US', expected: 'en-US' });
    const missingKey = 'localization.audit.missing.key';
    expect(i18n.t(missingKey)).toBe('Unknown');
    expect(i18n.t(missingKey)).not.toBe(missingKey);
    expect(i18n.t(missingKey).trim()).not.toBe('');

    await i18n.changeLanguage('zh-CN');
    expect(i18n.t(missingKey)).toBe('未知');
  });
});
