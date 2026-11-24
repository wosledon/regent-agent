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

test('employee Agent creates groups and the control plane observes the trace', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();
  const employees = await (await page.request.get('/v1/employees')).json();
  const engineering = employees.find((employee: { displayName: string }) => employee.displayName === 'Engineering Lead');
  const quality = employees.find((employee: { displayName: string }) => employee.displayName === 'Quality Reviewer');
  const research = employees.find((employee: { displayName: string }) => employee.displayName === 'Research Analyst');
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const issueToken = async (employeeId: string) => {
    const response = await page.request.post('/auth/dev-agent-token', {
      data: { tenantId, employeeId }
    });
    expect(response.ok()).toBeTruthy();
    return (await response.json()).token as string;
  };
  const token = await issueToken(engineering.id);
  const groupResponse = await page.request.post('/agent/v1/conversations', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      title: 'Agent Release Room', kind: 'Project',
      memberEmployeeIds: [engineering.id, quality.id], scopeType: 'Project'
    }
  });
  expect(groupResponse.ok()).toBeTruthy();
  const group = await groupResponse.json();
  expect(group.creatorType).toBe('Agent');
  expect(group.createdByEmployeeId).toBe(engineering.id);

  const messageResponse = await page.request.post(`/agent/v1/conversations/${group.id}/messages`, {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      body: 'Agent checkpoint is ready for independent review.', priority: 'P1',
      messageType: 'ReviewRequest', evidenceRefs: ['task:agent-group-review']
    }
  });
  expect(messageResponse.ok()).toBeTruthy();
  const message = await messageResponse.json();
  expect(message.actorType).toBe('Agent');
  expect(message.senderEmployeeId).toBe(engineering.id);

  const outsiderToken = await issueToken(research.id);
  const outsiderRead = await page.request.get(`/agent/v1/conversations/${group.id}/messages`, {
    headers: { Authorization: `Bearer ${outsiderToken}` }
  });
  expect(outsiderRead.status()).toBe(403);

  await page.goto(`/organization/collaboration?view=network&entityType=conversation&entityId=${group.id}`);
  await expect(page.getByRole('heading', { name: 'Agent Release Room', exact: true })).toBeVisible();
  await expect(page.getByText('Agent checkpoint is ready for independent review.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Create group' })).toHaveCount(0);
  await page.screenshot({ path: '/tmp/regent-collaboration-group.png', fullPage: true });
});

test('employee Agent invokes governed tools with task-scoped identity', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();
  const employees = await (await page.request.get('/v1/employees')).json();
  const engineering = employees.find((employee: { displayName: string }) => employee.displayName === 'Engineering Lead');
  const timeline = await (await page.request.get(`/v1/employees/${engineering.id}/timeline`)).json();
  const taskId = timeline.entries.find((entry: { taskId?: string }) => entry.taskId)?.taskId;
  const tokenResponse = await page.request.post('/auth/dev-agent-token', {
    data: { tenantId: '11111111-1111-1111-1111-111111111111', employeeId: engineering.id, taskId }
  });
  const token = (await tokenResponse.json()).token as string;
  const headers = { Authorization: `Bearer ${token}` };
  const discovered = await (await page.request.get('/agent/v1/tools/discover', { headers })).json();
  expect(discovered.some((tool: { name: string }) => tool.name === 'memory.capture')).toBeTruthy();

  const invoke = async (idempotencyKey: string) => page.request.post('/agent/v1/tools/invoke', {
    headers,
    data: {
      toolName: 'memory.capture', version: '1.0.0', capability: 'memory.write',
      taskGrant: '{"capabilities":["memory.write"]}', taint: 'Low', idempotencyKey,
      argumentsJson: JSON.stringify({
        operation: 'capture', scope: 'personal', content: 'Agent tool memory smoke evidence',
        sourceRef: 'task:agent-tool-smoke', confidence: 0.8, taint: 'trusted'
      })
    }
  });
  const first = await (await invoke('agent-tool-smoke-1')).json();
  const repeated = await (await invoke('agent-tool-smoke-1')).json();
  expect(first.allowed).toBeTruthy();
  expect(first.success).toBeTruthy();
  expect(first.result.author).toBe('Engineering Lead');
  expect(first.auditHash).toBeTruthy();
  expect(repeated.auditHash).toBe(first.auditHash);

  const denied = await page.request.post('/agent/v1/tools/invoke', {
    headers,
    data: {
      toolName: 'organization.create_position', version: '1.0.0', capability: 'org.design',
      taskGrant: '*', taint: 'Low', argumentsJson: JSON.stringify({ operation: 'create_position' })
    }
  });
  expect(denied.status()).toBe(403);
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
