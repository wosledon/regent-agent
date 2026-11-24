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
    headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() },
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
    headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() },
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
  const headers = { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() };
  const discovered = await (await page.request.get('/agent/v1/tools/discover', { headers })).json();
  expect(discovered.some((tool: { name: string }) => tool.name === 'memory.capture')).toBeTruthy();

  const idempotencyKey = `agent-tool-smoke-${crypto.randomUUID()}`;
  const invoke = async () => page.request.post('/agent/v1/tools/invoke', {
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
  const first = await (await invoke()).json();
  const repeated = await (await invoke()).json();
  expect(first.allowed).toBeTruthy();
  expect(first.success).toBeTruthy();
  expect(first.result.author).toBe('Engineering Lead');
  expect(first.auditHash).toBeTruthy();
  expect(repeated.auditHash).toBe(first.auditHash);

  const denied = await page.request.post('/agent/v1/tools/invoke', {
    headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
    data: {
      toolName: 'organization.create_position', version: '1.0.0', capability: 'org.design',
      taskGrant: '*', taint: 'Low', argumentsJson: JSON.stringify({ operation: 'create_position' })
    }
  });
  expect(denied.status()).toBe(403);
});

test('additional domain tools preserve Agent and human boundaries', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();
  const employees = await (await page.request.get('/v1/employees')).json();
  const engineering = employees.find((employee: { displayName: string }) => employee.displayName === 'Engineering Lead');
  const timeline = await (await page.request.get(`/v1/employees/${engineering.id}/timeline`)).json();
  const taskId = timeline.entries.find((entry: { taskId?: string }) => entry.taskId)?.taskId;
  const issueToken = async (scopes: string[]) => {
    const response = await page.request.post('/auth/dev-agent-token', {
      data: { tenantId: '11111111-1111-1111-1111-111111111111', employeeId: engineering.id, taskId, scopes }
    });
    return (await response.json()).token as string;
  };
  const invoke = async (token: string, toolName: string, capability: string, argumentsJson: object) =>
    page.request.post('/agent/v1/tools/invoke', {
      headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() },
      data: {
        toolName, version: '1.0.0', capability, taskGrant: '*', taint: 'Low',
        argumentsJson: JSON.stringify(argumentsJson)
      }
    });

  const evolutionToken = await issueToken(['agent.evolve']);
  const prompt = await (await invoke(evolutionToken, 'agent_evolution.create_prompt', 'agent.evolve', {
    operation: 'create_prompt', name: 'Agent Evolution Smoke', content: 'Use evidence before acting.', kind: 'Operations'
  })).json();
  expect(prompt.success).toBeTruthy();
  expect(prompt.result.name).toBe('Agent Evolution Smoke');

  const performanceToken = await issueToken(['performance.action']);
  const termination = await invoke(performanceToken, 'performance.action', 'performance.action', {
    operation: 'action', employeeId: engineering.id, actionType: 'Terminate', reason: 'Must remain human-only'
  });
  expect(termination.status()).toBe(403);

  const organizationToken = await issueToken(['org.design']);
  const position = await (await invoke(organizationToken, 'organization.create_position', 'org.design', {
    operation: 'create_position', name: 'Agent Recruitment Position', responsibilities: 'Execute hiring work.',
    capabilities: ['recruitment.execute'], budgetMinor: 0
  })).json();
  expect(position.success).toBeTruthy();
  const recruitmentToken = await issueToken(['recruitment.execute']);
  const candidate = await (await invoke(recruitmentToken, 'recruitment.create_candidate', 'recruitment.execute', {
    operation: 'create_candidate', positionId: position.result.id, displayName: 'Agent Candidate',
    modelVersion: 'local-cheap-v1', jobDescription: 'Execute and verify deterministic delivery.'
  })).json();
  expect(candidate.success).toBeTruthy();
  expect(candidate.result.displayName).toBe('Agent Candidate');
});

test('employee Agents delegate work and the control plane traces the decision', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in' }).click();
  const employees = await (await page.request.get('/v1/employees')).json();
  const quality = employees.find((employee: { displayName: string }) => employee.displayName === 'Quality Reviewer');
  const engineering = employees.find((employee: { displayName: string }) => employee.displayName === 'Engineering Lead');
  const research = employees.find((employee: { displayName: string }) => employee.displayName === 'Research Analyst');
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const projects = await (await page.request.get('/v1/projects')).json();
  const activeTasks = (await Promise.all(projects.map(async (project: { id: string }) =>
    (await (await page.request.get(`/v1/projects/${project.id}`)).json()).tasks))).flat()
    .filter((task: { status: string }) => ['Pending', 'Assigned', 'InProgress', 'Blocked'].includes(task.status));
  const qualityTaskId = activeTasks.find((task: { assignee: string }) => task.assignee === quality.displayName)?.id;
  const engineeringTaskId = activeTasks.find((task: { assignee: string }) => task.assignee === engineering.displayName)?.id ?? qualityTaskId;
  expect(qualityTaskId).toBeTruthy();
  const issueToken = async (employeeId: string, taskId: string) => {
    const response = await page.request.post('/auth/dev-agent-token', { data: { tenantId, employeeId, taskId } });
    expect(response.ok()).toBeTruthy();
    return (await response.json()).token as string;
  };
  const qualityToken = await issueToken(quality.id, qualityTaskId);
  const engineeringToken = await issueToken(engineering.id, engineeringTaskId);
  const researchToken = await issueToken(research.id, engineeringTaskId);
  const discovered = await (await page.request.get('/agent/v1/tools/discover', {
    headers: { Authorization: `Bearer ${qualityToken}` }
  })).json();
  expect(discovered.some((tool: { name: string }) => tool.name === 'collaboration.delegate')).toBeTruthy();

  const criteria = `Independent evidence review ${crypto.randomUUID()}`;
  const missingKey = await page.request.post('/agent/v1/collaboration/delegations', {
    headers: { Authorization: `Bearer ${qualityToken}` },
    data: {
      taskId: qualityTaskId, delegateEmployeeId: engineering.id, weight: 2,
      acceptanceCriteria: criteria, evidenceRequirements: ['test:independent-review'],
      requiredCapabilities: [], reason: 'Balance review workload'
    }
  });
  expect(missingKey.status()).toBe(400);

  const createdResponse = await page.request.post('/agent/v1/collaboration/delegations', {
    headers: { Authorization: `Bearer ${qualityToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: {
      taskId: qualityTaskId, delegateEmployeeId: engineering.id, weight: 2,
      acceptanceCriteria: criteria, evidenceRequirements: ['test:independent-review'],
      requiredCapabilities: [], reason: 'Balance review workload'
    }
  });
  expect(createdResponse.ok()).toBeTruthy();
  const created = await createdResponse.json();
  expect(created.status).toBe('Proposed');
  expect(created.requesterEmployeeId).toBe(quality.id);
  expect(created.targetEmployeeId).toBe(engineering.id);

  const outsider = await page.request.post(`/agent/v1/collaboration/delegations/${created.id}/decision`, {
    headers: { Authorization: `Bearer ${researchToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: { accepted: true, reason: 'Not a participant' }
  });
  expect(outsider.status()).toBe(403);

  const rejected = await page.request.post(`/agent/v1/collaboration/delegations/${created.id}/decision`, {
    headers: { Authorization: `Bearer ${engineeringToken}`, 'Idempotency-Key': crypto.randomUUID() },
    data: { accepted: false, reason: 'Current WIP is reserved for release verification' }
  });
  expect(rejected.ok()).toBeTruthy();
  expect((await rejected.json()).status).toBe('Rejected');

  await page.goto('/organization/collaboration');
  await expect(page.getByRole('heading', { name: 'Delegation, handoff and interrupt process' })).toBeVisible();
  await expect(page.getByText(criteria, { exact: true })).toBeVisible();
  await expect(page.getByText('Current WIP is reserved for release verification', { exact: false }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Create group' })).toHaveCount(0);
  await page.screenshot({ path: '/tmp/regent-collaboration-coordination.png', fullPage: true });
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
