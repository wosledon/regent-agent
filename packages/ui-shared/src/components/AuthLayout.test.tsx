import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { componentRegistry } from '../component-gallery/componentRegistry';
import { AppThemeProvider } from '../theme/AppThemeProvider';
import { AuthLayout, type AuthLayoutProps } from './AuthLayout';

const props: AuthLayoutProps = {
  audience: 'TENANT WORKSPACE',
  headline: 'A focused home for autonomous work',
  description: 'Projects, approvals and knowledge in one secure workspace.',
  highlights: [
    { icon: 'organization', title: 'Aligned teams', description: 'Clear ownership.' },
    { icon: 'success', title: 'Governed execution', description: 'Policy stays visible.' }
  ],
  footer: 'Regent Agent footer',
  locale: 'zh-CN',
  onLocaleChange: () => undefined,
  localeLabel: 'Display language'
};

describe('AuthLayout', () => {
  it('renders branded content, the form surface, language control and footer as landmarks', () => {
    render(<AppThemeProvider><AuthLayout {...props}><form aria-label="Sign in form" /></AuthLayout></AppThemeProvider>);

    const marketing = screen.getByRole('complementary', { name: props.headline });
    expect(marketing).toHaveTextContent(props.audience);
    expect(marketing).toHaveTextContent(props.description);
    expect(marketing).toHaveTextContent('Aligned teams');
    expect(marketing).toHaveTextContent('Policy stays visible.');
    expect(screen.getByRole('main')).toContainElement(screen.getByRole('form', { name: 'Sign in form' }));
    expect(screen.getByRole('banner')).toContainElement(screen.getByRole('group', { name: 'Display language' }));
    expect(screen.getByRole('contentinfo')).toHaveTextContent(props.footer);
  });

  it('keeps separate mobile-brand and desktop-marketing structures with a responsive rule', () => {
    render(<AppThemeProvider><AuthLayout {...props}><div>Form</div></AuthLayout></AppThemeProvider>);

    expect(screen.getByTestId('auth-mobile-brand')).toHaveTextContent('Regent Agent');
    expect(screen.getByTestId('auth-marketing')).toHaveTextContent('Regent Agent');
    const css = Array.from(document.styleSheets).flatMap(sheet => {
      try { return Array.from(sheet.cssRules).map(rule => rule.cssText); } catch { return []; }
    }).join('\n');
    expect(css).toMatch(/display:\s*none/);
    expect(css).toMatch(/@media \(min-width:\s*900px\)/);
  });

  it('registers interactive Candidate demos for both authentication primitives', async () => {
    const authLayout = componentRegistry.find(entry => entry.id === 'auth-layout');
    const localeSwitch = componentRegistry.find(entry => entry.id === 'locale-switch');
    expect(authLayout?.status).toBe('candidate');
    expect(localeSwitch?.status).toBe('candidate');
    if (!authLayout || !localeSwitch) throw new Error('Authentication demos must be registered');

    const AuthLayoutDemo = authLayout.demo;
    const LocaleSwitchDemo = localeSwitch.demo;
    const user = userEvent.setup();
    render(<AppThemeProvider><LocaleSwitchDemo /></AppThemeProvider>);
    await user.click(screen.getByRole('button', { name: 'English' }));
    expect(screen.getByRole('button', { name: 'English' })).toHaveAttribute('aria-pressed', 'true');
    expect(localStorage.getItem('regent.ui.locale')).toBe('en-US');
    expect(document.documentElement.lang).toBe('en-US');

    render(<AppThemeProvider><AuthLayoutDemo /></AppThemeProvider>);
    expect(screen.getByRole('heading', { name: 'Welcome back' })).toBeInTheDocument();
    expect(screen.getByPlaceholderText('owner@example.test')).toBeInTheDocument();
  });
});
