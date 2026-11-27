import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from './AuthProvider';
import { useAuth } from './useAuth';

const apiMocks = vi.hoisted(() => ({ me: vi.fn(), login: vi.fn(), logout: vi.fn() }));
vi.mock('../lib/api', () => ({
  OPS_UNAUTHORIZED_EVENT: 'regent:ops:unauthorized',
  OPS_PERMISSION_DENIED_EVENT: 'regent:ops:permission-denied',
  api: apiMocks
}));

const summary = {
  email: 'operator@example.test',
  display_name: 'Demo Operator',
  role: 'SRE',
  auth_method: 'Development',
  session_kind: 'Operator',
  capabilities: []
};

function Probe() {
  const { status, user, error, retry, login, logout } = useAuth();
  const location = useLocation();
  return <>
    <output data-testid="status">{status}</output>
    <output data-testid="user">{user?.display_name ?? 'none'}</output>
    <output data-testid="error">{error ?? 'none'}</output>
    <output data-testid="location">{location.pathname}{location.search}{location.hash}</output>
    <button onClick={() => void retry().catch(() => undefined)}>retry</button>
    <button onClick={() => void login().catch(() => undefined)}>login</button>
    <button onClick={() => void logout()}>logout</button>
  </>;
}

function renderProvider(initialEntry = '/', client = new QueryClient()) {
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[initialEntry]}><AuthProvider><Probe /></AuthProvider></MemoryRouter></QueryClientProvider>);
}

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.me.mockRejectedValue({ status: 401 });
  sessionStorage.clear();
  window.history.replaceState({}, '', '/');
});

afterEach(() => {
  sessionStorage.clear();
  window.history.replaceState({}, '', '/');
});

describe('Operations AuthProvider cookie lifecycle', () => {
  it('bootstraps from /ops/auth/me and restores on each mount', async () => {
    apiMocks.me.mockResolvedValue(summary);
    const first = renderProvider('/tenants');
    expect(screen.getByTestId('status')).toHaveTextContent('checking');
    await screen.findByText('authenticated');
    first.unmount();

    sessionStorage.setItem('regent.ops.user', 'Stale Operator');
    renderProvider('/tenants');
    await screen.findByText('authenticated');
    expect(apiMocks.me).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('user')).toHaveTextContent('Demo Operator');
    expect(sessionStorage.getItem('regent.ops.user')).toBeNull();
  });

  it('becomes anonymous on bootstrap 401 instead of trusting sessionStorage', async () => {
    sessionStorage.setItem('regent.ops.user', 'Stale Operator');
    renderProvider();
    expect(await screen.findByText('anonymous')).toBeInTheDocument();
    expect(screen.getByTestId('user')).toHaveTextContent('none');
  });

  it('retains permission-denied and retryable bootstrap error states', async () => {
    apiMocks.me.mockRejectedValueOnce({ status: 403 });
    renderProvider('/backups');
    expect(await screen.findByText('permission-denied')).toBeInTheDocument();
    expect(screen.getByTestId('error')).toHaveTextContent('Permission denied');
    expect(screen.getByTestId('location')).toHaveTextContent('/backups');

    apiMocks.me.mockRejectedValueOnce(new TypeError('offline'));
    fireEvent.click(screen.getByRole('button', { name: 'retry' }));
    expect(await screen.findByText('error')).toBeInTheDocument();
    apiMocks.me.mockResolvedValueOnce(summary);
    fireEvent.click(screen.getByRole('button', { name: 'retry' }));
    expect(await screen.findByText('authenticated')).toBeInTheDocument();
  });

  it('rejects reauthentication when the post-login session probe fails', async () => {
    apiMocks.login.mockResolvedValue({ display_name: 'Demo Operator' });
    apiMocks.me.mockRejectedValueOnce({ status: 401 }).mockRejectedValueOnce({ status: 503 });
    renderProvider('/login');
    await screen.findByText('anonymous');

    fireEvent.click(screen.getByRole('button', { name: 'login' }));

    expect(await screen.findByText('error')).toBeInTheDocument();
    expect(screen.getByTestId('user')).toHaveTextContent('none');
  });

  it('clears cache and preserves a safe return target on centralized 401', async () => {
    apiMocks.me.mockResolvedValue(summary);
    const client = new QueryClient();
    client.setQueryData(['ops-private'], 'stale');
    renderProvider('/backups?scope=all#jobs', client);
    await screen.findByText('authenticated');
    window.history.replaceState({}, '', '/backups?scope=all#jobs');

    act(() => window.dispatchEvent(new Event('regent:ops:unauthorized')));

    expect(screen.getByTestId('status')).toHaveTextContent('anonymous');
    expect(client.getQueryData(['ops-private'])).toBeUndefined();
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2Fbackups%3Fscope%3Dall%23jobs');
  });

  it('cleans up and routes to login when logout fails', async () => {
    apiMocks.me.mockResolvedValue(summary);
    apiMocks.logout.mockRejectedValue(new Error('offline'));
    const client = new QueryClient();
    client.setQueryData(['ops-private'], 'stale');
    renderProvider('/audit', client);
    await screen.findByText('authenticated');

    fireEvent.click(screen.getByRole('button', { name: 'logout' }));

    expect(await screen.findByText('anonymous')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/login'));
    expect(client.getQueryData(['ops-private'])).toBeUndefined();
  });
});
