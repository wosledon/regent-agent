import { act, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AppThemeProvider } from '@regent/ui-shared';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuditPage } from './AuditPage';
import i18n from '../lib/i18n';

const apiMocks = vi.hoisted(() => ({ audit: vi.fn() }));
vi.mock('../lib/api', () => ({ api: apiMocks }));

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><AppThemeProvider><AuditPage /></AppThemeProvider></QueryClientProvider>);
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
  apiMocks.audit.mockReset();
  apiMocks.audit.mockResolvedValue([{
    id: 'audit-1', operatorId: 'operator-42', action: 'Tenant.Suspended', target: 'tenant-alpha',
    createdAt: '2026-11-25T10:00:00Z'
  }]);
});

describe('AuditPage', () => {
  it('switches language without translating operator, action or target data', async () => {
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Operations Audit' })).toBeInTheDocument();
    expect(await screen.findByText('operator-42')).toBeInTheDocument();
    expect(screen.getByText('Tenant.Suspended')).toBeInTheDocument();
    expect(screen.getByText('tenant-alpha')).toBeInTheDocument();
    expect(screen.getByText(/2026/)).toBeInTheDocument();

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByRole('heading', { level: 1, name: '运营审计' })).toBeInTheDocument();
    expect(screen.getByText('operator-42')).toBeInTheDocument();
    expect(screen.getByText('Tenant.Suspended')).toBeInTheDocument();
    expect(screen.getByText('tenant-alpha')).toBeInTheDocument();
    expect(screen.getByText(/2026/)).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('renders safe localized API errors without backend details', async () => {
    apiMocks.audit.mockRejectedValue({ status: 500, message: 'private audit backend detail' });
    renderPage();

    expect(await screen.findByText('The service is temporarily unavailable. Please try again later.')).toBeInTheDocument();
    expect(screen.queryByText(/private audit backend detail/)).not.toBeInTheDocument();
  });
});
