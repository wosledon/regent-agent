import { expect, test } from '@playwright/test';

test('login, dashboard, project and employee timeline', async ({ page }) => {
  await page.goto('/login');
  await page.getByPlaceholder('owner@example.test').fill('owner@example.test');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Regent Autonomous Company' })).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-dashboard.png', fullPage: true });

  await page.goto('/projects');
  await page.getByRole('link', { name: 'Open project' }).first().click();
  await expect(page.getByRole('heading', { name: 'Milestones' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Tasks' })).toBeVisible();

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

test('organization, delivery, security, lifecycle and runtime pages work end to end', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();

  await page.goto('/organization/manage');
  await expect(page.getByRole('heading', { name: 'Organization' })).toBeVisible();
  await expect(page.getByText('Company charter')).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-organization.png', fullPage: true });

  await page.goto('/projects');
  const projectHref = await page.getByRole('link', { name: 'Open project' }).first().getAttribute('href');
  await page.goto(`${projectHref}/delivery`);
  await expect(page.getByRole('heading', { name: 'Delivery lifecycle' })).toBeVisible();
  await page.getByPlaceholder('Users need deterministic approval').fill('E2E evidence finding');
  await page.getByPlaceholder('interview:owner-1').fill('e2e:source');
  await page.getByRole('button', { name: 'Add finding' }).click();
  await expect(page.getByRole('cell', { name: 'E2E evidence finding' }).first()).toBeVisible();

  await page.goto('/governance/security');
  await expect(page.getByRole('heading', { name: 'Security & governance' })).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-governance.png', fullPage: true });
  await page.getByPlaceholder('research.search').fill('e2e.research');
  await page.getByPlaceholder('research.execute').fill('research.execute');
  await page.getByRole('button', { name: 'Register immutable tool' }).click();
  await expect(page.getByRole('cell', { name: 'e2e.research@1.0.0' }).first()).toBeVisible();

  await page.goto('/governance/lifecycle');
  await expect(page.getByRole('heading', { name: 'Data lifecycle' })).toBeVisible();
  await page.getByRole('button', { name: 'Create archive manifest' }).click();
  await expect(page.getByText('2026-11').first()).toBeVisible();

  await page.goto('/models');
  await expect(page.getByRole('heading', { name: 'Models' })).toBeVisible();
  await expect(page.getByText('local-cheap').first()).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-models.png', fullPage: true });

  await page.goto('/budget/runtime');
  await expect(page.getByRole('heading', { name: 'Budget, cost & scheduling' })).toBeVisible();
  await page.screenshot({ path: '/tmp/regent-runtime.png', fullPage: true });
  await page.getByPlaceholder('usage:unique-event').fill('e2e:usage');
  await page.getByRole('button', { name: 'Record cost once' }).click();
  await expect(page.getByRole('cell', { name: 'e2e:usage' }).first()).toBeVisible();
});

test('password registration issues recovery codes and session controls', async ({ page }) => {
  const email = `e2e-${Date.now()}@example.test`;
  await page.goto('/login');
  await page.getByRole('combobox', { name: 'Authentication mode' }).click();
  await page.getByRole('option', { name: 'Create account' }).click();
  await page.getByPlaceholder('owner@example.test').fill(email);
  await page.getByPlaceholder('At least 10 characters').fill('strong-password-123');
  await page.getByPlaceholder('Display name').fill('E2E Owner');
  await page.getByPlaceholder('Company name').fill('E2E Identity Company');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByRole('heading', { name: 'Save recovery codes' })).toBeVisible();
  await page.getByRole('button', { name: 'Continue' }).click();
  await page.goto('/settings');
  await expect(page.getByRole('heading', { name: 'Sessions' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Revoke' }).first()).toBeVisible();
});

test('approval can be decided once', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();
  const created = await page.request.post('/v1/approvals', {
    data: {
      actionType: 'e2e.approval', title: 'E2E approval', impact: 'Repeatable E2E decision',
      severity: 'high', requester: 'E2E', expiresInMinutes: 60, evidenceRefs: ['e2e:evidence']
    }
  });
  expect(created.ok()).toBeTruthy();
  const approval = await created.json() as { id: string };
  await page.goto(`/governance/approvals/${approval.id}`);
  await page.getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByText('approved')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Approve' })).toBeDisabled();
});
