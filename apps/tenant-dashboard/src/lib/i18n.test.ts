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

describe('Tenant Dashboard i18n', () => {
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

  it('persists languageChanged events and updates the document language', async () => {
    const i18n = await initializeI18n({ saved: null, browser: 'zh-CN', expected: 'zh-CN' });
    await i18n.changeLanguage('en-US');
    expect(localStorage.getItem('regent.ui.locale')).toBe('en-US');
    expect(document.documentElement.lang).toBe('en-US');
  });

  it('provides recursive zh-CN/en-US parity for navigation, auth, shell and business resources', async () => {
    const i18n = await initializeI18n({ saved: null, browser: 'en-US', expected: 'en-US' });
    const authKeys = (locale: string) => resourceKeys(i18n.getResourceBundle(locale, 'translation')).sort();
    expect(authKeys('zh-CN')).toEqual(authKeys('en-US'));
    expect(authKeys('en-US')).toContain('auth.errors.network');
    expect(authKeys('en-US')).toContain('shell.signedInAs');
    expect(authKeys('en-US')).toContain('projectsPage.createProject');
    expect(authKeys('en-US')).toContain('settingsPage.savePreferences');
    expect(authKeys('en-US')).toContain('settingsPage.chinese');
    expect(authKeys('en-US')).toContain('common.noRecords');
    expect(authKeys('en-US')).toContain('collaborationPage.projectSubtitle');
    expect(i18n.t('auth.title')).toBe('Sign in to Regent Agent');
    expect(i18n.t('shell.signedInAs', { name: 'Workspace Owner' })).toBe('Signed in as Workspace Owner');
    await i18n.changeLanguage('zh-CN');
    expect(i18n.t('auth.title')).toBe('登录 Regent Agent');
    expect(i18n.t('shell.signedInAs', { name: 'Workspace Owner' })).toBe('已登录：Workspace Owner');
    expect(i18n.t('auth.errors.network')).toContain('认证服务');
  });
});
