import { expect, test } from '@playwright/test';

test('collaboration workspace switches views and traces employee work', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Regent Autonomous Company' })).toBeVisible();

  await page.goto('/organization/collaboration');
  await expect(page.getByRole('heading', { name: 'Organization collaboration' })).toBeVisible();
  await expect(page.getByText('Delivery Room').first()).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-collaboration-office.png', fullPage: true });

  await page.getByRole('button', { name: 'Org chart', exact: true }).click();
  await expect(page.getByRole('img', { name: 'Organization collaboration graph' })).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-collaboration-org.png', fullPage: true });

  await page.getByRole('button', { name: 'Network', exact: true }).click();
  await expect(page.getByLabel('Relationship focus')).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-collaboration-network.png', fullPage: true });

  await page.getByRole('button', { name: 'Timeline', exact: true }).click();
  await expect(page.getByText('Process timeline')).toBeVisible();

  await page.getByRole('button', { name: 'Office', exact: true }).click();
  await page.getByRole('button', { name: 'Inspect Engineering Lead' }).click();
  await expect(page.getByText('Traceable process')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Employee timeline' })).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-collaboration-trace.png', fullPage: true });
});

test('collaboration groups can be created and traced with their members', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.goto('/organization/collaboration');
  await page.getByRole('button', { name: 'Create group' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByPlaceholder('Release war room').fill('Release War Room');
  await dialog.getByLabel('Employees').fill('Engineering Lead');
  await page.getByRole('option', { name: 'Engineering Lead' }).click();
  await dialog.getByLabel('Employees').fill('Quality Reviewer');
  await page.getByRole('option', { name: 'Quality Reviewer' }).click();
  await dialog.getByRole('button', { name: 'Create group' }).click();

  await expect(page.getByRole('heading', { name: 'Release War Room', exact: true })).toBeVisible();
  await expect(page.getByText('Engineering Lead').last()).toBeVisible();
  await expect(page.getByText('Quality Reviewer').last()).toBeVisible();
  await expect(page.getByRole('link', { name: 'Open messages' })).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-collaboration-group.png', fullPage: true });
});

test('collaboration office remains usable on a mobile viewport', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.goto('/organization/collaboration');
  await expect(page.getByRole('heading', { name: 'Office floor' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Inspect Engineering Lead' })).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow).toBeLessThanOrEqual(1);
  await page.screenshot({ path: '/tmp/regent-collaboration-mobile.png', fullPage: true });
});
