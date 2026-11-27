import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppThemeProvider } from '@regent/ui-shared';
import { AppShell } from './AppShell';
import i18n from '../lib/i18n';

const authMocks = vi.hoisted(() => ({
  session: {
    displayName: 'Workspace Owner',
    sessionKind: 'Standard',
    effectiveAccess: { canReadChat: true, canReadCollaboration: true, collaborationScope: 'observer' }
  },
  logout: vi.fn()
}));
const hubMocks = vi.hoisted(() => ({ useTenantHub: vi.fn() }));
vi.mock('./useAuth', () => ({ useAuth: () => authMocks }));
vi.mock('../hooks/useTenantHub', () => ({ useTenantHub: hubMocks.useTenantHub }));

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
  authMocks.logout.mockReset();
  authMocks.logout.mockResolvedValue(undefined);
  authMocks.session.sessionKind = 'Standard';
  authMocks.session.effectiveAccess = { canReadChat: true, canReadCollaboration: true, collaborationScope: 'observer' };
  hubMocks.useTenantHub.mockReset();
  hubMocks.useTenantHub.mockReturnValue('connected');
});

afterEach(() => vi.unstubAllEnvs());

describe('Tenant AppShell authentication labels', () => {
  it('omits the component gallery from production navigation', async () => {
    vi.stubEnv('DEV', false);
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={['/']}><AppThemeProvider><Routes>
      <Route element={<AppShell />}><Route path="/" element={<div>Workspace</div>} /></Route>
    </Routes></AppThemeProvider></MemoryRouter>);

    await user.click(screen.getByRole('button', { name: 'Open navigation' }));
    expect(screen.queryByRole('link', { name: 'Components' })).not.toBeInTheDocument();
  });

  it('hides chat and collaboration navigation when effective access is denied', async () => {
    authMocks.session.effectiveAccess = { canReadChat: false, canReadCollaboration: false, collaborationScope: 'none' };
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={['/']}><AppThemeProvider><Routes>
      <Route element={<AppShell />}><Route path="/" element={<div>Workspace</div>} /></Route>
    </Routes></AppThemeProvider></MemoryRouter>);

    await user.click(screen.getByRole('button', { name: 'Open navigation' }));
    expect(screen.queryByRole('link', { name: 'Messages' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Collaboration workspace' })).not.toBeInTheDocument();
  });

  it('hides chat and collaboration and disables the hub for emergency sessions', async () => {
    authMocks.session.sessionKind = 'Emergency';
    authMocks.session.effectiveAccess = { canReadChat: true, canReadCollaboration: true, collaborationScope: 'observer' };
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={['/']}><AppThemeProvider><Routes>
      <Route element={<AppShell />}><Route path="/" element={<div>Workspace</div>} /></Route>
    </Routes></AppThemeProvider></MemoryRouter>);

    await user.click(screen.getByRole('button', { name: 'Open navigation' }));

    expect(screen.queryByRole('link', { name: 'Messages' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Collaboration workspace' })).not.toBeInTheDocument();
    expect(hubMocks.useTenantHub).toHaveBeenCalledWith(false);
  });

  it('localizes navigation, logout and signed-in caption without translating the display name', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={['/']}><AppThemeProvider><Routes>
      <Route element={<AppShell />}><Route path="/" element={<div>Workspace</div>} /></Route>
    </Routes></AppThemeProvider></MemoryRouter>);

    expect(screen.getByRole('button', { name: 'Open navigation' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Logout' })).toBeInTheDocument();
    expect(screen.getByText('Signed in as Workspace Owner')).toBeInTheDocument();

    await i18n.changeLanguage('zh-CN');

    expect(screen.getByRole('button', { name: '打开导航' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '退出登录' })).toBeInTheDocument();
    expect(screen.getByText('已登录：Workspace Owner')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '打开导航' }));
    expect(screen.getByRole('link', { name: '总览' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    await user.click(await screen.findByRole('button', { name: '退出登录' }));
    expect(authMocks.logout).toHaveBeenCalledOnce();
  });
});
