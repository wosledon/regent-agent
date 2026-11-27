import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { AppThemeProvider } from '@regent/ui-shared';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TenantsPage } from './TenantsPage';
import i18n from '../lib/i18n';

const apiMocks = vi.hoisted(() => ({ tenants: vi.fn(), setTenantStatus: vi.fn() }));
vi.mock('../lib/api', () => ({ api: apiMocks }));

const tenant = { id: 'tenant-a', name: 'Alpha', status: 'Active', employees: 2, runnerCount: 1 };

function renderPage(canWrite = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><MemoryRouter><AppThemeProvider><TenantsPage canWrite={canWrite} /></AppThemeProvider></MemoryRouter></QueryClientProvider>);
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.tenants.mockResolvedValue([tenant]);
  apiMocks.setTenantStatus.mockResolvedValue({ ...tenant, status: 'Suspended' });
});

afterEach(() => vi.restoreAllMocks());

describe('TenantsPage', () => {
  it('requires explicit confirmation before suspending a tenant', async () => {
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Suspend' }));
    expect(apiMocks.setTenantStatus).not.toHaveBeenCalled();

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(/Alpha/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Suspend' }));
    await waitFor(() => expect(apiMocks.setTenantStatus).toHaveBeenCalledWith('tenant-a', 'Suspended'));
  });

  it('prevents duplicate suspension submissions while the mutation is pending', async () => {
    let complete!: (value: typeof tenant) => void;
    apiMocks.setTenantStatus.mockReturnValue(new Promise<typeof tenant>(resolve => { complete = resolve; }));
    renderPage();
    const suspend = await screen.findByRole('button', { name: 'Suspend' });
    fireEvent.click(suspend);
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Suspend' }));
    await waitFor(() => expect(suspend).toBeDisabled());
    fireEvent.click(suspend);
    expect(apiMocks.setTenantStatus).toHaveBeenCalledOnce();

    await act(async () => complete({ ...tenant, status: 'Suspended' }));
  });

  it('requires confirmation before activating a suspended tenant', async () => {
    apiMocks.tenants.mockResolvedValue([{ ...tenant, status: 'Suspended' }]);
    apiMocks.setTenantStatus.mockResolvedValue({ ...tenant, status: 'Active' });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Activate' }));
    expect(apiMocks.setTenantStatus).not.toHaveBeenCalled();

    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Activate' }));
    await waitFor(() => expect(apiMocks.setTenantStatus).toHaveBeenCalledWith('tenant-a', 'Active'));
  });

  it('filters the tenant table by search and status', async () => {
    apiMocks.tenants.mockResolvedValue([
      tenant,
      { id: 'tenant-b', name: 'Beta', status: 'Suspended', employees: 4, runnerCount: 2 }
    ]);
    renderPage(false);
    expect(await screen.findByText('Beta')).toBeInTheDocument();

    fireEvent.change(screen.getByRole('textbox', { name: 'Tenant' }), { target: { value: 'beta' } });
    expect(screen.queryByText('Alpha')).not.toBeInTheDocument();
    expect(screen.getByText('Beta')).toBeInTheDocument();

    fireEvent.change(screen.getByRole('textbox', { name: 'Tenant' }), { target: { value: '' } });
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Status' }));
    fireEvent.click(await screen.findByRole('option', { name: 'Suspended' }));
    expect(screen.queryByText('Alpha')).not.toBeInTheDocument();
    expect(screen.getByText('Beta')).toBeInTheDocument();
  });

  it('renders permission-denied guidance for a 403 response', async () => {
    apiMocks.tenants.mockRejectedValue({ status: 403, message: 'Request failed: 403' });
    renderPage();

    expect(await screen.findByText('Permission denied. You do not have access to this operation.')).toBeInTheDocument();
  });

  it('does not render tenant write controls for a read-only session', async () => {
    renderPage(false);
    expect(await screen.findByText('Read-only session: tenant status changes are unavailable.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Suspend' })).not.toBeInTheDocument();
  });

  it('switches mounted business copy and document language without translating tenant data', async () => {
    renderPage(false);
    expect(await screen.findByRole('heading', { name: 'Tenants' })).toBeInTheDocument();
    expect(screen.getByText('Alpha')).toBeInTheDocument();
    expect(within(screen.getByRole('table')).getByText('Active')).toBeInTheDocument();
    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByRole('heading', { level: 1, name: '租户账户' })).toBeInTheDocument();
    expect(screen.getByText('只读会话：无法修改租户状态。')).toBeInTheDocument();
    expect(screen.getByText('Alpha')).toBeInTheDocument();
    expect(within(screen.getByRole('table')).getByText('活跃')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('localizes confirmation copy while preserving the raw status payload', async () => {
    renderPage();
    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    fireEvent.click(await screen.findByRole('button', { name: '暂停' }));

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(/Alpha/)).toBeInTheDocument();
    expect(within(dialog).getByText(/暂停 Alpha/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: '暂停' }));
    await waitFor(() => expect(apiMocks.setTenantStatus).toHaveBeenCalledWith('tenant-a', 'Suspended'));
  });
});
