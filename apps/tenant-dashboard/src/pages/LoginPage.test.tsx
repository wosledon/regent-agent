import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppThemeProvider } from '@regent/ui-shared';
import { ApiError } from '../lib/api-client/api';
import i18n from '../lib/i18n';
import { LoginPage } from './LoginPage';
import { authenticationModes, defaultAuthMode } from './authModes';

const authMocks = vi.hoisted(() => ({
  status: 'anonymous' as 'checking' | 'authenticated' | 'anonymous',
  session: null as null | { displayName: string },
  login: vi.fn(),
  passwordLogin: vi.fn(),
  register: vi.fn(),
  emergencyLogin: vi.fn(),
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
    <Route path="/settings" element={<div>Recovery settings</div>} />
    <Route path="/projects/:projectId" element={<div>Project target</div>} />
    <Route path="/" element={<div>Dashboard target</div>} />
  </Routes></AppThemeProvider></MemoryRouter>);
}

beforeEach(async () => {
  localStorage.clear();
  await i18n.changeLanguage('en-US');
  authMocks.status = 'anonymous';
  authMocks.session = null;
  Object.values(authMocks).forEach(value => typeof value === 'function' && vi.mocked(value).mockReset());
  authMocks.login.mockResolvedValue(undefined);
  authMocks.passwordLogin.mockResolvedValue(undefined);
  authMocks.register.mockResolvedValue(['code-one', 'code-two']);
  authMocks.emergencyLogin.mockResolvedValue('rotated-credential');
  authMocks.logout.mockResolvedValue(undefined);
});

afterEach(() => vi.unstubAllEnvs());

describe('LoginPage', () => {
  it('renders the polished development sign-in form inside AuthLayout', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: 'Sign in to Regent Agent' })).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toHaveTextContent('Autonomous work, with people in control');
    expect(screen.getByRole('main')).toContainElement(screen.getByPlaceholderText('owner@example.test'));
    expect(screen.getByPlaceholderText('owner@example.test')).toHaveAttribute('required');
    expect(screen.getByPlaceholderText('owner@example.test')).toHaveAttribute('autocomplete', 'email');
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('defaults to Password and excludes Local development in production', async () => {
    expect(defaultAuthMode(true)).toBe('password');
    expect(defaultAuthMode(false)).toBe('dev');
    expect(authenticationModes(true)).not.toContain('dev');
    expect(authenticationModes(false)).toContain('dev');

    vi.stubEnv('PROD', true);
    const user = userEvent.setup();
    renderPage();
    const mode = screen.getByRole('combobox', { name: 'Authentication mode' });
    expect(mode).toHaveTextContent('Password');
    await user.click(mode);
    expect(screen.queryByRole('option', { name: 'Local development' })).not.toBeInTheDocument();
  });

  it('switches display language, persists it and applies document lang without invoking authentication', async () => {
    const user = userEvent.setup();
    renderPage('/login?returnTo=%2Fprojects%2F42');
    await user.click(screen.getByRole('button', { name: '简体中文' }));

    expect(await screen.findByRole('heading', { name: '登录 Regent Agent' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: '认证方式' })).toBeInTheDocument();
    expect(localStorage.getItem('regent.ui.locale')).toBe('zh-CN');
    expect(document.documentElement.lang).toBe('zh-CN');
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2Fprojects%2F42');
    expect(authMocks.login).not.toHaveBeenCalled();
    expect(authMocks.passwordLogin).not.toHaveBeenCalled();
  });

  it('continues password login to a validated return target', async () => {
    const user = userEvent.setup();
    renderPage('/login?returnTo=%2Fprojects%2F42%3Ftab%3Ddelivery%23evidence');
    await user.click(screen.getByRole('combobox', { name: 'Authentication mode' }));
    await user.click(screen.getByRole('option', { name: 'Password' }));
    await user.type(screen.getByPlaceholderText('At least 10 characters'), 'strong-password-123');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(authMocks.passwordLogin).toHaveBeenCalledWith('owner@example.test', 'strong-password-123');
    expect(await screen.findByText('Project target')).toBeInTheDocument();
  });

  it('continues password login to a safe root query and hash target', async () => {
    const user = userEvent.setup();
    renderPage('/login?returnTo=%2F%3Ftab%3Doverview%23summary');
    await user.click(screen.getByRole('combobox', { name: 'Authentication mode' }));
    await user.click(screen.getByRole('option', { name: 'Password' }));
    await user.type(screen.getByPlaceholderText('At least 10 characters'), 'strong-password-123');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Dashboard target')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/?tab=overview#summary');
  });

  it('shows localized accessible registration fields and recovery codes before continuing', async () => {
    const user = userEvent.setup();
    renderPage('/login?returnTo=%2Fprojects%2F42');
    await user.click(screen.getByRole('combobox', { name: 'Authentication mode' }));
    await user.click(screen.getByRole('option', { name: 'Create account' }));

    expect(screen.getByRole('textbox', { name: 'Display name' })).toHaveAttribute('required');
    expect(screen.getByRole('textbox', { name: 'Company name' })).toHaveAttribute('autocomplete', 'organization');
    expect(screen.getByPlaceholderText('At least 10 characters')).toHaveAttribute('autocomplete', 'new-password');

    await user.click(screen.getByRole('button', { name: '简体中文' }));
    expect(screen.getByRole('combobox', { name: '认证方式' })).toHaveTextContent('创建账户');
    expect(screen.getByRole('textbox', { name: '显示名称' })).toHaveAttribute('required', '');
    expect(screen.getByRole('textbox', { name: '显示名称' })).toHaveAttribute('autocomplete', 'name');
    expect(screen.getByRole('textbox', { name: '公司名称' })).toHaveAttribute('required', '');
    expect(screen.getByRole('textbox', { name: '公司名称' })).toHaveAttribute('autocomplete', 'organization');
    expect(screen.getByLabelText('密码')).toHaveAttribute('required', '');
    expect(screen.getByLabelText('密码')).toHaveAttribute('autocomplete', 'new-password');
    expect(screen.getByLabelText('密码')).toHaveAttribute('placeholder', '至少 10 个字符');
    expect(screen.getByRole('textbox', { name: '邮箱' })).toHaveAttribute('autocomplete', 'email');

    await user.click(screen.getByRole('button', { name: 'English' }));
    await user.type(screen.getByPlaceholderText('At least 10 characters'), 'strong-password-123');
    await user.type(screen.getByPlaceholderText('Display name'), 'New Owner');
    await user.type(screen.getByPlaceholderText('Company name'), 'New Co');
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByRole('heading', { name: 'Save recovery codes' })).toBeInTheDocument();
    expect(screen.getByText('code-one')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2Fprojects%2F42');
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
    await user.click(screen.getByRole('checkbox', { name: /saved these recovery codes/ }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText('Project target')).toBeInTheDocument();
  });

  it('shows the rotated emergency credential before continuing to settings', async () => {
    const user = userEvent.setup();
    renderPage('/login?returnTo=%2Fprojects%2F42');
    await user.click(screen.getByRole('combobox', { name: 'Authentication mode' }));
    await user.click(screen.getByRole('option', { name: 'Emergency access' }));
    await user.type(screen.getByRole('textbox', { name: 'Tenant ID' }), '11111111-1111-1111-1111-111111111111');
    await user.type(screen.getByLabelText('Emergency credential'), 'emergency-credential');
    await user.type(screen.getByRole('textbox', { name: 'Purpose' }), 'Restore unavailable identity provider');
    await user.click(screen.getByRole('button', { name: 'Use emergency access' }));

    expect(await screen.findByRole('heading', { name: 'Store the rotated emergency credential' })).toBeInTheDocument();
    expect(screen.getByText('rotated-credential')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/login');
    expect(screen.getByRole('button', { name: 'Continue to recovery settings' })).toBeDisabled();
    await user.click(screen.getByRole('checkbox', { name: /stored the rotated emergency credential/ }));
    await user.click(screen.getByRole('button', { name: 'Continue to recovery settings' }));
    expect(await screen.findByText('Recovery settings')).toBeInTheDocument();
  });

  it('maps backend failures to safe localized feedback instead of rendering exception text', async () => {
    authMocks.passwordLogin.mockRejectedValue(new ApiError(503, 'INTERNAL_ERROR', 'private backend detail'));
    const user = userEvent.setup();
    vi.stubEnv('PROD', true);
    renderPage();
    await user.type(screen.getByPlaceholderText('At least 10 characters'), 'strong-password-123');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Authentication is temporarily unavailable. Please try again later.')).toBeInTheDocument();
    expect(screen.queryByText(/private backend detail/)).not.toBeInTheDocument();
  });

  it('localizes network failures without rendering arbitrary exception text', async () => {
    authMocks.passwordLogin.mockRejectedValue(new TypeError('arbitrary network detail'));
    const user = userEvent.setup();
    vi.stubEnv('PROD', true);
    renderPage();
    await user.click(screen.getByRole('button', { name: '简体中文' }));
    await user.type(screen.getByPlaceholderText('至少 10 个字符'), 'strong-password-123');
    await user.click(screen.getByRole('button', { name: '登录' }));

    expect(await screen.findByText('无法连接认证服务，请检查网络后重试。')).toBeInTheDocument();
    expect(screen.queryByText(/arbitrary network detail/)).not.toBeInTheDocument();
  });

  it('shows checking and submission loading states', async () => {
    authMocks.status = 'checking';
    const checking = renderPage();
    expect(screen.getByLabelText('Checking your session…')).toBeInTheDocument();
    checking.unmount();

    authMocks.status = 'anonymous';
    let finishLogin: () => void = () => undefined;
    authMocks.passwordLogin.mockReturnValue(new Promise<void>(resolve => { finishLogin = resolve; }));
    vi.stubEnv('PROD', true);
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: '简体中文' }));
    await user.type(screen.getByPlaceholderText('至少 10 个字符'), 'strong-password-123');
    await user.click(screen.getByRole('button', { name: '登录' }));
    expect(screen.getByText('正在登录…')).toHaveAttribute('role', 'status');
    expect(screen.getByRole('button', { name: '登录' })).toBeDisabled();
    finishLogin();
    expect(await screen.findByText('Dashboard target')).toBeInTheDocument();
  });

  it('offers useful Continue and Logout actions when already authenticated', async () => {
    authMocks.status = 'authenticated';
    authMocks.session = { displayName: 'Workspace Owner' };
    const user = userEvent.setup();
    const first = renderPage('/login?returnTo=%2Fprojects%2F42');
    await user.click(screen.getByRole('button', { name: '简体中文' }));
    expect(screen.getByRole('heading', { name: '已登录：Workspace Owner' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '继续' }));
    expect(await screen.findByText('Project target')).toBeInTheDocument();
    first.unmount();

    renderPage('/login?returnTo=%2Fprojects%2F42');
    await user.click(screen.getByRole('button', { name: '退出登录' }));
    expect(authMocks.logout).toHaveBeenCalledOnce();
  });
});
