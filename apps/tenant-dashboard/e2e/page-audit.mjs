/* global process, URL, setTimeout, clearTimeout, document, window, localStorage, console */
import { chromium } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const tenantBase = process.env.TENANT_URL ?? 'http://localhost:5173';
const opsBase = process.env.OPS_URL ?? 'http://localhost:4173';
const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const appDirectory = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(appDirectory, '../..');
const reportPath = path.join(appDirectory, 'test-results/page-audit/report.json');
const viewports = [
  { name: 'desktop', width: 1440, height: 1000 },
  { name: 'mobile', width: 390, height: 844 }
];

const report = {
  schemaVersion: 1,
  startedAt: new Date().toISOString(),
  setup: {},
  tenantRoutes: [],
  opsRoutes: [],
  permissionChecks: [],
  viewportChecks: 0,
  pageErrors: [],
  failedRequests: [],
  overflow: [],
  blankPages: [],
  permissionLeaks: [],
  cleanup: 'not-run',
  blockers: [],
  warnings: [],
  success: false
};

let browser;
let tenantContext;
let opsContext;
let tenantId;

function safeUrl(value) {
  try {
    const url = new URL(value);
    url.username = '';
    url.password = '';
    for (const key of [...url.searchParams.keys()]) {
      if (/token|secret|password|cookie|authorization|code|state|key/i.test(key)) url.searchParams.set(key, '[redacted]');
    }
    return url.toString();
  } catch {
    return String(value).replace(/(Bearer\s+)\S+/gi, '$1[redacted]');
  }
}

function safeText(value) {
  return String(value)
    .replace(/(Bearer\s+)\S+/gi, '$1[redacted]')
    .replace(/((?:token|secret|password|cookie|authorization|api[_-]?key)\s*[=:]\s*)[^\s,;]+/gi, '$1[redacted]')
    .slice(0, 1000);
}

function unique(values) {
  return [...new Set(values)];
}

function isAnonymousAuthResponse(response) {
  if (response.status() !== 401) return false;
  const pathname = new URL(response.url()).pathname;
  return pathname === '/auth/me' || pathname === '/ops/auth/me';
}



function attachCapture(page) {
  const capture = { consoleErrors: [], pageErrors: [], requestFailures: [], abortedRequests: [], httpErrors: [], successfulApiResponses: [], pending: new Set() };
  page.on('console', message => {
    if (message.type() === 'error') capture.consoleErrors.push({ text: safeText(message.text()), url: safeUrl(message.location().url ?? '') });
  });
  page.on('pageerror', error => capture.pageErrors.push({ text: safeText(error.message), stack: safeText(error.stack ?? '') }));
  page.on('request', request => {
    if (request.resourceType() === 'fetch' || request.resourceType() === 'xhr') capture.pending.add(request);
  });
  page.on('requestfinished', request => capture.pending.delete(request));
  page.on('requestfailed', request => {
    capture.pending.delete(request);
    const failure = {
      method: request.method(),
      url: safeUrl(request.url()),
      resourceType: request.resourceType(),
      error: safeText(request.failure()?.errorText ?? 'unknown request failure')
    };
    if (failure.error === 'net::ERR_ABORTED') capture.abortedRequests.push(failure);
    else capture.requestFailures.push(failure);
  });
  page.on('response', response => {
    if (response.status() >= 400) {
      capture.httpErrors.push({
        status: response.status(),
        method: response.request().method(),
        url: safeUrl(response.url()),
        resourceType: response.request().resourceType()
      });
    } else if (response.status() >= 200 && response.status() < 300) {
      const pathname = new URL(response.url()).pathname;
      if (pathname.startsWith('/v1/') || pathname.startsWith('/ops/')) {
        capture.successfulApiResponses.push({
          status: response.status(),
          method: response.request().method(),
          url: safeUrl(response.url()),
          resourceType: response.request().resourceType()
        });
      }
    }
  });
  return capture;
}

async function waitForRequestsToSettle(capture, timeoutMs = 15000) {
  const deadline = Date.now() + timeoutMs;
  let quietSince = 0;
  while (Date.now() < deadline) {
    if (capture.pending.size === 0) {
      quietSince ||= Date.now();
      if (Date.now() - quietSince >= 250) return;
    } else {
      quietSince = 0;
    }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw new Error(`Requests did not settle within ${timeoutMs}ms (${capture.pending.size} pending)`);
}

async function waitForReady(page, capture) {
  await page.locator('#root').waitFor({ state: 'visible', timeout: 15000 });
  await page.waitForFunction(() => {
    const root = document.querySelector('#root');
    const scope = document.querySelector('main') ?? root;
    const text = scope?.textContent?.trim() ?? '';
    const progress = document.querySelector('[role="progressbar"]');
    const loading = [...document.querySelectorAll('[aria-label]')].some(node => /loading|checking/i.test(node.getAttribute('aria-label') ?? ''));
    return text.length > 0 && !progress && !loading;
  }, null, { timeout: 20000 });
  await waitForRequestsToSettle(capture);
  await page.waitForFunction(() => !document.querySelector('[role="progressbar"]'), null, { timeout: 5000 });
}

async function captureSnapshot(page) {
  return page.evaluate(() => {
    const scope = document.querySelector('main') ?? document.querySelector('#root');
    const text = scope?.innerText?.trim() ?? '';
    const meaningfulNode = scope?.querySelector('h1,h2,h3,h4,h5,h6,form,table,button,input,select,textarea,a,[role="alert"]');
    const viewportWidth = window.innerWidth;
    const documentWidth = document.documentElement.scrollWidth;
    const bodyWidth = document.body?.scrollWidth ?? 0;
    const overflow = Math.max(documentWidth, bodyWidth) > viewportWidth + 2;
    const offenders = overflow ? [...document.querySelectorAll('body *')].map(node => {
      const rect = node.getBoundingClientRect();
      return { node, rect };
    }).filter(({ rect }) => rect.width > 0 && (rect.right > viewportWidth + 2 || rect.left < -2)).slice(0, 12).map(({ node, rect }) => {
      const tag = node.tagName.toLowerCase();
      const id = node.id ? `#${node.id}` : '';
      const classes = typeof node.className === 'string' && node.className.trim()
        ? `.${node.className.trim().split(/\s+/).slice(0, 3).join('.')}` : '';
      return `${tag}${id}${classes} left=${Math.round(rect.left)} right=${Math.round(rect.right)} width=${Math.round(rect.width)}`;
    }) : [];
    return {
      textLength: text.length,
      meaningfulNode: Boolean(meaningfulNode),
      blank: text.length === 0 || (!meaningfulNode && text.length < 80),
      mainVisible: Boolean(document.querySelector('main')),
      headingCount: scope?.querySelectorAll('h1,h2,h3,h4,h5,h6').length ?? 0,
      loadingVisible: Boolean(document.querySelector('[role="progressbar"]')),
      errorAlertCount: scope?.querySelectorAll('[role="alert"]').length ?? 0,
      viewportWidth,
      documentWidth,
      bodyWidth,
      overflow,
      offenders
    };
  });
}

function checkLabel(app, route, viewport) {
  return `${app} ${viewport.width}x${viewport.height} ${route}`;
}

async function auditRoute(context, base, route, app, viewport) {
  const page = await context.newPage();
  await page.setViewportSize({ width: viewport.width, height: viewport.height });
  const capture = attachCapture(page);
  let response = null;
  let navigationError = null;
  let readinessError = null;
  try {
    response = await page.goto(base + route, { waitUntil: 'domcontentloaded', timeout: 30000 });
    await waitForReady(page, capture);
  } catch (error) {
    if (!response) navigationError = safeText(error.message);
    else readinessError = safeText(error.message);
  }
  const snapshot = await captureSnapshot(page).catch(error => ({
    textLength: 0, meaningfulNode: false, blank: true, mainVisible: false, headingCount: 0,
    loadingVisible: false, errorAlertCount: 0, viewportWidth: viewport.width,
    documentWidth: 0, bodyWidth: 0, overflow: false, offenders: [], snapshotError: safeText(error.message)
  }));
  const finalUrl = safeUrl(page.url());
  const routeFailures = [];
  if (navigationError) routeFailures.push(`navigation failed: ${navigationError}`);
  if (readinessError) routeFailures.push(`readiness failed: ${readinessError}`);
  if (snapshot.snapshotError) routeFailures.push(`snapshot failed: ${snapshot.snapshotError}`);
  if (!response) routeFailures.push('document response missing');
  else if (response.status() >= 400) routeFailures.push(`document status ${response.status()}`);
  try {
    const expectedPath = new URL(route, base).pathname;
    const actualPath = new URL(page.url()).pathname;
    if (actualPath !== expectedPath) routeFailures.push(`final path ${actualPath} did not match ${expectedPath}`);
  } catch (error) {
    routeFailures.push(`final URL invalid: ${safeText(error.message)}`);
  }
  if (snapshot.loadingVisible) routeFailures.push('loading indicator remained visible');
  if (snapshot.errorAlertCount > 0) routeFailures.push(`${snapshot.errorAlertCount} visible error alert(s)`);
  const check = {
    viewport: viewport.name,
    width: viewport.width,
    height: viewport.height,
    route,
    documentStatus: response?.status() ?? null,
    contentType: response?.headers()['content-type'] ?? null,
    finalUrl,
    ready: !navigationError && !readinessError && !snapshot.snapshotError,
    ...snapshot,
    pageErrors: [
      ...capture.consoleErrors.map(error => ({ type: 'console.error', ...error })),
      ...capture.pageErrors.map(error => ({ type: 'pageerror', ...error }))
    ],
    failedRequests: [
      ...capture.httpErrors.map(error => ({ type: 'http', ...error })),
      ...capture.requestFailures.map(error => ({ type: 'requestfailed', ...error }))
    ],
    abortedRequests: capture.abortedRequests,
    routeFailures
  };
  await page.close().catch(() => undefined);
  return check;
}

async function auditRoutePair(context, base, route, app, group) {
  const entry = { route, checks: [] };
  for (const viewport of viewports) entry.checks.push(await auditRoute(context, base, route, app, viewport));
  group.push(entry);
}

async function setupRequest(context, method, url, data) {
  const headers = {};
  if (data !== undefined) headers['Content-Type'] = 'application/json';
  if (method !== 'GET') headers['Idempotency-Key'] = randomUUID();
  let response;
  try {
    response = await context.request.fetch(url, {
      method,
      headers,
      data: data === undefined ? undefined : JSON.stringify(data)
    });
  } catch (error) {
    report.setup.requests ??= [];
    report.setup.requests.push({ method, url: safeUrl(url), status: null, error: safeText(error.message) });
    throw new Error(`${method} ${safeUrl(url)} failed: ${safeText(error.message)}`, { cause: error });
  }
  const status = response.status();
  report.setup.requests ??= [];
  report.setup.requests.push({ method, url: safeUrl(url), status });
  if (status < 200 || status >= 300) {
    report.setup.httpErrors ??= [];
    report.setup.httpErrors.push({ method, url: safeUrl(url), status });
    throw new Error(`${method} ${safeUrl(url)} returned HTTP ${status}`);
  }
  const body = status === 204 ? null : await response.json().catch(() => null);
  return { response, status, body };
}

async function createBrowserContext(viewport = viewports[0]) {
  const context = await browser.newContext({
    viewport: { width: viewport.width, height: viewport.height },
    locale: 'en-US',
    serviceWorkers: 'block'
  });
  await context.addInitScript(() => localStorage.setItem('regent.ui.locale', 'en-US'));
  return context;
}

async function setupTenant() {
  const suffix = `${Date.now()}-${randomUUID().slice(0, 8)}`;
  const email = `page-audit-${suffix}@example.test`;
  const password = `Aa1!${randomUUID()}${randomUUID()}`;
  const displayName = 'Page Audit';
  const companyName = `Page Audit Company ${suffix}`;
  const projectName = `Audit project ${suffix}`;
  const registered = await setupRequest(tenantContext, 'POST', `${tenantBase}/auth/register`, {
    email, password, displayName, companyName
  });
  tenantId = registered.body?.tenant_id ?? null;
  report.setup.tenantRegistered = true;
  report.setup.registrationStatus = registered.status;

  const session = await setupRequest(tenantContext, 'GET', `${tenantBase}/auth/me`);
  tenantId ||= session.body?.tenant_id ?? null;
  if (!tenantId) throw new Error('Tenant registration did not return a tenant ID.');
  report.setup.tenantId = tenantId;

  const company = await setupRequest(tenantContext, 'POST', `${tenantBase}/v1/company/start`);
  report.setup.companyStartStatus = company.status;

  const project = await setupRequest(tenantContext, 'POST', `${tenantBase}/v1/projects`, {
    name: projectName,
    owner: 'Controller',
    mission: 'Disposable all-route browser audit fixture',
    targetDays: 7,
    budgetMinor: 100000,
    currency: 'CNY'
  });
  const projectId = project.body?.project?.id ?? project.body?.id ?? null;
  if (!projectId) throw new Error('Project creation did not return a project ID.');
  report.setup.projectCreateStatus = project.status;
  report.setup.projectId = projectId;

  const employees = await setupRequest(tenantContext, 'GET', `${tenantBase}/v1/employees`);
  const employeeId = Array.isArray(employees.body) ? employees.body[0]?.id ?? null : null;
  if (!employeeId) throw new Error('Company start did not create an employee fixture.');
  report.setup.employeeFixtureStatus = employees.status;
  report.setup.employeeId = employeeId;

  const approval = await setupRequest(tenantContext, 'POST', `${tenantBase}/v1/approvals`, {
    actionType: 'page-audit.fixture',
    title: `Page audit approval ${suffix}`,
    impact: 'Disposable fixture for the approval detail route',
    severity: 'low',
    requester: displayName,
    expiresInMinutes: 60,
    evidenceRefs: ['page-audit:fixture']
  });
  const approvalId = approval.body?.id ?? null;
  if (!approvalId) throw new Error('Approval creation did not return an approval ID.');
  report.setup.approvalCreateStatus = approval.status;
  report.setup.approvalId = approvalId;

  const projectCheck = await setupRequest(tenantContext, 'GET', `${tenantBase}/v1/projects/${projectId}`);
  const employeeCheck = await setupRequest(tenantContext, 'GET', `${tenantBase}/v1/employees/${employeeId}/timeline`);
  const approvalCheck = await setupRequest(tenantContext, 'GET', `${tenantBase}/v1/approvals/${approvalId}`);
  report.setup.fixturesValidated = Boolean(projectCheck.body && employeeCheck.body && approvalCheck.body);
  if (!report.setup.fixturesValidated) throw new Error('One or more dynamic fixtures could not be validated.');

  return { companyName, projectName, displayName, projectId, employeeId, approvalId };
}

async function setupOps() {
  const login = await setupRequest(opsContext, 'POST', `${opsBase}/auth/dev-login`);
  report.setup.opsLoginStatus = login.status;
  const session = await setupRequest(opsContext, 'GET', `${opsBase}/ops/auth/me`);
  report.setup.opsSessionStatus = session.status;
  report.setup.opsLoggedIn = Boolean(session.body);
  if (!session.body) throw new Error('Operations demo login did not establish a session.');
}

async function runPermissionPage(context, base, route, app, markers) {
  const page = await context.newPage();
  await page.setViewportSize({ width: viewports[0].width, height: viewports[0].height });
  const capture = attachCapture(page);
  let readinessError = null;
  try {
    await page.goto(base + route, { waitUntil: 'domcontentloaded', timeout: 30000 });
    await waitForReady(page, capture);
  } catch (error) {
    readinessError = safeText(error.message);
  }
  const finalUrl = safeUrl(page.url());
  const bodyText = await page.locator('body').innerText().catch(() => '');
  const leakedMarkers = markers.filter(marker => marker && bodyText.includes(marker));
  const unexpectedHttp = capture.httpErrors.filter(error => !isAnonymousAuthResponse({ status: () => error.status, url: () => error.url }));
  const unexpectedConsole = capture.consoleErrors.filter(error => {
    if (!error.url) return true;
    const pathname = new URL(error.url).pathname;
    return !((pathname === '/auth/me' || pathname === '/ops/auth/me') && /401/.test(error.text));
  });
  const successfulProtectedApis = capture.successfulApiResponses.filter(response => {
    const pathname = new URL(response.url).pathname;
    return pathname !== '/auth/me' && pathname !== '/ops/auth/me';
  });
  let redirectedToLogin = false;
  try { redirectedToLogin = new URL(page.url()).pathname === '/login'; } catch { /* handled as a failure below. */ }
  const failures = [];
  if (readinessError) failures.push(`readiness failed: ${readinessError}`);
  if (!redirectedToLogin) failures.push(`final URL ${finalUrl} was not the login page`);
  if (leakedMarkers.length) failures.push(`protected markers rendered: ${leakedMarkers.join(', ')}`);
  if (unexpectedHttp.length) failures.push(`unexpected HTTP errors: ${unexpectedHttp.map(error => `${error.status} ${error.method} ${error.url}`).join('; ')}`);
  if (unexpectedConsole.length) failures.push(`unexpected console errors: ${unexpectedConsole.map(error => error.text).join('; ')}`);
  if (capture.pageErrors.length) failures.push(`page errors: ${capture.pageErrors.map(error => error.text).join('; ')}`);
  if (capture.requestFailures.length) failures.push(`request failures: ${capture.requestFailures.map(error => `${error.error} ${error.url}`).join('; ')}`);
  if (successfulProtectedApis.length) failures.push(`protected API returned success: ${successfulProtectedApis.map(response => `${response.status} ${response.url}`).join('; ')}`);
  const check = {
    app,
    route,
    finalUrl,
    redirectedToLogin,
    expectedAnonymousAuthResponses: capture.httpErrors.filter(error => isAnonymousAuthResponse({ status: () => error.status, url: () => error.url })).length,
    leakedMarkers,
    successfulProtectedApis,
    failures
  };
  report.permissionChecks.push(check);
  if (failures.length) report.permissionLeaks.push(`${app} ${route}: ${failures.join(' | ')}`);
  await page.close().catch(() => undefined);
  return check;
}

async function probeAnonymousPage(base, route, app, markers) {
  const context = await createBrowserContext();
  try { return await runPermissionPage(context, base, route, app, markers); }
  finally { await context.close(); }
}

async function probeAnonymousApi(base, route, app, markers) {
  const context = await browser.newContext();
  try {
    const response = await context.request.get(base + route);
    const status = response.status();
    const body = await response.text();
    const leakedMarkers = markers.filter(marker => marker && body.includes(marker));
    const allowed = status === 401 || status === 403;
    const failures = [];
    if (!allowed) failures.push(`expected HTTP 401/403, received ${status}`);
    if (leakedMarkers.length) failures.push(`response contained protected markers: ${leakedMarkers.join(', ')}`);
    report.permissionChecks.push({ app, route, type: 'anonymous-api', status, leakedMarkers, failures });
    if (failures.length) report.permissionLeaks.push(`${app} API ${route}: ${failures.join(' | ')}`);
  } finally {
    await context.close();
  }
}

async function cleanupTenant() {
  if (!tenantId) {
    report.cleanup = 'not-required: no disposable tenant was created';
    return;
  }
  report.cleanup = 'attempting approved ControlPlane tenant purge';
  const result = await new Promise(resolve => {
    const child = spawn('docker', [
      'compose', 'exec', '-T', 'controlplane', 'dotnet', 'RegentAgent.ControlPlane.dll',
      '--purge-tenants', 'PURGE', tenantId
    ], { cwd: repositoryRoot, stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '';
    let stderr = '';
    const timeout = setTimeout(() => child.kill('SIGTERM'), 120000);
    child.stdout.on('data', chunk => { stdout += chunk.toString(); });
    child.stderr.on('data', chunk => { stderr += chunk.toString(); });
    child.on('error', error => resolve({ code: null, error: safeText(error.message), stdout, stderr }));
    child.on('close', code => {
      clearTimeout(timeout);
      resolve({ code, stdout, stderr });
    });
  });
  if (result.code !== 0) {
    const reason = result.error ? `spawn error: ${result.error}` : `exit code ${result.code}`;
    report.cleanup = `failed: approved ControlPlane purge ${reason}`;
    report.blockers.push(report.cleanup);
    return;
  }
  const proof = result.stdout.match(/proof=([0-9a-f-]{36})/i)?.[1] ?? null;
  if (proof) report.cleanupProof = proof;
  let verification = 'not-verified';
  if (tenantContext) {
    try {
      const response = await tenantContext.request.get(`${tenantBase}/auth/me`);
      verification = `session check HTTP ${response.status()}`;
      if (response.status() === 200) {
        report.cleanup = 'failed: purge command succeeded but the disposable session remained valid';
        report.blockers.push(report.cleanup);
        return;
      }
    } catch (error) {
      verification = `session check failed: ${safeText(error.message)}`;
      report.warnings.push(`Cleanup verification warning: ${verification}`);
    }
  }
  report.cleanup = `success: approved ControlPlane purge completed${proof ? ` (proof ${proof})` : ''}; ${verification}`;
}

function buildSummary() {
  const checks = [
    ...report.tenantRoutes.flatMap(route => route.checks.map(check => ({ app: 'Tenant', route: route.route, ...check }))),
    ...report.opsRoutes.flatMap(route => route.checks.map(check => ({ app: 'Ops', route: route.route, ...check })))
  ];
  const pageErrors = [];
  const failedRequests = [];
  const overflow = [];
  const blankPages = [];
  const blockers = [...report.blockers];
  for (const check of checks) {
    const label = checkLabel(check.app, check.route, check);
    for (const error of check.pageErrors) {
      pageErrors.push(`${label}: ${error.type} ${error.text}${error.url ? ` [${error.url}]` : ''}`);
    }
    for (const error of check.failedRequests) {
      failedRequests.push(error.type === 'http'
        ? `${label}: HTTP ${error.status} ${error.method} ${error.url}`
        : `${label}: requestfailed ${error.method} ${error.url} (${error.error})`);
    }
    if (check.overflow) {
      overflow.push(`${label}: horizontal overflow viewport=${check.viewportWidth} document=${check.documentWidth} body=${check.bodyWidth}${check.offenders.length ? `; ${check.offenders.join(' | ')}` : ''}`);
    }
    if (check.blank) blankPages.push(`${label}: blank page textLength=${check.textLength} meaningfulNode=${check.meaningfulNode}`);
    for (const failure of check.routeFailures) blockers.push(`${label}: ${failure}`);
  }
  for (const error of report.setup.httpErrors ?? []) {
    failedRequests.push(`setup: HTTP ${error.status} ${error.method} ${error.url}`);
  }
  if (report.setup.error) blockers.push(`setup/audit aborted: ${report.setup.error}`);
  report.viewportChecks = checks.length;
  report.counts = {
    tenantRoutes: report.tenantRoutes.length,
    opsRoutes: report.opsRoutes.length,
    viewportChecks: checks.length,
    permissionChecks: report.permissionChecks.length
  };
  report.pageErrors = unique(pageErrors);
  report.failedRequests = unique(failedRequests);
  report.abortedRequests = unique(checks.flatMap(check => (check.abortedRequests ?? []).map(request =>
    `${checkLabel(check.app, check.route, check)}: requestaborted ${request.method} ${request.url}`)));
  report.overflow = unique(overflow);
  report.blankPages = unique(blankPages);
  report.permissionLeaks = unique(report.permissionLeaks);
  report.blockers = unique(blockers);
  report.warnings = unique(report.warnings);
  report.success = report.blockers.length === 0
    && report.pageErrors.length === 0
    && report.failedRequests.length === 0
    && report.overflow.length === 0
    && report.blankPages.length === 0
    && report.permissionLeaks.length === 0
    && report.cleanup.startsWith('success');
  report.completedAt = new Date().toISOString();
}

function tenantRouteList(fixtures) {
  return [
    '/',
    '/butler',
    '/projects',
    `/projects/${fixtures.projectId}`,
    `/projects/${fixtures.projectId}/delivery`,
    '/organization',
    '/organization/manage',
    '/organization/collaboration',
    `/organization/employees/${fixtures.employeeId}`,
    '/governance/approvals',
    `/governance/approvals/${fixtures.approvalId}`,
    '/governance/action-center',
    '/governance/notifications',
    '/governance/incidents',
    '/governance/compliance',
    '/governance/security',
    '/governance/lifecycle',
    '/messages',
    '/workflows',
    '/knowledge',
    '/performance',
    '/performance/actions',
    '/recruitment',
    '/agent-governance',
    '/evaluations',
    '/budget',
    '/budget/runtime',
    '/billing',
    '/models',
    '/journal',
    '/settings',
    '/dev/components'
  ];
}

const opsRoutes = ['/', '/tenants', '/support', '/backups', '/audit', '/platform', '/observability', '/status'];

await mkdir(path.dirname(reportPath), { recursive: true });
try {
  for (const base of [tenantBase, opsBase]) {
    const url = new URL(base);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') throw new Error(`Unsupported URL protocol for ${safeUrl(base)}`);
  }
  report.setup.tenantUrl = tenantBase;
  report.setup.opsUrl = opsBase;
  browser = await chromium.launch({ headless: true });
  tenantContext = await createBrowserContext();
  opsContext = await createBrowserContext();

  const fixtures = await setupTenant();
  report.setup.fixtureNames = { companyName: fixtures.companyName, projectName: fixtures.projectName, displayName: fixtures.displayName };
  const tenantRoutes = tenantRouteList(fixtures);
  if (tenantRoutes.length !== 32) throw new Error(`Expected 32 Tenant routes, declared ${tenantRoutes.length}.`);
  if (opsRoutes.length !== 8) throw new Error(`Expected 8 Ops routes, declared ${opsRoutes.length}.`);
  report.setup.tenantRoutesDeclared = tenantRoutes.length;
  report.setup.opsRoutesDeclared = opsRoutes.length;

  for (const route of tenantRoutes) await auditRoutePair(tenantContext, tenantBase, route, 'Tenant', report.tenantRoutes);
  await setupOps();
  for (const route of opsRoutes) await auditRoutePair(opsContext, opsBase, route, 'Ops', report.opsRoutes);

  const tenantMarkers = [fixtures.companyName, fixtures.projectName, fixtures.displayName, 'Signed in as', 'Logout'];
  const opsMarkers = ['Demo Operator', 'Operations Overview', 'Support Grants', 'Backup coverage', 'Operational'];
  for (const route of tenantRoutes.filter(route => route !== '/dev/components')) {
    try { await probeAnonymousPage(tenantBase, route, 'Tenant', tenantMarkers); }
    catch (error) { report.permissionLeaks.push(`Tenant ${route}: permission probe failed: ${safeText(error.message)}`); }
  }
  for (const route of opsRoutes.filter(route => route !== '/status')) {
    try { await probeAnonymousPage(opsBase, route, 'Ops', opsMarkers); }
    catch (error) { report.permissionLeaks.push(`Ops ${route}: permission probe failed: ${safeText(error.message)}`); }
  }
  for (const route of [
    `/v1/projects/${fixtures.projectId}`,
    `/v1/employees/${fixtures.employeeId}/timeline`,
    `/v1/approvals/${fixtures.approvalId}`,
    '/v1/dashboard/summary'
  ]) {
    try { await probeAnonymousApi(tenantBase, route, 'Tenant', tenantMarkers); }
    catch (error) { report.permissionLeaks.push(`Tenant API ${route}: permission probe failed: ${safeText(error.message)}`); }
  }
  for (const route of ['/ops/tenants', '/ops/support-grants', '/ops/audit']) {
    try { await probeAnonymousApi(opsBase, route, 'Ops', opsMarkers); }
    catch (error) { report.permissionLeaks.push(`Ops API ${route}: permission probe failed: ${safeText(error.message)}`); }
  }
  try { await runPermissionPage(tenantContext, opsBase, '/tenants', 'Ops', opsMarkers); }
  catch (error) { report.permissionLeaks.push(`Tenant-to-Ops isolation: permission probe failed: ${safeText(error.message)}`); }
  try { await runPermissionPage(opsContext, tenantBase, `/projects/${fixtures.projectId}`, 'Tenant', tenantMarkers); }
  catch (error) { report.permissionLeaks.push(`Ops-to-Tenant isolation: permission probe failed: ${safeText(error.message)}`); }
} catch (error) {
  report.setup.error = safeText(error.message);
  report.blockers.push(`setup/audit aborted: ${safeText(error.message)}`);
} finally {
  await cleanupTenant().catch(error => {
    report.cleanup = `failed: cleanup threw ${safeText(error.message)}`;
    report.blockers.push(report.cleanup);
  });
  await browser?.close().catch(() => undefined);
  buildSummary();
  await writeFile(reportPath, JSON.stringify(report, null, 2));
  console.log(JSON.stringify({
    success: report.success,
    reportPath,
    tenantRoutes: report.counts.tenantRoutes,
    opsRoutes: report.counts.opsRoutes,
    viewportChecks: report.counts.viewportChecks,
    permissionChecks: report.counts.permissionChecks,
    pageErrors: report.pageErrors.length,
    failedRequests: report.failedRequests.length,
    abortedRequests: report.abortedRequests.length,
    overflow: report.overflow.length,
    blankPages: report.blankPages.length,
    permissionLeaks: report.permissionLeaks.length,
    cleanup: report.cleanup
  }));
  process.exitCode = report.success ? 0 : 1;
}
