import { act, fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppThemeProvider } from '@regent/ui-shared';
import { App } from './App';
import i18n from './lib/i18n';
import { canWriteOperations } from './lib/operationsPermissions';

const apiMocks = vi.hoisted(() => ({
  me: vi.fn(), login: vi.fn(), logout: vi.fn(), backups: vi.fn(), backupCoverage: vi.fn(), backupJobs: vi.fn(), restoreJobs: vi.fn(), status: vi.fn()
}));
vi.mock('./lib/api', () => ({
  OPS_UNAUTHORIZED_EVENT: 'regent:ops:unauthorized',
  OPS_PERMISSION_DENIED_EVENT: 'regent:ops:permission-denied',
  api: apiMocks
}));

const operator = {
  email: 'operator@example.test', display_name: 'Demo Operator', role: 'SRE',
  auth_method: 'Development', session_kind: 'Operator', capabilities: []
};

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname}{location.search}{location.hash}</output>;
}

function renderApp(entry = '/login') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[entry]}><AppThemeProvider><LocationProbe /><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.me.mockRejectedValue({ status: 401 });
  apiMocks.backups.mockResolvedValue([]);
  apiMocks.backupCoverage.mockResolvedValue({});
  apiMocks.backupJobs.mockResolvedValue([]);
  apiMocks.restoreJobs.mockResolvedValue([]);
  apiMocks.status.mockResolvedValue({ overallStatus: 'Operational', checkedAt: '2026-11-25T10:00:00Z', components: [], incidents: [] });
});

describe('Ops Console', () => {
  it('derives write access from viewer role and operator capabilities', () => {
    expect(canWriteOperations(null)).toBe(false);
    expect(canWriteOperations({ ...operator, role: 'Ops Viewer', capabilities: ['OperatorWriter'] })).toBe(false);
    expect(canWriteOperations({ ...operator, capabilities: ['OperatorReader'] })).toBe(false);
    expect(canWriteOperations({ ...operator, capabilities: ['OperatorReader', 'OperatorWriter'] })).toBe(true);
    expect(canWriteOperations({ ...operator, capabilities: [] })).toBe(true);
  });

  it('waits for bootstrap with localized loading feedback', async () => {
    apiMocks.me.mockReturnValue(new Promise(() => undefined));
    renderApp('/backups');

    expect(screen.getByRole('progressbar', { name: 'Checking your session…' })).toBeInTheDocument();
    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByRole('progressbar', { name: '正在检查您的会话…' })).toBeInTheDocument();
  });

  it('keeps the public status page available after anonymous bootstrap', async () => {
    renderApp('/status');

    expect(await screen.findByRole('heading', { name: 'Platform status' })).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/status');
    expect(screen.queryByRole('heading', { name: 'Operations sign in' })).not.toBeInTheDocument();
  });

  it('does not navigate away from public status on a centralized 401', async () => {
    apiMocks.me.mockReturnValue(new Promise(() => undefined));
    renderApp('/status');
    act(() => window.dispatchEvent(new Event('regent:ops:unauthorized')));

    expect(await screen.findByRole('heading', { name: 'Platform status' })).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/status');
  });

  it('renders the operator sign-in page after anonymous bootstrap', async () => {
    renderApp();
    expect(await screen.findByText('Operations sign in')).toBeInTheDocument();
  });

  it('preserves a safe return target through the route guard', async () => {
    renderApp('/backups?scope=all#jobs');
    expect(await screen.findByText('Operations sign in')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2Fbackups%3Fscope%3Dall%23jobs');
  });

  it('preserves a safe root query and hash return target', async () => {
    renderApp('/?tab=overview#summary');
    expect(await screen.findByText('Operations sign in')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2F%3Ftab%3Doverview%23summary');
  });

  it('omits only the exact root return target', async () => {
    renderApp('/');
    expect(await screen.findByText('Operations sign in')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/login');
  });

  it('redirects an authenticated Operator safely and localizes shell-login labels', async () => {
    apiMocks.me.mockResolvedValue(operator);
    renderApp('/login?returnTo=%2Fbackups');

    expect(await screen.findByRole('button', { name: 'Open navigation' })).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/backups');
    expect(screen.getByRole('button', { name: 'Logout' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }));
    expect(screen.getByRole('link', { name: 'Backups' })).toBeInTheDocument();

    await i18n.changeLanguage('zh-CN');
    expect(screen.getByRole('link', { name: '备份' })).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape' });
    expect(await screen.findByRole('button', { name: '打开导航' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '退出登录' })).toBeInTheDocument();
  });
});
