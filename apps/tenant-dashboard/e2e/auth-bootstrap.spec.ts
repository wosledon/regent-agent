import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('regent.ui.locale', 'en-US'));
});

async function removeRandomUuid(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    Object.defineProperty(crypto, 'randomUUID', { configurable: true, value: undefined });
  });
}

test('cookie auth survives missing randomUUID, reload, new tab, 401 and failed logout', async ({ page, context }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await removeRandomUuid(page);
  const email = `auth-bootstrap-${Date.now()}@example.test`;
  const password = 'strong-password-123';

  await page.goto('/login?returnTo=%2F');
  await page.getByRole('combobox', { name: 'Authentication mode' }).click();
  await page.getByRole('option', { name: 'Create account' }).click();
  await page.getByPlaceholder('owner@example.test').fill(email);
  await page.getByPlaceholder('At least 10 characters').fill(password);
  await page.getByPlaceholder('Display name').fill('Bootstrap Owner');
  await page.getByPlaceholder('Company name').fill('Bootstrap Company');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByRole('heading', { name: 'Save recovery codes' })).toBeVisible();
  await page.getByRole('checkbox', { name: 'I saved these recovery codes in a secure location.' }).check();
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('heading', { name: 'Bootstrap Company' })).toBeVisible();

  await page.reload();
  await expect(page.getByRole('heading', { name: 'Bootstrap Company' })).toBeVisible();

  const newTab = await context.newPage();
  const newTabErrors: string[] = [];
  newTab.on('pageerror', error => newTabErrors.push(error.message));
  await removeRandomUuid(newTab);
  await newTab.goto('/');
  await expect(newTab.getByRole('heading', { name: 'Bootstrap Company' })).toBeVisible();

  await page.goto('/projects');
  await page.getByRole('button', { name: 'Create project' }).click();
  await page.locator('input').first().fill(`Bootstrap project ${Date.now()}`);
  await page.locator('textarea:not([readonly])').fill('Verify cookie-authenticated writes without randomUUID.');
  await page.getByRole('button', { name: 'Create project' }).click();
  await expect(page.getByRole('link', { name: /Bootstrap project/ }).first()).toBeVisible();

  await newTab.route('**/auth/logout', route => route.abort('failed'));
  await newTab.getByRole('button', { name: 'Logout' }).click();
  await expect(newTab).toHaveURL(/\/login$/);
  await expect(newTab.getByText('Sign in to Regent Agent')).toBeVisible();
  await newTab.getByRole('combobox', { name: 'Authentication mode' }).click();
  await newTab.getByRole('option', { name: 'Password' }).click();
  await newTab.getByPlaceholder('owner@example.test').fill(email);
  await newTab.getByPlaceholder('At least 10 characters').fill(password);
  await newTab.getByRole('button', { name: 'Sign in' }).click();
  await expect(newTab.getByRole('heading', { name: 'Bootstrap Company' })).toBeVisible();

  await page.route('**/v1/dashboard/summary', route => route.fulfill({
    status: 401,
    contentType: 'application/json',
    body: JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Session expired.' } })
  }));
  await page.goto('/');
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByText('Sign in to Regent Agent')).toBeVisible();
  await expect(page.getByText(/Signed in as/)).toHaveCount(0);

  expect(errors.filter(message => message.includes('randomUUID'))).toEqual([]);
  expect(newTabErrors.filter(message => message.includes('randomUUID'))).toEqual([]);
  await newTab.close();
});
