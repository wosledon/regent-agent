import { act, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppThemeProvider } from '@regent/ui-shared';
import { App } from './App';
import i18n from '../lib/i18n';

const apiMocks = vi.hoisted(() => ({ session: vi.fn() }));
vi.mock('../lib/api-client/api', () => ({
  TENANT_UNAUTHORIZED_EVENT: 'regent:tenant:unauthorized',
  TENANT_PERMISSION_DENIED_EVENT: 'regent:tenant:permission-denied',
  api: apiMocks
}));
vi.mock('../hooks/useTenantHub', () => ({ useTenantHub: () => 'connected' }));

const session = {
  email: 'owner@example.test', display_name: 'Owner', tenant_id: '11111111-1111-1111-1111-111111111111',
  role: 'Owner', auth_method: 'Password', session_kind: 'Standard', capabilities: [],
  effective_access: { can_read_chat: true, can_read_collaboration: true, collaboration_scope: 'observer' }
};

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname}{location.search}{location.hash}</output>;
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
  apiMocks.session.mockReset();
  apiMocks.session.mockRejectedValue({ status: 401 });
});

afterEach(() => vi.unstubAllEnvs());

describe('Tenant route guard', () => {
  it('waits for bootstrap with localized loading feedback', async () => {
    apiMocks.session.mockReturnValue(new Promise<void>(() => undefined));
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/projects/42']}><AppThemeProvider><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);

    expect(screen.getByRole('progressbar', { name: 'Checking your session…' })).toBeInTheDocument();
    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByRole('progressbar', { name: '正在检查您的会话…' })).toBeInTheDocument();
  });

  it('waits for bootstrap and preserves a safe deep-link return target', async () => {
    let finishBootstrap: (error: unknown) => void = () => undefined;
    apiMocks.session.mockReturnValue(new Promise((_, reject) => { finishBootstrap = reject; }));
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/projects/42?tab=delivery#evidence']}><AppThemeProvider><LocationProbe /><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);

    expect(screen.getByTestId('location')).toHaveTextContent('/projects/42?tab=delivery#evidence');
    expect(screen.queryByText('Sign in to Regent Agent')).not.toBeInTheDocument();

    await act(async () => finishBootstrap({ status: 401 }));

    expect(await screen.findByText('Sign in to Regent Agent')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2Fprojects%2F42%3Ftab%3Ddelivery%23evidence');
  });

  it('preserves a safe root query and hash return target', async () => {
    apiMocks.session.mockRejectedValue({ status: 401 });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/?tab=overview#summary']}><AppThemeProvider><LocationProbe /><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);

    expect(await screen.findByText('Sign in to Regent Agent')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2F%3Ftab%3Doverview%23summary');
  });

  it('registers a single security-incidents route', async () => {
    apiMocks.session.mockResolvedValue(session);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/governance/incidents']}><AppThemeProvider><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);

    expect(await screen.findByRole('heading', { name: 'Security Incidents' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Security & governance' })).not.toBeInTheDocument();
  });

  it('denies direct message and collaboration routes when effective access is missing', async () => {
    apiMocks.session.mockResolvedValue({
      ...session,
      effective_access: { can_read_chat: false, can_read_collaboration: false, collaboration_scope: 'none' }
    });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/messages']}><AppThemeProvider><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);

    expect(await screen.findByRole('heading', { name: 'Permission denied' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Messages & collaboration' })).not.toBeInTheDocument();
  });

  it('localizes the direct-route permission denied state', async () => {
    await i18n.changeLanguage('zh-CN');
    apiMocks.session.mockResolvedValue({
      ...session,
      effective_access: { can_read_chat: false, can_read_collaboration: false, collaboration_scope: 'none' }
    });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/messages']}><AppThemeProvider><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);

    expect(await screen.findByRole('heading', { name: '权限不足' })).toBeInTheDocument();
  });

  it('denies direct collaboration routes when effective access is missing', async () => {
    apiMocks.session.mockResolvedValue({
      ...session,
      effective_access: { can_read_chat: true, can_read_collaboration: false, collaboration_scope: 'participant' }
    });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/organization/collaboration']}><AppThemeProvider><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);

    expect(await screen.findByRole('heading', { name: 'Permission denied' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Organization collaboration' })).not.toBeInTheDocument();
  });

  it('shows a development-only notice for the component gallery in production', async () => {
    vi.stubEnv('DEV', false);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/dev/components']}><AppThemeProvider><LocationProbe /><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);

    expect(await screen.findByRole('heading', { name: 'Component gallery unavailable' })).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/dev/components');
  });

  it('omits only the exact root return target', async () => {
    apiMocks.session.mockRejectedValue({ status: 401 });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/']}><AppThemeProvider><LocationProbe /><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);

    expect(await screen.findByText('Sign in to Regent Agent')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/login');
  });
});
