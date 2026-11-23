import { expect, test } from '@playwright/test';

test('login, dashboard, project and employee timeline', async ({ page }) => {
  await page.goto('/login');
  await page.getByPlaceholder('owner@example.test').fill('owner@example.test');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Regent Autonomous Company' })).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-dashboard.png', fullPage: true });

  await page.goto('/projects');
  await page.getByRole('link', { name: 'Open project' }).first().click();
  await expect(page.getByText('Milestones')).toBeVisible();
  await expect(page.getByText('Tasks')).toBeVisible();

  await page.goto('/organization');
  await page.getByRole('link', { name: 'View work timeline' }).nth(1).click();
  await expect(page.getByText('Work timeline')).toBeVisible();
  await expect(page.getByText('Submitted dashboard vertical slice')).toBeVisible();
});

test('workflows, messages, evaluations and compliance are available', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();

  await page.goto('/workflows');
  await expect(page.getByText('Product Delivery v1')).toBeVisible();
  await page.getByRole('button', { name: 'Start run' }).first().click();
  await expect(page.getByText('Current run')).toBeVisible();

  await page.goto('/messages');
  await expect(page.getByText('Local workspace is ready. Create a task or review the dashboard.')).toBeVisible();

  await page.goto('/evaluations');
  await expect(page.getByText('Core Capability v1')).toBeVisible();
  await page.getByRole('button', { name: 'Run evaluation' }).first().click();
  await expect(page.getByText('Approved').first()).toBeVisible();

  await page.goto('/governance/compliance');
  await expect(page.getByRole('heading', { name: 'Policy' })).toBeVisible();
});

test('operations console supports tenant and grant views', async ({ page }) => {
  await page.goto('http://localhost:4173/login');
  await page.getByRole('button', { name: 'Sign in as Demo Operator' }).click();
  await expect(page.getByRole('heading', { name: 'Operations Overview' })).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-ops.png', fullPage: true });
  await page.goto('http://localhost:4173/tenants');
  await expect(page.getByRole('heading', { name: 'Tenants' })).toBeVisible();
  await expect(page.getByText('Regent Demo')).toBeVisible();
  await page.goto('http://localhost:4173/support');
  await expect(page.getByRole('heading', { name: 'Support Grants' })).toBeVisible();
});

test('approval can be decided once', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.goto('/governance/approvals');
  await page.getByRole('link', { name: 'Review approval' }).first().click();
  await page.getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByText('approved')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Approve' })).toBeDisabled();
});
