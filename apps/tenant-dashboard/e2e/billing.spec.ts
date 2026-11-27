import { expect, test } from '@playwright/test';
import { requireDevelopmentAuth } from './support/externalStack';

requireDevelopmentAuth();

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('regent.ui.locale', 'en-US'));
});

test('owner manages local billing and validates a tenant export package', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.goto('/billing');
  await expect(page.getByRole('heading', { name: 'Plan & Billing' })).toBeVisible();
  await expect(page.getByText('Plan quotas')).toBeVisible();
  await expect(page.getByText('Invoices and ledger')).toBeVisible();

  await page.goto('/settings');
  await expect(page.getByText('Tenant data export and migration package')).toBeVisible();
  await page.getByRole('button', { name: 'Create export package' }).click();
  await page.getByRole('button', { name: 'Validate' }).last().click();
  await expect(page.getByText('Validated').last()).toBeVisible();
});
