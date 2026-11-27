import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, findField, renderPage } from '../../test/renderPage';
import { AuthenticationPolicyPanel } from './AuthenticationPolicyPanel';

const apiMocks = vi.hoisted(() => ({
  authenticationPolicy: vi.fn(), configureIdentityProvider: vi.fn(), activateIdentityProvider: vi.fn(),
  updateGroupMapping: vi.fn(), refreshGroupMappings: vi.fn(), configureEmergencyAccess: vi.fn(), updateAuthenticationPolicy: vi.fn(), localization: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

const policy = {
  mode: 'optional', requireGroupClaim: false, allowedAcrValues: [], requiredAmrValues: [],
  maxAuthenticationAgeMinutes: 60, maxSessionMinutes: 480, idleTimeoutMinutes: 30, version: 2
};
const savedProvider = {
  tenantId: 'tenant-1', provider: 'Oidc', issuer: 'https://idp.example.test/tenant', clientId: 'client-1',
  clientSecretRef: 'keyvault://oidc/client', redirectUri: 'https://app.example.test/callback',
  groupMappingJson: '{"engineering":"dashboard.read"}', enabled: false, status: 'Draft', scopes: 'openid profile', groupClaim: 'groups', version: 5
};

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.authenticationPolicy.mockResolvedValue(policy);
  apiMocks.configureIdentityProvider.mockResolvedValue(savedProvider);
  apiMocks.activateIdentityProvider.mockResolvedValue({ ...savedProvider, enabled: true, status: 'Active', version: 6 });
  apiMocks.updateGroupMapping.mockResolvedValue({});
  apiMocks.refreshGroupMappings.mockResolvedValue({ evaluated: 1, updated: 1, denied: 0 });
  apiMocks.configureEmergencyAccess.mockResolvedValue({ credential: 'one-time-test-credential' });
  apiMocks.updateAuthenticationPolicy.mockResolvedValue(policy);
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
});

async function saveProviderDraft() {
  await findField('Issuer');
  fireEvent.change(field('Issuer'), { target: { value: savedProvider.issuer } });
  fireEvent.change(field('Client ID'), { target: { value: savedProvider.clientId } });
  fireEvent.change(field('Client secret reference'), { target: { value: savedProvider.clientSecretRef } });
  fireEvent.change(field('Redirect URI'), { target: { value: savedProvider.redirectUri } });
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Activate provider' })).toBeEnabled());
}

async function confirmActivation() {
  const dialog = await screen.findByRole('dialog');
  expect(within(dialog).getByText(/Oidc v5/)).toBeInTheDocument();
  fireEvent.change(within(dialog).getByRole('textbox', { name: 'Oidc' }), { target: { value: 'Oidc' } });
  fireEvent.click(within(dialog).getByRole('button', { name: 'Activate provider' }));
}

describe('AuthenticationPolicyPanel activation guard', () => {
  it('does not fetch tenant localization when the embedding session disables it', async () => {
    renderPage(<AuthenticationPolicyPanel tenantId="tenant-1" canManage localizationEnabled={false} />);
    await findField('Issuer');
    expect(apiMocks.localization).not.toHaveBeenCalled();
  });

  it('requires a saved provider and blocks activation while edits are unsaved', async () => {
    renderPage(<AuthenticationPolicyPanel tenantId="tenant-1" canManage />);
    const activate = await screen.findByRole('button', { name: 'Activate provider' });
    expect(activate).toBeDisabled();

    fireEvent.change(field('Issuer'), { target: { value: savedProvider.issuer } });
    fireEvent.change(field('Client ID'), { target: { value: savedProvider.clientId } });
    fireEvent.change(field('Client secret reference'), { target: { value: savedProvider.clientSecretRef } });
    fireEvent.change(field('Redirect URI'), { target: { value: savedProvider.redirectUri } });
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    expect(await screen.findByText('Saved provider: Oidc v5. Activation uses this saved version, not unsaved edits.')).toBeInTheDocument();
    await waitFor(() => expect(activate).toBeEnabled());

    fireEvent.change(field('Issuer'), { target: { value: 'https://unsaved.example.test' } });
    expect(activate).toBeDisabled();
    fireEvent.click(activate);
    expect(apiMocks.activateIdentityProvider).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(apiMocks.configureIdentityProvider).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(activate).toBeEnabled());
    fireEvent.click(activate);
    expect(apiMocks.activateIdentityProvider).not.toHaveBeenCalled();
    await confirmActivation();
    await waitFor(() => expect(apiMocks.activateIdentityProvider).toHaveBeenCalledWith('tenant-1', 'Oidc'));
  });

  it('disables provider writes while a configuration save is pending', async () => {
    let complete!: (value: typeof savedProvider) => void;
    apiMocks.configureIdentityProvider.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(<AuthenticationPolicyPanel tenantId="tenant-1" canManage />);
    await findField('Issuer');
    fireEvent.change(field('Issuer'), { target: { value: savedProvider.issuer } });
    fireEvent.change(field('Client ID'), { target: { value: savedProvider.clientId } });
    const save = screen.getByRole('button', { name: 'Save draft' });
    fireEvent.click(save);
    await waitFor(() => expect(apiMocks.configureIdentityProvider).toHaveBeenCalledTimes(1));
    expect(save).toBeDisabled();
    fireEvent.click(save);
    expect(apiMocks.configureIdentityProvider).toHaveBeenCalledTimes(1);

    await act(async () => complete(savedProvider));
    expect(await screen.findByText('Provider draft saved.')).toBeInTheDocument();
  });

  it('blocks activation for unsaved mapping edits until the mapping is saved', async () => {
    renderPage(<AuthenticationPolicyPanel tenantId="tenant-1" canManage />);
    await findField('Issuer');
    fireEvent.change(field('Issuer'), { target: { value: savedProvider.issuer } });
    fireEvent.change(field('Client ID'), { target: { value: savedProvider.clientId } });
    fireEvent.change(field('Client secret reference'), { target: { value: savedProvider.clientSecretRef } });
    fireEvent.change(field('Redirect URI'), { target: { value: savedProvider.redirectUri } });
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    const activate = screen.getByRole('button', { name: 'Activate provider' });
    await waitFor(() => expect(activate).toBeEnabled());

    fireEvent.change(field('Group to capability mapping (JSON)'), { target: { value: '{"engineering":"dashboard.read","audit":"audit.read"}' } });
    expect(activate).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Save mapping' }));
    expect(await screen.findByText('Group mapping saved.')).toBeInTheDocument();
    await waitFor(() => expect(activate).toBeEnabled());
  });

  it('tracks authentication-policy dirty state and renders save feedback', async () => {
    renderPage(<AuthenticationPolicyPanel tenantId="tenant-1" canManage />);
    const save = await screen.findByRole('button', { name: 'Save authentication policy' });
    expect(save).toBeDisabled();
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Authentication mode' }));
    fireEvent.click(await screen.findByRole('option', { name: 'Required OIDC SSO' }));
    expect(save).toBeEnabled();
    fireEvent.click(save);
    expect(await screen.findByText('Authentication policy saved.')).toBeInTheDocument();
  });

  it('prevents duplicate authentication-policy saves while pending', async () => {
    let complete!: (value: typeof policy) => void;
    apiMocks.updateAuthenticationPolicy.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(<AuthenticationPolicyPanel tenantId="tenant-1" canManage />);
    const save = await screen.findByRole('button', { name: 'Save authentication policy' });
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Authentication mode' }));
    fireEvent.click(await screen.findByRole('option', { name: 'Required OIDC SSO' }));
    fireEvent.click(save);
    await waitFor(() => expect(apiMocks.updateAuthenticationPolicy).toHaveBeenCalledTimes(1));
    expect(save).toBeDisabled();
    fireEvent.click(save);
    expect(apiMocks.updateAuthenticationPolicy).toHaveBeenCalledTimes(1);
    await act(async () => complete({ ...policy, mode: 'required' }));
  });

  it('prevents duplicate provider activation while pending', async () => {
    let complete!: (value: typeof savedProvider) => void;
    apiMocks.activateIdentityProvider.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(<AuthenticationPolicyPanel tenantId="tenant-1" canManage />);
    await saveProviderDraft();

    const activate = screen.getByRole('button', { name: 'Activate provider' });
    fireEvent.click(activate);
    await confirmActivation();
    await waitFor(() => expect(apiMocks.activateIdentityProvider).toHaveBeenCalledTimes(1));
    expect(activate).toBeDisabled();
    fireEvent.click(activate);
    expect(apiMocks.activateIdentityProvider).toHaveBeenCalledTimes(1);

    await act(async () => complete({ ...savedProvider, enabled: true, status: 'Active', version: 6 }));
    expect(await screen.findByText('Saved provider activated.')).toBeInTheDocument();
  });

  it('renders activation errors without changing the saved provider', async () => {
    apiMocks.activateIdentityProvider.mockRejectedValue(new Error('Activation conflict'));
    renderPage(<AuthenticationPolicyPanel tenantId="tenant-1" canManage />);
    await saveProviderDraft();

    const activate = screen.getByRole('button', { name: 'Activate provider' });
    fireEvent.click(activate);
    await confirmActivation();
    expect(await screen.findByText('Activation conflict')).toBeInTheDocument();
    expect(screen.getByText('Saved provider: Oidc v5. Activation uses this saved version, not unsaved edits.')).toBeInTheDocument();
    expect(activate).toBeEnabled();
  });

  it('renders provider mutation errors', async () => {
    apiMocks.configureIdentityProvider.mockRejectedValue(new Error('Provider conflict'));
    renderPage(<AuthenticationPolicyPanel tenantId="tenant-1" canManage />);
    await findField('Issuer');
    fireEvent.change(field('Issuer'), { target: { value: savedProvider.issuer } });
    fireEvent.change(field('Client ID'), { target: { value: savedProvider.clientId } });
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    expect(await screen.findByText('Provider conflict')).toBeInTheDocument();
  });
});
