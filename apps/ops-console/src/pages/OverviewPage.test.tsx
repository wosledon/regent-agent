import { act, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { AppThemeProvider } from '@regent/ui-shared';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { OverviewPage } from './OverviewPage';
import i18n from '../lib/i18n';

const apiMocks = vi.hoisted(() => ({
  tenants: vi.fn(), grants: vi.fn(), status: vi.fn(), alerts: vi.fn(), backups: vi.fn(), backupCoverage: vi.fn()
}));
vi.mock('../lib/api', () => ({ api: apiMocks }));

const tenants = [
  { id: 'tenant-a', name: 'Alpha', status: 'Active', employees: 2, runnerCount: 1 },
  { id: 'tenant-b', name: 'Beta', status: 'Suspended', employees: 4, runnerCount: 2 }
];
const grants = [
  { id: 'grant-1', tenantId: 'tenant-a', operatorId: 'operator-1', scope: 'read-only', expiresAt: '2026-11-25T11:00:00Z', status: 'Active' },
  { id: 'grant-2', tenantId: 'tenant-a', operatorId: 'operator-1', scope: 'diagnostics', expiresAt: '2026-11-25T11:00:00Z', status: 'Pending' },
  { id: 'grant-3', tenantId: 'tenant-b', operatorId: 'operator-2', scope: 'read-only', expiresAt: '2026-11-25T12:00:00Z', status: 'Pending' }
];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><MemoryRouter><AppThemeProvider><OverviewPage /></AppThemeProvider></MemoryRouter></QueryClientProvider>);
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.tenants.mockResolvedValue(tenants);
  apiMocks.grants.mockResolvedValue(grants);
  apiMocks.status.mockResolvedValue({
    overallStatus: 'Operational', checkedAt: '2026-11-25T10:00:00Z',
    components: [{ name: 'api', status: 'Operational', latencyMs: 12, checkedAt: '2026-11-25T10:00:00Z', message: '' }],
    incidents: []
  });
  apiMocks.alerts.mockResolvedValue([]);
  apiMocks.backups.mockResolvedValue([{ id: 'default', deploymentId: 'local', enabled: true, components: 'postgres', retentionDays: 7 }]);
  apiMocks.backupCoverage.mockResolvedValue({
    profileId: 'default', enabled: true, status: 'covered', enabledComponents: ['postgres'],
    lastSuccessfulAt: '2026-11-25T09:00:00Z', effectiveRpoMinutes: 60, effectiveRtoMinutes: 120,
    rpoStatus: 'measured', rtoStatus: 'measured'
  });
});

describe('OverviewPage', () => {
  it('switches language and plural counts without changing domain data', async () => {
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Operations Overview' })).toBeInTheDocument();
    expect(await screen.findByText('2 tenants')).toBeInTheDocument();
    expect(screen.getByText('1 active support grant')).toBeInTheDocument();
    expect(screen.getByText('2 pending approvals')).toBeInTheDocument();
    expect(screen.getByText('SQLite · local files · no containers')).toBeInTheDocument();

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByRole('heading', { level: 1, name: '运营总览' })).toBeInTheDocument();
    expect(screen.getByText('2 个租户')).toBeInTheDocument();
    expect(screen.getByText('1 个活跃支持授权')).toBeInTheDocument();
    expect(screen.getByText('2 个待审批')).toBeInTheDocument();
    expect(screen.getByText('SQLite · 本地文件 · 无容器')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('keeps tenant and approval cards available when health is unavailable', async () => {
    apiMocks.status.mockRejectedValue({ status: 503, message: 'private health detail' });
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Operations Overview' })).toBeInTheDocument();
    expect(await screen.findByText('2 tenants')).toBeInTheDocument();
    expect(screen.getByText('2 pending approvals')).toBeInTheDocument();
    expect(screen.getByText('The service is temporarily unavailable. Please try again later.')).toBeInTheDocument();
    expect(screen.queryByText(/private health detail/)).not.toBeInTheDocument();
  });

  it('renders a safe permission error instead of conflicting backend details', async () => {
    apiMocks.tenants.mockRejectedValue({ status: 403, message: 'private timeout detail' });
    renderPage();

    expect(await screen.findByText('Permission denied. You do not have access to this operation.')).toBeInTheDocument();
    expect(screen.queryByText(/private timeout detail/)).not.toBeInTheDocument();
    expect(screen.queryByText('The request timed out. Please try again.')).not.toBeInTheDocument();
  });
});
