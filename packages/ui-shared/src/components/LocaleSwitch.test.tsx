import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthLayoutDemo, LocaleSwitchDemo } from '../component-gallery/authDemos';
import { AppThemeProvider } from '../theme/AppThemeProvider';
import { LocaleSwitch } from './LocaleSwitch';

beforeEach(() => localStorage.clear());
afterEach(() => vi.restoreAllMocks());

describe('LocaleSwitch', () => {
  it('exposes the selected language and reports a new display preference', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<LocaleSwitch value="zh-CN" onChange={onChange} label="Display language" />);

    const group = screen.getByRole('group', { name: 'Display language' });
    expect(screen.getByRole('button', { name: '简体中文' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'English' })).toHaveAttribute('aria-pressed', 'false');
    expect(group).toContainElement(screen.getByRole('button', { name: 'English' }));

    await user.click(screen.getByRole('button', { name: 'English' }));
    expect(onChange).toHaveBeenCalledExactlyOnceWith('en-US');
  });

  it.each([
    { saved: 'en-US', browser: 'zh-CN', expected: 'en-US' },
    { saved: 'zh-CN', browser: 'en-US', expected: 'zh-CN' },
    { saved: null, browser: 'en-GB', expected: 'en-US' },
    { saved: null, browser: 'fr-FR', expected: 'zh-CN' }
  ])('initializes both gallery demos from saved/browser locale ($expected)', ({ saved, browser, expected }) => {
    if (saved) localStorage.setItem('regent.ui.locale', saved);
    vi.spyOn(navigator, 'language', 'get').mockReturnValue(browser);

    const localeSwitch = render(<AppThemeProvider><LocaleSwitchDemo /></AppThemeProvider>);
    expect(screen.getByRole('button', { name: expected === 'en-US' ? 'English' : '简体中文' })).toHaveAttribute('aria-pressed', 'true');
    localeSwitch.unmount();

    render(<AppThemeProvider><AuthLayoutDemo /></AppThemeProvider>);
    expect(screen.getByRole('button', { name: expected === 'en-US' ? 'English' : '简体中文' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('heading', { name: 'Welcome back' })).toBeInTheDocument();
  });
});
