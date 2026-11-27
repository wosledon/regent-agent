import { expect, test, type Page } from '@playwright/test';
import { externalStack, opsUrl, skipMockedAuthInExternalStack, tenantPreviewUrl, tenantUrl } from './support/externalStack';

type Locale = 'zh-CN' | 'en-US';
type AppName = 'tenant' | 'ops';

const tenantCopy = {
  'zh-CN': {
    title: '登录 Regent Agent', otherTitle: 'Sign in to Regent Agent', switchLocale: 'English',
    mode: '认证方式', dev: '本地开发', passwordMode: '密码', register: '创建账户', email: '邮箱', password: '密码',
    passwordPlaceholder: '至少 10 个字符', displayName: '显示名称', displayNamePlaceholder: '显示名称',
    companyName: '公司名称', companyNamePlaceholder: '公司名称',
    checking: '正在检查您的会话…', error: '认证服务暂时不可用，请稍后重试。', signedIn: '已登录：Browser Owner',
    openNavigation: '打开导航', logout: '退出登录', shellSignedIn: '已登录：Browser Owner'
  },
  'en-US': {
    title: 'Sign in to Regent Agent', otherTitle: '登录 Regent Agent', switchLocale: '简体中文',
    mode: 'Authentication mode', dev: 'Local development', passwordMode: 'Password', register: 'Create account', email: 'Email', password: 'Password',
    passwordPlaceholder: 'At least 10 characters', displayName: 'Display name', displayNamePlaceholder: 'Display name',
    companyName: 'Company name', companyNamePlaceholder: 'Company name',
    checking: 'Checking your session…', error: 'Authentication is temporarily unavailable. Please try again later.', signedIn: 'Signed in as Browser Owner',
    openNavigation: 'Open navigation', logout: 'Logout', shellSignedIn: 'Signed in as Browser Owner'
  }
} as const;

type TenantCopy = typeof tenantCopy[Locale];

const opsCopy = {
  'zh-CN': {
    title: '运营登录', otherTitle: 'Operations sign in', switchLocale: 'English', action: '进入本地演示 Operator',
    notice: '此操作使用本地开发登录端点，不提供生产 SSO 或 MFA。', checking: '正在检查您的会话…',
    submitting: '正在进入运营控制台…', error: '运营登录服务暂时不可用，请稍后重试。',
    openNavigation: '打开导航', logout: '退出登录'
  },
  'en-US': {
    title: 'Operations sign in', otherTitle: '运营登录', switchLocale: '简体中文', action: 'Enter local demo Operator',
    notice: 'This action uses the local development login endpoint. It does not provide production SSO or MFA.',
    checking: 'Checking your session…', submitting: 'Entering the operations console…',
    error: 'Operations sign-in is temporarily unavailable. Please try again later.',
    openNavigation: 'Open navigation', logout: 'Logout'
  }
} as const;

const urls: Record<AppName, string> = {
  tenant: tenantUrl,
  ops: opsUrl
};

async function seedLocale(page: Page, locale: Locale) {
  await page.addInitScript(value => {
    if (!localStorage.getItem('regent.ui.locale')) localStorage.setItem('regent.ui.locale', value);
  }, locale);
}

async function mockAnonymousSession(page: Page, app: AppName, delay = 0) {
  await page.route(url => url.pathname === (app === 'tenant' ? '/auth/me' : '/ops/auth/me'), async route => {
    if (delay) await new Promise(resolve => setTimeout(resolve, delay));
    await route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Anonymous preview.' } })
    });
  });
}

async function expectNoHorizontalOverflow(page: Page) {
  const noOverflow = await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1);
  expect(noOverflow).toBe(true);
}

async function expectRegistrationFields(page: Page, copy: TenantCopy) {
  const email = page.getByRole('textbox', { name: copy.email });
  const password = page.locator('input[autocomplete="new-password"]');
  const displayName = page.getByRole('textbox', { name: copy.displayName });
  const companyName = page.getByRole('textbox', { name: copy.companyName });
  await expect(email).toHaveAttribute('required', '');
  await expect(email).toHaveAttribute('autocomplete', 'email');
  await expect(password).toHaveAttribute('aria-label', copy.password);
  await expect(password).toHaveAttribute('required', '');
  await expect(password).toHaveAttribute('autocomplete', 'new-password');
  await expect(password).toHaveAttribute('placeholder', copy.passwordPlaceholder);
  await expect(displayName).toHaveAttribute('required', '');
  await expect(displayName).toHaveAttribute('autocomplete', 'name');
  await expect(displayName).toHaveAttribute('placeholder', copy.displayNamePlaceholder);
  await expect(companyName).toHaveAttribute('required', '');
  await expect(companyName).toHaveAttribute('autocomplete', 'organization');
  await expect(companyName).toHaveAttribute('placeholder', copy.companyNamePlaceholder);
}

async function expectResponsiveLayout(page: Page, mobile: boolean, screenshotPath: string) {
  await expect(page.getByTestId('auth-marketing')).toBeVisible({ visible: !mobile });
  await expect(page.getByTestId('auth-mobile-brand')).toBeVisible({ visible: mobile });
  await expect(page.getByRole('banner')).toBeVisible();
  await expect(page.getByRole('main')).toBeVisible();
  await expect(page.getByRole('contentinfo')).toBeVisible();
  await expectNoHorizontalOverflow(page);
  await page.screenshot({ path: screenshotPath, fullPage: true });
}

for (const viewport of [
  { name: 'desktop', width: 1280, height: 900, mobile: false },
  { name: 'mobile', width: 390, height: 844, mobile: true }
]) {
  test.describe(`${viewport.name} localized login`, () => {
    test.use({ viewport: { width: viewport.width, height: viewport.height } });

    for (const locale of ['zh-CN', 'en-US'] as const) {
      test(`Tenant ${locale} switches, persists and exposes registration fields`, async ({ page }) => {
        const copy = tenantCopy[locale];
        await seedLocale(page, locale);
        await mockAnonymousSession(page, 'tenant');
        await page.goto(`${urls.tenant}/login`);
        await expect(page.locator('html')).toHaveAttribute('lang', locale);
        await expect(page.getByRole('heading', { name: copy.title })).toBeVisible();
        await expect(page.getByRole('combobox', { name: copy.mode })).toContainText(externalStack ? copy.passwordMode : copy.dev);
        await expectResponsiveLayout(page, viewport.mobile, `test-results/visual/regent-tenant-${locale}-${viewport.name}.png`);

        await page.getByRole('combobox', { name: copy.mode }).click();
        await page.getByRole('option', { name: copy.register }).click();
        await expectRegistrationFields(page, copy);

        const otherLocale = locale === 'zh-CN' ? 'en-US' : 'zh-CN';
        const otherCopy = tenantCopy[otherLocale];
        await page.getByRole('button', { name: copy.switchLocale }).click();
        await expect(page.getByRole('heading', { name: copy.otherTitle })).toBeVisible();
        await expect(page.locator('html')).toHaveAttribute('lang', otherLocale);
        expect(await page.evaluate(() => localStorage.getItem('regent.ui.locale'))).toBe(otherLocale);
        await expectRegistrationFields(page, otherCopy);
        await expectResponsiveLayout(page, viewport.mobile, `test-results/visual/regent-tenant-${otherLocale}-registration-${viewport.name}.png`);

        await page.reload();
        await expect(page.getByRole('heading', { name: copy.otherTitle })).toBeVisible();
        await expect(page.locator('html')).toHaveAttribute('lang', otherLocale);
        await page.getByRole('combobox', { name: otherCopy.mode }).click();
        await page.getByRole('option', { name: otherCopy.register }).click();
        await expectRegistrationFields(page, otherCopy);
        await expectNoHorizontalOverflow(page);
      });

      test(`Ops ${locale} switches, persists and labels the local demo entry`, async ({ page }) => {
        const copy = opsCopy[locale];
        await seedLocale(page, locale);
        await mockAnonymousSession(page, 'ops');
        await page.goto(`${urls.ops}/login`);
        await expect(page.locator('html')).toHaveAttribute('lang', locale);
        await expect(page.getByRole('heading', { name: copy.title })).toBeVisible();
        await expect(page.getByText(copy.notice)).toBeVisible();
        await expect(page.getByRole('button', { name: copy.action })).toBeVisible();
        await expectResponsiveLayout(page, viewport.mobile, `test-results/visual/regent-ops-${locale}-${viewport.name}.png`);

        await page.getByRole('button', { name: copy.switchLocale }).click();
        await expect(page.getByRole('heading', { name: copy.otherTitle })).toBeVisible();
        const otherLocale = locale === 'zh-CN' ? 'en-US' : 'zh-CN';
        await expect(page.locator('html')).toHaveAttribute('lang', otherLocale);
        expect(await page.evaluate(() => localStorage.getItem('regent.ui.locale'))).toBe(otherLocale);
        await page.reload();
        await expect(page.getByRole('heading', { name: copy.otherTitle })).toBeVisible();
        await expect(page.locator('html')).toHaveAttribute('lang', otherLocale);
      });
    }
  });
}

test('Tenant shows localized checking and safe error feedback', async ({ page }) => {
  await seedLocale(page, 'en-US');
  await mockAnonymousSession(page, 'tenant', 500);
  await page.route('**/auth/login', route => route.fulfill({
    status: 503,
    contentType: 'application/json',
    body: JSON.stringify({ error: { code: 'INTERNAL_ERROR', message: 'sensitive-backend-marker' } })
  }));
  await page.goto(`${urls.tenant}/login`, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('progressbar', { name: tenantCopy['en-US'].checking })).toBeVisible();
  await expect(page.getByRole('heading', { name: tenantCopy['en-US'].title })).toBeVisible();
  await page.getByRole('combobox', { name: 'Authentication mode' }).click();
  await page.getByRole('option', { name: 'Password' }).click();
  await page.getByPlaceholder('At least 10 characters').fill('browser-test-password');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByText(tenantCopy['en-US'].error)).toBeVisible();
  await expect(page.getByText('sensitive-backend-marker')).toHaveCount(0);
});

test('Ops shows localized checking, submission loading and safe error feedback', async ({ page }) => {
  await seedLocale(page, 'en-US');
  await mockAnonymousSession(page, 'ops', 500);
  let releaseLogin: () => void = () => undefined;
  const loginGate = new Promise<void>(resolve => { releaseLogin = resolve; });
  await page.route('**/auth/dev-login', async route => {
    await loginGate;
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({ error: { code: 'INTERNAL_ERROR', message: 'sensitive-backend-marker' } })
    });
  });
  await page.goto(`${urls.ops}/login`, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('progressbar', { name: opsCopy['en-US'].checking })).toBeVisible();
  await page.getByRole('button', { name: opsCopy['en-US'].action }).click();
  await expect(page.getByText(opsCopy['en-US'].submitting)).toBeVisible();
  releaseLogin();
  await expect(page.getByText(opsCopy['en-US'].error)).toBeVisible();
  await expect(page.getByText('sensitive-backend-marker')).toHaveCount(0);
});

test('Tenant signed-in state offers working Continue and Logout actions', async ({ page }) => {
  skipMockedAuthInExternalStack();
  await seedLocale(page, 'en-US');
  await page.route('**/auth/me', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      email: 'browser@example.test', display_name: 'Browser Owner', tenant_id: '11111111-1111-1111-1111-111111111111',
      role: 'Owner', auth_method: 'Password', session_kind: 'Standard', capabilities: [],
      effective_access: { can_read_chat: true, can_read_collaboration: true, collaboration_scope: 'observer' }
    })
  }));
  await page.route('**/auth/logout', route => route.fulfill({ status: 204 }));
  await page.goto(`${urls.tenant}/login?returnTo=%2Fprojects%2F42`);
  await expect(page.getByRole('heading', { name: tenantCopy['en-US'].signedIn })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Continue' })).toBeVisible();
  await page.getByRole('button', { name: 'Logout' }).click();
  await expect(page.getByRole('heading', { name: tenantCopy['en-US'].title })).toBeVisible();
});

test('Ops authenticated Operator follows a safe return target', async ({ page }) => {
  skipMockedAuthInExternalStack();
  await seedLocale(page, 'en-US');
  await page.route('**/ops/auth/me', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      email: 'operator@example.test', display_name: 'Browser Operator', role: 'SRE',
      auth_method: 'Development', session_kind: 'Operator', capabilities: []
    })
  }));
  await page.route('**/ops/backups', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.route('**/ops/backup-coverage/default', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: JSON.stringify({ status: 'Unknown', enabledComponents: [], effectiveRpoMinutes: null, effectiveRtoMinutes: null, rpoStatus: 'Unknown', rtoStatus: 'Unknown' })
  }));
  await page.route('**/ops/backup-jobs', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.route('**/ops/restore-jobs', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));

  await page.goto(`${urls.ops}/login?returnTo=%2Fbackups`);
  await expect(page).toHaveURL(/\/backups$/);
  await expect(page.getByRole('navigation', { name: 'Operations navigation' })).toBeVisible();
});

test('Tenant production preview defaults to Password and omits Local development', async ({ page }) => {
  await seedLocale(page, 'en-US');
  await mockAnonymousSession(page, 'tenant');
  await page.goto(`${externalStack ? tenantUrl : tenantPreviewUrl}/login`);
  await expect(page.getByRole('heading', { name: 'Sign in to Regent Agent' })).toBeVisible();
  const mode = page.getByRole('combobox', { name: 'Authentication mode' });
  await expect(mode).toContainText('Password');
  await mode.click();
  await expect(page.getByRole('option', { name: 'Local development' })).toHaveCount(0);
  await expect(page.getByPlaceholder('At least 10 characters')).toBeVisible();
});

for (const locale of ['zh-CN', 'en-US'] as const) {
  test(`Tenant ${locale} localizes shell authentication labels and preserves a root return target`, async ({ page }) => {
    skipMockedAuthInExternalStack();
    const copy = tenantCopy[locale];
    await seedLocale(page, locale);
    await page.route('**/auth/me', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        email: 'browser@example.test', display_name: 'Browser Owner', tenant_id: '11111111-1111-1111-1111-111111111111',
        role: 'Owner', auth_method: 'Password', session_kind: 'Standard', capabilities: [],
        effective_access: { can_read_chat: true, can_read_collaboration: true, collaboration_scope: 'observer' }
      })
    }));
    await page.route('**/v1/dashboard/summary', route => route.fulfill({
      status: 200, contentType: 'application/json',
      body: JSON.stringify({ companyName: 'Browser Company', companyStatus: 'Active', generatedAt: new Date().toISOString(), cards: [] })
    }));
    await page.route('**/v1/localization', route => route.fulfill({
      status: 200, contentType: 'application/json',
      body: JSON.stringify({ locale, timeZone: 'UTC', currency: 'USD', version: 1 })
    }));
    await page.route('**/auth/logout', route => route.fulfill({ status: 204 }));

    await page.goto(`${urls.tenant}/login?returnTo=%2F%3Ftab%3Doverview%23summary`);

    await expect(page.getByRole('heading', { name: copy.signedIn })).toBeVisible();
    await page.getByRole('button', { name: locale === 'zh-CN' ? '继续' : 'Continue' }).click();
    await expect(page).toHaveURL(/\/\?tab=overview#summary$/);
    await expect(page.getByRole('button', { name: copy.logout })).toBeVisible();
    await expect(page.getByText(copy.shellSignedIn)).toBeVisible();
    const desktop = (page.viewportSize()?.width ?? 0) >= 1200;
    if (!desktop) await page.getByRole('button', { name: copy.openNavigation }).click();
    await expect(page.getByRole('link', { name: locale === 'zh-CN' ? '总览' : 'Overview' })).toBeVisible();
    if (!desktop) await page.keyboard.press('Escape');
    await page.getByRole('button', { name: copy.logout }).click();
    await expect(page.getByRole('heading', { name: copy.title })).toBeVisible();
  });

  test(`Ops ${locale} localizes shell authentication labels and preserves a root return target`, async ({ page }) => {
    skipMockedAuthInExternalStack();
    const copy = opsCopy[locale];
    await seedLocale(page, locale);
    await page.route('**/ops/auth/me', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        email: 'operator@example.test', display_name: 'Browser Operator', role: 'SRE',
        auth_method: 'Development', session_kind: 'Operator', capabilities: []
      })
    }));
    await page.route('**/ops/tenants', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
    await page.route('**/ops/support-grants', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
    await page.route('**/public-status', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ overallStatus: 'Operational', checkedAt: new Date().toISOString(), components: [], incidents: [] }) }));
    await page.route('**/ops/observability/alerts', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
    await page.route('**/ops/backups', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
    await page.route('**/auth/logout', route => route.fulfill({ status: 204 }));

    await page.goto(`${urls.ops}/login?returnTo=%2F%3Ftab%3Doverview%23summary`);

    await expect(page).toHaveURL(/\/\?tab=overview#summary$/);
    await expect(page.getByRole('button', { name: copy.logout })).toBeVisible();
    await expect(page.getByRole('banner').getByText('Browser Operator')).toBeVisible();
    const desktop = (page.viewportSize()?.width ?? 0) >= 1200;
    if (!desktop) await page.getByRole('button', { name: copy.openNavigation }).click();
    await expect(page.getByRole('link', { name: locale === 'zh-CN' ? '运营总览' : 'Overview' })).toBeVisible();
    if (!desktop) await page.keyboard.press('Escape');
    await page.getByRole('button', { name: copy.logout }).click();
    await expect(page.getByRole('heading', { name: copy.title })).toBeVisible();
  });
}
