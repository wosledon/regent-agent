import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AppThemeProvider } from '@regent/ui-shared';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { StatusPage } from './StatusPage';
import i18n from '../lib/i18n';

const apiMocks = vi.hoisted(() => ({
  status: vi.fn(), refreshStatus: vi.fn(), createIncident: vi.fn(), resolveIncident: vi.fn()
}));
vi.mock('../lib/api', () => ({ api: apiMocks }));

function renderPage(canWrite: boolean) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><AppThemeProvider><StatusPage operational canWrite={canWrite} /></AppThemeProvider></QueryClientProvider>);
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.status.mockResolvedValue({
    overallStatus: 'Operational', checkedAt: '2026-11-25T10:00:00Z', components: [],
    incidents: [{ id: 'incident-1', title: 'Degraded API', severity: 'SEV3', status: 'Open', message: 'Investigating', startedAt: '2026-11-25T09:00:00Z' }]
  });
});

describe('StatusPage', () => {
  it('marks a failed background refresh as stale instead of healthy', async () => {
    apiMocks.status.mockRejectedValue(new Error('offline'));
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    client.setQueryData(['platform-status'], {
      overallStatus: 'Operational', checkedAt: '2026-11-25T10:00:00Z', components: [], incidents: []
    });
    render(<QueryClientProvider client={client}><AppThemeProvider><StatusPage /></AppThemeProvider></QueryClientProvider>);

    expect(await screen.findByText('Stale / unknown')).toBeInTheDocument();
    expect(screen.getByText(/Last successful check:/)).toHaveTextContent('2026');
  });

  it('disables incident fields while publication is pending', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.createIncident.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(true);
    fireEvent.change(await screen.findByRole('textbox', { name: 'Incident title' }), { target: { value: 'Incident' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Incident message' }), { target: { value: 'Impact' } });
    fireEvent.click(screen.getByRole('button', { name: 'Publish incident' }));

    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Incident title' })).toBeDisabled());
    expect(screen.getByRole('textbox', { name: 'Incident message' })).toBeDisabled();
    await act(async () => complete({}));
  });

  it('hides platform write controls from a read-only operator', async () => {
    renderPage(false);
    expect(await screen.findByText('Read-only session: platform status writes are unavailable.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Refresh health' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Resolve' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Publish incident' })).not.toBeInTheDocument();
  });

  it('switches language while preserving domain data and localizing status, count and duration display', async () => {
    apiMocks.status.mockResolvedValue({
      overallStatus: 'Degraded', checkedAt: '2026-11-25T10:00:00Z',
      components: [{ name: 'Control Plane API', status: 'Operational', latencyMs: 250, checkedAt: '2026-11-25T10:00:00Z', message: 'Healthy' }],
      incidents: [{ id: 'incident-1', title: 'Degraded API', severity: 'SEV2', status: 'Open', message: 'Investigating', startedAt: '2026-11-25T09:00:00Z' }]
    });
    renderPage(false);

    expect(await screen.findByRole('heading', { name: 'Platform status' })).toBeInTheDocument();
    expect(screen.getByText('Control Plane API')).toBeInTheDocument();
    expect(screen.getByText('Degraded API')).toBeInTheDocument();
    expect(screen.getByText('Investigating')).toBeInTheDocument();
    expect(screen.getByText('Operational')).toBeInTheDocument();
    expect(screen.getByText('Severity 2')).toBeInTheDocument();
    expect(screen.getByText('Open')).toBeInTheDocument();
    expect(screen.getByText('250 ms')).toBeInTheDocument();
    expect(screen.getByText('1 component')).toBeInTheDocument();
    expect(screen.getByText('1 published incident')).toBeInTheDocument();
    expect(screen.getByText(/Last successful check:/)).toHaveTextContent('2026');

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByRole('heading', { level: 1, name: '平台状态' })).toBeInTheDocument();
    expect(screen.getByText('Control Plane API')).toBeInTheDocument();
    expect(screen.getByText('Degraded API')).toBeInTheDocument();
    expect(screen.getByText('Investigating')).toBeInTheDocument();
    expect(screen.getByText('正常')).toBeInTheDocument();
    expect(screen.getByText('严重级别 2')).toBeInTheDocument();
    expect(screen.getByText('处理中')).toBeInTheDocument();
    expect(screen.getByText('250毫秒')).toBeInTheDocument();
    expect(screen.getByText('1 个组件')).toBeInTheDocument();
    expect(screen.getByText('1 个已发布事件')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('renders a localized permission error without backend details', async () => {
    apiMocks.status.mockRejectedValue({ status: 403, message: 'private authorization detail' });
    renderPage(false);

    expect(await screen.findByText('Permission denied. You do not have access to this operation.')).toBeInTheDocument();
    expect(screen.queryByText(/private authorization detail/)).not.toBeInTheDocument();
  });
});
