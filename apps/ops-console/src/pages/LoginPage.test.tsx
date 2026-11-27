import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppThemeProvider } from '@regent/ui-shared';
import { ApiError } from '../lib/api';
import i18n from '../lib/i18n';
import { LoginPage } from './LoginPage';

const authMocks = vi.hoisted(() => ({
  status: 'anonymous' as 'checking' | 'authenticated' | 'anonymous',
  user: null as null | { display_name: string },
  login: vi.fn(),
  logout: vi.fn()
}));
vi.mock('../app/useAuth', () => ({ useAuth: () => authMocks }));

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname}{location.search}{location.hash}</output>;
}

function renderPage(entry = '/login') {
  return render(<MemoryRouter initialEntries={[entry]}><AppThemeProvider><LocationProbe /><Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route path="/backups" element={<div>Backup target</div>} />
    <Route path="/" element={<div>Overview target</div>} />
  </Routes></AppThemeProvider></MemoryRouter>);
}

beforeEach(async () => {
  localStorage.clear();
  await i18n.changeLanguage('en-US');
  authMocks.status = 'anonymous';
  authMocks.user = null;
  authMocks.login.mockReset();
  authMocks.login.mockResolvedValue(undefined);
  authMocks.logout.mockReset();
});

describe('Ops LoginPage', () => {
  it('renders the localized split-screen layout and explicit local demo notice', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: 'Operations sign in' })).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toHaveTextContent('Keep every tenant operation accountable');
    expect(screen.getByText('Local / demo Operator entry')).toBeInTheDocument();
    expect(screen.getByText(/does not provide production SSO or MFA/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Enter local demo Operator' })).toBeInTheDocument();
  });

  it('switches and persists display language without changing the return target or session', async () => {
    renderPage('/login?returnTo=%2Fbackups');
    fireEvent.click(screen.getByRole('button', { name: '简体中文' }));

    expect(await screen.findByRole('heading', { name: '运营登录' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '进入本地演示 Operator' })).toBeInTheDocument();
    expect(localStorage.getItem('regent.ui.locale')).toBe('zh-CN');
    expect(document.documentElement.lang).toBe('zh-CN');
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2Fbackups');
    expect(authMocks.login).not.toHaveBeenCalled();
  });

  it('shows checking and submission loading states', async () => {
    authMocks.status = 'checking';
    const checking = renderPage();
    expect(screen.getByLabelText('Checking your session…')).toBeInTheDocument();
    checking.unmount();

    authMocks.status = 'anonymous';
    let finishLogin: () => void = () => undefined;
    authMocks.login.mockReturnValue(new Promise<void>(resolve => { finishLogin = resolve; }));
    renderPage('/login?returnTo=%2Fbackups');
    fireEvent.click(screen.getByRole('button', { name: '简体中文' }));
    fireEvent.click(screen.getByRole('button', { name: '进入本地演示 Operator' }));
    expect(screen.getByText('正在进入运营控制台…')).toHaveAttribute('role', 'status');
    expect(screen.getByRole('button', { name: '进入本地演示 Operator' })).toBeDisabled();
    finishLogin();
    expect(await screen.findByText('Backup target')).toBeInTheDocument();
  });

  it('shows safe localized errors instead of backend exception text', async () => {
    authMocks.login.mockRejectedValue(new ApiError(503, 'INTERNAL_ERROR'));
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: '简体中文' }));
    fireEvent.click(screen.getByRole('button', { name: '进入本地演示 Operator' }));

    expect(await screen.findByText('运营登录服务暂时不可用，请稍后重试。')).toBeInTheDocument();
    expect(screen.queryByText(/Request failed/)).not.toBeInTheDocument();
  });

  it('redirects an authenticated Operator to a validated return target', async () => {
    authMocks.status = 'authenticated';
    authMocks.user = { display_name: 'Demo Operator' };
    renderPage('/login?returnTo=%2Fbackups');
    expect(await screen.findByText('Backup target')).toBeInTheDocument();
    expect(authMocks.login).not.toHaveBeenCalled();
  });

  it('continues an authenticated Operator to a safe root query and hash target', async () => {
    authMocks.status = 'authenticated';
    authMocks.user = { display_name: 'Demo Operator' };
    renderPage('/login?returnTo=%2F%3Ftab%3Doverview%23summary');

    expect(await screen.findByText('Overview target')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/?tab=overview#summary');
  });

  it('rejects an unsafe return target for an authenticated Operator', async () => {
    authMocks.status = 'authenticated';
    authMocks.user = { display_name: 'Demo Operator' };
    renderPage('/login?returnTo=https%3A%2F%2Fexample.test');
    expect(await screen.findByText('Overview target')).toBeInTheDocument();
  });
});
