import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AppThemeProvider } from '@regent/ui-shared';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ObservabilityPage } from './ObservabilityPage';
import i18n from '../lib/i18n';

const apiMocks = vi.hoisted(() => ({
  observability: vi.fn(), elasticsearch: vi.fn(), evaluateSlos: vi.fn(), alerts: vi.fn(),
  acknowledgeAlert: vi.fn(), resolveAlert: vi.fn(), silenceAlert: vi.fn(), saveObservability: vi.fn(), saveSlo: vi.fn()
}));
vi.mock('../lib/api', () => ({ api: apiMocks }));

function renderPage(canWrite: boolean) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><AppThemeProvider><ObservabilityPage canWrite={canWrite} /></AppThemeProvider></QueryClientProvider>);
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.observability.mockResolvedValue({ mode: 'local', serviceName: 'regent-agent', elasticEnabled: false, metricIntervalSeconds: 15 });
  apiMocks.elasticsearch.mockResolvedValue({ configured: false, reachable: false, indices: [], checkedAt: '2026-11-25T10:00:00Z' });
  apiMocks.evaluateSlos.mockResolvedValue([]);
  apiMocks.alerts.mockResolvedValue([{
    id: 'alert-1', tenantId: 'local', serviceName: 'regent-agent', sloName: 'api_availability', fingerprint: 'fp-1',
    level: 'L2', status: 'Firing', message: 'Error budget burning', runbook: '', owner: 'SRE',
    createdAt: '2026-11-25T10:00:00Z', updatedAt: '2026-11-25T10:00:00Z'
  }]);
});

describe('ObservabilityPage', () => {
  it('disables profile fields while a save is pending', async () => {
    let complete!: (value: { mode: string; serviceName: string; elasticEnabled: boolean; metricIntervalSeconds: number }) => void;
    apiMocks.saveObservability.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(true);
    const serviceName = await screen.findByDisplayValue('regent-agent');
    fireEvent.change(serviceName, { target: { value: 'edited-service' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save profile' }));

    await waitFor(() => expect(serviceName).toBeDisabled());
    expect(screen.getByRole('combobox', { name: 'Mode' })).toHaveAttribute('aria-disabled', 'true');
    await act(async () => complete({ mode: 'local', serviceName: 'edited-service', elasticEnabled: false, metricIntervalSeconds: 15 }));
  });

  it('renders safe SLO query errors instead of an empty evaluation state', async () => {
    apiMocks.evaluateSlos.mockRejectedValue(new Error('SLO evaluations unavailable: private detail'));
    renderPage(true);

    expect(await screen.findByText(i18n.t('common.errors.unexpected'))).toBeInTheDocument();
    expect(screen.queryByText(/SLO evaluations unavailable/)).not.toBeInTheDocument();
    expect(screen.queryByText('No SLO evaluations')).not.toBeInTheDocument();
  });

  it('hides observability write controls from a read-only operator', async () => {
    renderPage(false);
    expect(await screen.findByText('Read-only session: observability writes are unavailable.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save profile' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save SLO' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Acknowledge' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Resolve' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve 60 minute silence' })).not.toBeInTheDocument();
  });

  it('sanitizes raw Elasticsearch error details and localizes unreachability', async () => {
    apiMocks.elasticsearch.mockResolvedValue({
      configured: true, reachable: false, status: 'red', indices: [], checkedAt: '2026-11-25T10:00:00Z', error: 'private cluster detail'
    });
    renderPage(false);

    expect(await screen.findByText('Unreachable')).toBeInTheDocument();
    expect(screen.getByText('Details unavailable')).toBeInTheDocument();
    expect(screen.queryByText(/private cluster detail/)).not.toBeInTheDocument();
    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByText('无法连接')).toBeInTheDocument();
    expect(screen.getByText('详情不可用')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('switches language while preserving domain data and localizing formatted values', async () => {
    apiMocks.observability.mockResolvedValue({ mode: 'otlp', serviceName: 'regent-agent', elasticEnabled: true, metricIntervalSeconds: 15 });
    apiMocks.elasticsearch.mockResolvedValue({
      configured: true, reachable: true, status: 'yellow',
      indices: [{ name: 'regent-audit-2026.11.25', documents: 1234, storeSize: '1.5gb' }],
      checkedAt: '2026-11-25T10:00:00Z'
    });
    apiMocks.evaluateSlos.mockResolvedValue([
      { name: 'api_availability', actual: 0.999, target: 0.99, passed: true, errorBudgetRemaining: 0.5, level: 'L1' }
    ]);
    renderPage(false);

    expect(await screen.findByRole('heading', { name: 'Observability' })).toBeInTheDocument();
    expect(await screen.findByText('Mode: OTLP')).toBeInTheDocument();
    expect(screen.getByText('Service: regent-agent')).toBeInTheDocument();
    expect(screen.getByText('Metric interval: 15 sec')).toBeInTheDocument();
    expect(await screen.findByText('Reachable')).toBeInTheDocument();
    expect(screen.getByText('Yellow')).toBeInTheDocument();
    expect(screen.getByText('regent-audit-2026.11.25')).toBeInTheDocument();
    expect(screen.getByText('1,234')).toBeInTheDocument();
    expect(await screen.findByText('50%')).toBeInTheDocument();
    expect(screen.getByText('99.9%')).toBeInTheDocument();
    expect(screen.getByText('99%')).toBeInTheDocument();
    expect(await screen.findByText('Error budget burning')).toBeInTheDocument();
    expect(screen.getByText('Level 2')).toBeInTheDocument();
    expect(screen.getByText('Firing')).toBeInTheDocument();
    expect(screen.getByText('1 active alert')).toBeInTheDocument();

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByRole('heading', { level: 1, name: '可观测性' })).toBeInTheDocument();
    expect(screen.getByText('模式：OTLP')).toBeInTheDocument();
    expect(screen.getByText('服务：regent-agent')).toBeInTheDocument();
    expect(screen.getByText('指标间隔：15秒')).toBeInTheDocument();
    expect(screen.getByText('可连接')).toBeInTheDocument();
    expect(screen.getByText('黄色')).toBeInTheDocument();
    expect(screen.getByText('regent-audit-2026.11.25')).toBeInTheDocument();
    expect(screen.getByText('1,234')).toBeInTheDocument();
    expect(screen.getByText('99.9%')).toBeInTheDocument();
    expect(screen.getByText('99%')).toBeInTheDocument();
    expect(screen.getByText('50%')).toBeInTheDocument();
    expect(screen.getByText('Error budget burning')).toBeInTheDocument();
    expect(screen.getByText('级别 2')).toBeInTheDocument();
    expect(screen.getByText('触发中')).toBeInTheDocument();
    expect(screen.getByText('1 个活跃告警')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('keeps raw domain values in mutation payloads while displaying localized labels', async () => {
    apiMocks.saveObservability.mockResolvedValue({ mode: 'elastic', serviceName: 'regent-agent', elasticEnabled: true, metricIntervalSeconds: 15 });
    renderPage(true);
    fireEvent.mouseDown(await screen.findByRole('combobox', { name: 'Mode' }));
    fireEvent.click(await screen.findByRole('option', { name: 'Elasticsearch' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save profile' }));

    await waitFor(() => expect(apiMocks.saveObservability).toHaveBeenCalledWith({
      mode: 'elastic', serviceName: 'regent-agent', elasticEnabled: false, metricIntervalSeconds: 15
    }));
  });
});
