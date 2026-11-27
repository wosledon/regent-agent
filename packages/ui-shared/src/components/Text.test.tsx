import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AppThemeProvider } from '../theme/AppThemeProvider';
import { Text } from './Text';

describe('Text', () => {
  it('scales page titles at theme breakpoints and wraps unbroken content', () => {
    render(<AppThemeProvider><Text variant="h1">Company 1795692509777-8c6ed261</Text></AppThemeProvider>);

    expect(screen.getByRole('heading', { level: 1 })).toHaveStyle({ overflowWrap: 'anywhere' });
    const css = Array.from(document.styleSheets).flatMap(sheet => {
      try { return Array.from(sheet.cssRules).map(rule => rule.cssText); } catch { return []; }
    }).join('\n');
    expect(css).toMatch(/font-size:\s*1\.75rem/);
    expect(css).toMatch(/line-height:\s*2\.25rem/);
    expect(css).not.toMatch(/font-size:\s*4rem/);
  });
});
