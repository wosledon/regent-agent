import { act, fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from './AuthProvider';
import { useAuth } from './useAuth';

const apiMocks = vi.hoisted(() => ({
  session: vi.fn(),
  devLogin: vi.fn(),
  passwordLogin: vi.fn(),
  register: vi.fn(),
  logout: vi.fn(),
  switchTenant: vi.fn(),
  useEmergencyAccess: vi.fn()
}));

vi.mock('../lib/api-client/api', () => ({
  TENANT_UNAUTHORIZED_EVENT: 'regent:tenant:unauthorized',
  TENANT_PERMISSION_DENIED_EVENT: 'regent:tenant:permission-denied',
  api: apiMocks
}));

const summary = {
  email: 'owner@example.test',
  display_name: 'Owner',
  tenant_id: '11111111-1111-1111-1111-111111111111',
  role: 'Owner',
  auth_method: 'Password',
  session_kind: 'Standard',
  capabilities: [],
  effective_access: { can_read_chat: true, can_read_collaboration: true, collaboration_scope: 'observer' }
};

function Probe() {
  const { status, session, error, retry, passwordLogin, logout, switchTenant } = useAuth();
  const location = useLocation();
  return <>
    <output data-testid="status">{status}</output>
    <output data-testid="session">{session?.displayName ?? 'none'}</output>
    <output data-testid="session-kind">{session?.sessionKind ?? 'none'}</output>
    <output data-testid="access">{session?.effectiveAccess.canReadChat === true ? 'chat' : 'no-chat'}:{session?.effectiveAccess.canReadCollaboration === true ? 'collaboration' : 'no-collaboration'}:{session?.effectiveAccess.collaborationScope ?? 'none'}</output>
    <output data-testid="error">{error ?? 'none'}</output>
    <output data-testid="location">{location.pathname}{location.search}{location.hash}</output>
    <button onClick={() => void retry().catch(() => undefined)}>retry</button>
    <button onClick={() => void passwordLogin('owner@example.test', 'password').catch(() => undefined)}>login</button>
    <button onClick={() => void switchTenant('11111111-1111-1111-1111-111111111111').catch(() => undefined)}>switch</button>
    <button onClick={() => void logout()}>logout</button>
  </>;
}

function renderProvider(initialEntry = '/', client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[initialEntry]}><AuthProvider><Probe /></AuthProvider></MemoryRouter></QueryClientProvider>);
}

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.session.mockRejectedValue({ status: 401 });
  sessionStorage.clear();
  window.history.replaceState({}, '', '/');
});

afterEach(() => {
  sessionStorage.clear();
  window.history.replaceState({}, '', '/');
});

describe('Tenant AuthProvider cookie lifecycle', () => {
  it('models checking then authenticated from the bootstrap endpoint', async () => {
    apiMocks.session.mockResolvedValue(summary);
    renderProvider('/projects/42');
    expect(screen.getByTestId('status')).toHaveTextContent('checking');
    expect(await screen.findByText('authenticated')).toBeInTheDocument();
    expect(screen.getByTestId('session')).toHaveTextContent('Owner');
    expect(apiMocks.session).toHaveBeenCalledOnce();
  });

  it('becomes anonymous on bootstrap 401 and rejects legacy sessionStorage truth', async () => {
    sessionStorage.setItem('regent.session.summary', JSON.stringify({ email: 'stale@example.test', displayName: 'Stale' }));
    renderProvider();
    expect(await screen.findByText('anonymous')).toBeInTheDocument();
    expect(screen.getByTestId('session')).toHaveTextContent('none');
    expect(sessionStorage.getItem('regent.session.summary')).toBeNull();
  });

  it('distinguishes permission denial and transport failures from anonymous sessions', async () => {
    apiMocks.session.mockRejectedValueOnce({ status: 403 });
    renderProvider('/projects');
    expect(await screen.findByText('permission-denied')).toBeInTheDocument();
    expect(screen.getByTestId('error')).toHaveTextContent('Permission denied');

    apiMocks.session.mockRejectedValueOnce(new TypeError('offline'));
    fireEvent.click(screen.getByRole('button', { name: 'retry' }));
    expect(await screen.findByText('error')).toBeInTheDocument();
    apiMocks.session.mockResolvedValueOnce(summary);
    fireEvent.click(screen.getByRole('button', { name: 'retry' }));
    expect(await screen.findByText('authenticated')).toBeInTheDocument();
  });

  it('rejects login when reauthentication fails with a server error', async () => {
    apiMocks.passwordLogin.mockResolvedValue({});
    apiMocks.session.mockRejectedValueOnce({ status: 401 }).mockRejectedValueOnce({ status: 503 });
    renderProvider('/login');
    await screen.findByText('anonymous');

    fireEvent.click(screen.getByRole('button', { name: 'login' }));

    expect(await screen.findByText('error')).toBeInTheDocument();
    expect(screen.getByTestId('session')).toHaveTextContent('none');
  });

  it('restores from a fresh bootstrap on reload or in a new tab', async () => {
    apiMocks.session.mockResolvedValue(summary);
    const first = renderProvider();
    await screen.findByText('authenticated');
    first.unmount();

    sessionStorage.setItem('regent.session.summary', JSON.stringify({ email: 'stale@example.test', displayName: 'Stale' }));
    renderProvider();
    await screen.findByText('authenticated');
    expect(apiMocks.session).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('session')).toHaveTextContent('Owner');
  });

  it('clears cache and ignores a late bootstrap response after 401', async () => {
    let resolveSession: (value: typeof summary) => void = () => undefined;
    apiMocks.session.mockReturnValue(new Promise(resolve => { resolveSession = resolve; }));
    const client = new QueryClient();
    client.setQueryData(['private'], 'stale');
    renderProvider('/projects/42?tab=delivery#evidence', client);
    window.history.replaceState({}, '', '/projects/42?tab=delivery#evidence');

    act(() => window.dispatchEvent(new Event('regent:tenant:unauthorized')));
    expect(screen.getByTestId('status')).toHaveTextContent('anonymous');
    expect(client.getQueryData(['private'])).toBeUndefined();
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2Fprojects%2F42%3Ftab%3Ddelivery%23evidence');

    await act(async () => resolveSession(summary));
    expect(screen.getByTestId('status')).toHaveTextContent('anonymous');
    expect(screen.getByTestId('session')).toHaveTextContent('none');
  });

  it('clears the authenticated write shell and cache on centralized 403', async () => {
    apiMocks.session.mockResolvedValue(summary);
    const client = new QueryClient();
    client.setQueryData(['private'], 'stale');
    renderProvider('/settings', client);
    await screen.findByText('authenticated');

    act(() => window.dispatchEvent(new Event('regent:tenant:permission-denied')));

    expect(screen.getByTestId('status')).toHaveTextContent('permission-denied');
    expect(screen.getByTestId('session')).toHaveTextContent('none');
    expect(screen.getByTestId('error')).toHaveTextContent('Permission denied');
    expect(screen.getByTestId('location')).toHaveTextContent('/settings');
    expect(client.getQueryData(['private'])).toBeUndefined();
  });

  it('fails closed for emergency sessions even when the server reports observer access', async () => {
    apiMocks.session.mockResolvedValue({
      ...summary,
      session_kind: 'Emergency',
      effective_access: { can_read_chat: true, can_read_collaboration: true, collaboration_scope: 'observer' }
    });
    renderProvider();

    await screen.findByText('authenticated');

    expect(screen.getByTestId('session-kind')).toHaveTextContent('Emergency');
    expect(screen.getByTestId('access')).toHaveTextContent('no-chat:no-collaboration:none');
  });

  it('cleans up and routes to login when logout transport fails', async () => {
    apiMocks.session.mockResolvedValue(summary);
    apiMocks.logout.mockRejectedValue(new Error('offline'));
    const client = new QueryClient();
    client.setQueryData(['private'], 'stale');
    renderProvider('/settings', client);
    await screen.findByText('authenticated');

    fireEvent.click(screen.getByRole('button', { name: 'logout' }));

    expect(await screen.findByText('anonymous')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/login');
    expect(client.getQueryData(['private'])).toBeUndefined();
  });

  it('fails closed when effective access is missing or malformed', async () => {
    apiMocks.session.mockResolvedValue({ ...summary, effective_access: undefined });
    renderProvider();
    await screen.findByText('authenticated');
    expect(screen.getByTestId('access')).toHaveTextContent('no-chat:no-collaboration:none');

    apiMocks.session.mockResolvedValue({
      ...summary,
      effective_access: { can_read_chat: 'yes', can_read_collaboration: true, collaboration_scope: 'observer' }
    });
    fireEvent.click(screen.getByRole('button', { name: 'retry' }));
    await screen.findByText('authenticated');
    expect(screen.getByTestId('access')).toHaveTextContent('no-chat:no-collaboration:none');
  });

  it('refreshes effective access and clears caches after login', async () => {
    apiMocks.session.mockRejectedValueOnce({ status: 401 }).mockResolvedValueOnce(summary);
    apiMocks.passwordLogin.mockResolvedValue({});
    const client = new QueryClient();
    client.setQueryData(['private'], 'stale');
    renderProvider('/login', client);
    await screen.findByText('anonymous');

    fireEvent.click(screen.getByRole('button', { name: 'login' }));

    expect(await screen.findByText('authenticated')).toBeInTheDocument();
    expect(apiMocks.session).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('access')).toHaveTextContent('chat:collaboration:observer');
    expect(client.getQueryData(['private'])).toBeUndefined();
  });

  it('refreshes effective access and clears caches after tenant switch', async () => {
    apiMocks.session.mockResolvedValueOnce(summary).mockResolvedValueOnce({
      ...summary,
      effective_access: { can_read_chat: false, can_read_collaboration: false, collaboration_scope: 'none' }
    });
    apiMocks.switchTenant.mockResolvedValue({});
    const client = new QueryClient();
    renderProvider('/', client);
    await screen.findByText('authenticated');
    client.setQueryData(['private'], 'stale');

    fireEvent.click(screen.getByRole('button', { name: 'switch' }));

    expect(await screen.findByText('no-chat:no-collaboration:none')).toBeInTheDocument();
    expect(apiMocks.session).toHaveBeenCalledTimes(2);
    expect(client.getQueryData(['private'])).toBeUndefined();
  });
});
