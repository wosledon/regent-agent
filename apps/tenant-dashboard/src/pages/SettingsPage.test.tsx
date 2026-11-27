import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { field, findField, renderPage } from '../test/renderPage';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SettingsPage } from './SettingsPage';
import i18n from '../lib/i18n';

const authMocks = vi.hoisted(() => ({
  session: { tenantId: 'tenant-1', role: 'Owner', sessionKind: 'Standard' },
  switchTenant: vi.fn(), logout: vi.fn()
}));
const apiMocks = vi.hoisted(() => ({
  tenants: vi.fn(), sessions: vi.fn(), invitations: vi.fn(), ownershipDelegations: vi.fn(),
  displaySuffix: vi.fn(), localization: vi.fn(), updateDisplaySuffix: vi.fn(), updateLocalization: vi.fn(), revokeSession: vi.fn(),
  createInvitation: vi.fn(), createOwnershipDelegation: vi.fn()
}));
vi.mock('../app/useAuth', () => ({ useAuth: () => authMocks }));
vi.mock('../lib/api-client/api', () => ({ api: apiMocks }));
vi.mock('../features/identity/AuthenticationPolicyPanel', () => ({ AuthenticationPolicyPanel: () => null }));
vi.mock('../features/billing/DataExportPanel', () => ({ DataExportPanel: () => null }));

const ownerSession = { tenantId: 'tenant-1', role: 'Owner', sessionKind: 'Standard' };

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  authMocks.session = ownerSession;
  authMocks.switchTenant.mockResolvedValue(undefined);
  authMocks.logout.mockResolvedValue(undefined);
  apiMocks.tenants.mockResolvedValue([{ id: 'tenant-1', name: 'Tenant One', companyName: 'Company', status: 'Active' }]);
  apiMocks.sessions.mockResolvedValue([{ id: 'session-1', tenantId: 'tenant-1', expiresAt: '2026-11-25T12:00:00Z', status: 'Active' }]);
  apiMocks.invitations.mockResolvedValue([]);
  apiMocks.ownershipDelegations.mockResolvedValue([]);
  apiMocks.displaySuffix.mockResolvedValue({ suffix: '[Boss]' });
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
  apiMocks.updateDisplaySuffix.mockResolvedValue({ suffix: '' });
  apiMocks.updateLocalization.mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
  apiMocks.createInvitation.mockResolvedValue({});
  apiMocks.createOwnershipDelegation.mockResolvedValue({});
});

describe('SettingsPage identity sections', () => {
  it('does not enable owner queries for a member session', async () => {
    authMocks.session = { tenantId: 'tenant-1', role: 'Member', sessionKind: 'Standard' };
    renderPage(<SettingsPage />);

    expect(await screen.findByText('Tenant One')).toBeInTheDocument();
    expect(apiMocks.invitations).not.toHaveBeenCalled();
    expect(apiMocks.ownershipDelegations).not.toHaveBeenCalled();
    expect(screen.getAllByText('Permission denied. You do not have access to this resource.')).toHaveLength(2);
  });

  it('isolates owner query failures from preferences, tenant and session content', async () => {
    apiMocks.invitations.mockRejectedValue(new Error('Invitation query failed'));
    apiMocks.ownershipDelegations.mockRejectedValue(new Error('Delegation query failed'));
    renderPage(<SettingsPage />);

    expect(await screen.findByDisplayValue('[Boss]')).toBeInTheDocument();
    expect(screen.getByText('Tenant One')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Sessions' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Revoke' })).toBeInTheDocument();
    expect(screen.getByText('Invitation query failed')).toBeInTheDocument();
    expect(screen.getByText('Delegation query failed')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Retry' })).toHaveLength(2);
  });

  it('hydrates suffix, preserves dirty edits on refetch and saves an intentional clear once', async () => {
    const { client } = renderPage(<SettingsPage />);
    const input = await screen.findByDisplayValue('[Boss]');
    fireEvent.change(input, { target: { value: '[Edited]' } });
    apiMocks.displaySuffix.mockResolvedValue({ suffix: '[Server]' });
    await act(async () => { await client.refetchQueries({ queryKey: ['display-suffix'] }); });
    expect(input).toHaveValue('[Edited]');

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }));
    expect(input).toHaveValue('');
    const save = screen.getByRole('button', { name: 'Save display suffix' });
    expect(save).toBeEnabled();
    fireEvent.click(save);
    fireEvent.click(save);
    await waitFor(() => expect(apiMocks.updateDisplaySuffix).toHaveBeenCalledTimes(1));
    expect(apiMocks.updateDisplaySuffix).toHaveBeenCalledWith('');
    expect(await screen.findByText('Display suffix saved.')).toBeInTheDocument();
  });

  it('treats a reverted suffix as clean and accepts later server hydration', async () => {
    const { client } = renderPage(<SettingsPage />);
    const input = await screen.findByDisplayValue('[Boss]');
    const save = screen.getByRole('button', { name: 'Save display suffix' });
    expect(save).toBeDisabled();

    fireEvent.change(input, { target: { value: '[Edited]' } });
    expect(save).toBeEnabled();
    fireEvent.change(input, { target: { value: '[Boss]' } });
    expect(save).toBeDisabled();
    fireEvent.click(save);
    expect(apiMocks.updateDisplaySuffix).not.toHaveBeenCalled();

    apiMocks.displaySuffix.mockResolvedValue({ suffix: '[Server]' });
    await act(async () => { await client.refetchQueries({ queryKey: ['display-suffix'] }); });
    await waitFor(() => expect(input).toHaveValue('[Server]'));
    expect(save).toBeDisabled();
  });

  it('preserves an intentional clear after a failed save and permits a controlled retry', async () => {
    apiMocks.updateDisplaySuffix.mockRejectedValueOnce(new Error('Suffix conflict'));
    renderPage(<SettingsPage />);
    const input = await screen.findByDisplayValue('[Boss]');
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save display suffix' }));

    expect(await screen.findByText('Suffix conflict')).toBeInTheDocument();
    expect(input).toHaveValue('');
    const retry = screen.getByRole('button', { name: 'Save display suffix' });
    expect(retry).toBeEnabled();
    apiMocks.updateDisplaySuffix.mockResolvedValue({ suffix: '' });
    fireEvent.click(retry);
    await waitFor(() => expect(apiMocks.updateDisplaySuffix).toHaveBeenCalledTimes(2));
    expect(apiMocks.updateDisplaySuffix).toHaveBeenLastCalledWith('');
  });

  it('keeps owner queries disabled until a tenant context is available', async () => {
    authMocks.session = { tenantId: '', role: 'Owner', sessionKind: 'Standard' };
    renderPage(<SettingsPage />);

    await screen.findByDisplayValue('[Boss]');
    expect(apiMocks.invitations).not.toHaveBeenCalled();
    expect(apiMocks.ownershipDelegations).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Create invitation' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Delegate for 24 hours' })).toBeDisabled();
  });

  it('prevents duplicate preference, invitation and delegation writes while pending', async () => {
    let completeLocalization!: (value: unknown) => void;
    let completeInvitation!: (value: unknown) => void;
    let completeDelegation!: (value: unknown) => void;
    apiMocks.updateLocalization.mockReturnValue(new Promise(resolve => { completeLocalization = resolve; }));
    apiMocks.createInvitation.mockReturnValue(new Promise(resolve => { completeInvitation = resolve; }));
    apiMocks.createOwnershipDelegation.mockReturnValue(new Promise(resolve => { completeDelegation = resolve; }));
    renderPage(<SettingsPage />);
    await screen.findByDisplayValue('[Boss]');

    const savePreferences = screen.getByRole('button', { name: 'Save preferences' });
    fireEvent.click(savePreferences);
    await waitFor(() => expect(apiMocks.updateLocalization).toHaveBeenCalledTimes(1));
    expect(savePreferences).toBeDisabled();
    fireEvent.click(savePreferences);
    expect(apiMocks.updateLocalization).toHaveBeenCalledTimes(1);

    fireEvent.change(field('Email'), { target: { value: 'invitee@example.test' } });
    const invite = screen.getByRole('button', { name: 'Create invitation' });
    fireEvent.click(invite);
    await waitFor(() => expect(apiMocks.createInvitation).toHaveBeenCalledTimes(1));
    expect(invite).toBeDisabled();
    fireEvent.click(invite);
    expect(apiMocks.createInvitation).toHaveBeenCalledTimes(1);

    fireEvent.change(field('Delegate user ID'), { target: { value: 'delegate-1' } });
    const delegate = screen.getByRole('button', { name: 'Delegate for 24 hours' });
    fireEvent.click(delegate);
    await waitFor(() => expect(apiMocks.createOwnershipDelegation).toHaveBeenCalledTimes(1));
    expect(delegate).toBeDisabled();
    fireEvent.click(delegate);
    expect(apiMocks.createOwnershipDelegation).toHaveBeenCalledTimes(1);

    await act(async () => {
      completeLocalization({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
      completeInvitation({});
      completeDelegation({});
    });
  });

  it('keeps owner queries disabled during an emergency session', async () => {
    authMocks.session = { tenantId: 'tenant-1', role: 'Owner', sessionKind: 'Emergency' };
    renderPage(<SettingsPage />);

    expect(await screen.findByText('Restricted emergency session')).toBeInTheDocument();
    expect(apiMocks.invitations).not.toHaveBeenCalled();
    expect(apiMocks.ownershipDelegations).not.toHaveBeenCalled();
    expect(apiMocks.tenants).not.toHaveBeenCalled();
  });

  it('validates locale, IANA time zone and ISO currency before saving preferences', async () => {
    apiMocks.localization.mockResolvedValue({ locale: 'fr-FR', timeZone: 'Invalid/Zone', currency: 'US', version: 1 });
    renderPage(<SettingsPage />);

    expect(await screen.findByText('Select a supported display language.')).toBeInTheDocument();
    expect(screen.getByText('Time zone must be a valid IANA time zone.')).toBeInTheDocument();
    expect(screen.getByText('Currency must use a three-letter ISO currency code.')).toBeInTheDocument();
    const save = screen.getByRole('button', { name: 'Save preferences' });
    expect(save).toBeDisabled();
    fireEvent.click(save);
    expect(apiMocks.updateLocalization).not.toHaveBeenCalled();
  });

  it('normalizes whitespace-padded locale, time zone and currency before submission', async () => {
    apiMocks.localization.mockResolvedValue({ locale: ' en-us ', timeZone: ' Asia/Shanghai ', currency: ' usd ', version: 1 });
    const normalized = { locale: 'en-US', timeZone: 'Asia/Shanghai', currency: 'USD', version: 1 };
    apiMocks.updateLocalization.mockResolvedValue(normalized);
    renderPage(<SettingsPage />);

    await findField('IANA time zone');
    expect(field('IANA time zone')).toHaveValue(' Asia/Shanghai ');
    expect(screen.getByRole('button', { name: 'Save preferences' })).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: 'Save preferences' }));

    await waitFor(() => expect(apiMocks.updateLocalization).toHaveBeenCalledWith(normalized));
    expect(await screen.findByText('Preferences saved.')).toBeInTheDocument();
  });

  it('normalizes whitespace-padded time zone and currency drafts before submission', async () => {
    const normalized = { locale: 'en-US', timeZone: 'Asia/Shanghai', currency: 'USD', version: 1 };
    apiMocks.updateLocalization.mockResolvedValue(normalized);
    renderPage(<SettingsPage />);

    await findField('IANA time zone');
    fireEvent.change(field('IANA time zone'), { target: { value: ' Asia/Shanghai ' } });
    fireEvent.change(field('ISO currency'), { target: { value: ' usd ' } });
    const save = screen.getByRole('button', { name: 'Save preferences' });
    expect(save).toBeEnabled();
    fireEvent.click(save);

    await waitFor(() => expect(apiMocks.updateLocalization).toHaveBeenCalledWith(normalized));
    expect(await screen.findByText('Preferences saved.')).toBeInTheDocument();
  });

  it('switches page copy, document language and tenant time-zone formatting immediately', async () => {
    const { client } = renderPage(<SettingsPage />);
    await screen.findByRole('heading', { name: 'Sessions' });
    expect(screen.getByRole('heading', { name: 'Preferences' })).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('en-US');
    expect(await screen.findByText(/11\/25\/2026/)).toBeInTheDocument();
    expect(screen.getByText(/12:00:00/)).toBeInTheDocument();

    const chinese = { locale: 'zh-CN', timeZone: 'Asia/Shanghai', currency: 'CNY', version: 2 };
    apiMocks.localization.mockResolvedValue(chinese);
    apiMocks.updateLocalization.mockResolvedValue(chinese);
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Language' }));
    fireEvent.click(await screen.findByRole('option', { name: '简体中文' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save preferences' }));

    await waitFor(() => expect(document.documentElement.lang).toBe('zh-CN'));
    expect(screen.getByRole('heading', { name: '偏好设置' })).toBeInTheDocument();
    expect(screen.getByText(/2026\/11\/25/)).toBeInTheDocument();
    expect(screen.getByText(/20:00:00/)).toBeInTheDocument();
    expect(client.getQueryData(['localization'])).toEqual(chinese);
    expect(localStorage.getItem('regent.ui.locale')).toBe('zh-CN');
    await i18n.changeLanguage('en-US');
  });
});
