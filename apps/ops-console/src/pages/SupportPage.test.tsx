import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { AppThemeProvider } from '@regent/ui-shared';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SupportPage } from './SupportPage';
import i18n from '../lib/i18n';
import { createOpsFormatter } from '../lib/formatting';

const apiMocks = vi.hoisted(() => ({
  grants: vi.fn(), tenants: vi.fn(), createGrant: vi.fn(), approveGrant: vi.fn(), revokeGrant: vi.fn()
}));
vi.mock('../lib/api', () => ({ api: apiMocks }));

const tenants = [
  { id: 'tenant-a', name: 'Alpha', status: 'Active', employees: 2, runnerCount: 1 },
  { id: 'tenant-b', name: 'Beta', status: 'Active', employees: 4, runnerCount: 2 }
];
const activeGrant = {
  id: 'grant-1', tenantId: 'tenant-b', operatorId: 'operator-1', scope: 'read-only',
  expiresAt: '2026-11-25T11:00:00Z', status: 'Active', approvalRef: 'tenant-approval-1'
};
const diagnosticsGrant = {
  id: 'grant-2', tenantId: 'tenant-a', operatorId: 'operator-2', scope: 'diagnostics',
  expiresAt: '2026-11-25T12:00:00Z', status: 'Pending', approvalRef: 'tenant-approval-2'
};

function renderPage(canWrite = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><MemoryRouter><AppThemeProvider><SupportPage canWrite={canWrite} /></AppThemeProvider></MemoryRouter></QueryClientProvider>);
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.tenants.mockResolvedValue(tenants);
  apiMocks.grants.mockResolvedValue([activeGrant]);
  apiMocks.createGrant.mockResolvedValue({ ...activeGrant, id: 'grant-2', status: 'Pending' });
  apiMocks.approveGrant.mockResolvedValue(activeGrant);
  apiMocks.revokeGrant.mockResolvedValue(undefined);
});

afterEach(() => vi.restoreAllMocks());

describe('SupportPage', () => {
  it('requires an explicit loaded tenant selection before requesting a grant', async () => {
    renderPage();
    const request = await screen.findByRole('button', { name: 'Request 30-minute read-only grant' });
    expect(request).toBeDisabled();
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Tenant' }));
    fireEvent.click(await screen.findByRole('option', { name: 'Beta' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Tenant approval reference' }), { target: { value: 'tenant-approval-1' } });
    fireEvent.click(request);

    await waitFor(() => expect(apiMocks.createGrant).toHaveBeenCalledWith('tenant-b', 'tenant-approval-1'));
    expect(await screen.findByText('Support grant requested.')).toBeInTheDocument();
    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByText('支持授权已申请。')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('disables the selected tenant while a grant request is pending', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.createGrant.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage();
    fireEvent.mouseDown(await screen.findByRole('combobox', { name: 'Tenant' }));
    fireEvent.click(await screen.findByRole('option', { name: 'Beta' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Tenant approval reference' }), { target: { value: 'approval-1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Request 30-minute read-only grant' }));

    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Tenant' })).toHaveAttribute('aria-disabled', 'true'));
    expect(screen.getByRole('textbox', { name: 'Tenant approval reference' })).toBeDisabled();
    await act(async () => complete({}));
  });

  it('requires confirmation before approving tenant access', async () => {
    apiMocks.grants.mockResolvedValue([{ ...activeGrant, status: 'Pending' }]);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Approve' }));
    expect(apiMocks.approveGrant).not.toHaveBeenCalled();

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(/Beta/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Read-only/)).toBeInTheDocument();
    expect(within(dialog).getByText(/tenant-approval-1/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Approve' }));
    await waitFor(() => expect(apiMocks.approveGrant).toHaveBeenCalledWith('grant-1'));
  });

  it('requires confirmation before revoking an active grant', async () => {
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Revoke' }));
    expect(apiMocks.revokeGrant).not.toHaveBeenCalled();

    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Revoke' }));
    await waitFor(() => expect(apiMocks.revokeGrant).toHaveBeenCalledWith('grant-1'));
  });

  it('switches language while preserving tenant data and localizing scopes, counts and dates', async () => {
    apiMocks.grants.mockResolvedValue([activeGrant, diagnosticsGrant]);
    renderPage(false);

    expect(await screen.findByRole('heading', { name: 'Support Grants' })).toBeInTheDocument();
    expect(await screen.findByText('2 access requests')).toBeInTheDocument();
    expect(screen.getByText('Read-only')).toBeInTheDocument();
    expect(screen.getByText('Diagnostics')).toBeInTheDocument();
    expect(screen.getByText('Pending')).toBeInTheDocument();
    expect(screen.getByText(createOpsFormatter('en-US').dateTime(activeGrant.expiresAt))).toBeInTheDocument();
    expect(screen.getByText(createOpsFormatter('en-US').dateTime(diagnosticsGrant.expiresAt))).toBeInTheDocument();
    expect(screen.getByText('Alpha')).toBeInTheDocument();
    expect(screen.getByText('Beta')).toBeInTheDocument();

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByRole('heading', { level: 1, name: '支持授权' })).toBeInTheDocument();
    expect(screen.getByText('2 个访问请求')).toBeInTheDocument();
    expect(screen.getByText('只读')).toBeInTheDocument();
    expect(screen.getByText('诊断')).toBeInTheDocument();
    expect(screen.getByText('待处理')).toBeInTheDocument();
    expect(screen.getByText(createOpsFormatter('zh-CN').dateTime(activeGrant.expiresAt))).toBeInTheDocument();
    expect(screen.getByText(createOpsFormatter('zh-CN').dateTime(diagnosticsGrant.expiresAt))).toBeInTheDocument();
    expect(screen.getByText('Alpha')).toBeInTheDocument();
    expect(screen.getByText('Beta')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('does not render grant write controls for a read-only session', async () => {
    renderPage(false);
    expect(await screen.findByText('Read-only session: support grant writes are unavailable.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Request 30-minute read-only grant' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Revoke' })).not.toBeInTheDocument();
  });
});
