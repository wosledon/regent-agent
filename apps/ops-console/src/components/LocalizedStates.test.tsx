import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { AppThemeProvider } from '@regent/ui-shared';
import { beforeEach, describe, expect, it } from 'vitest';
import { LocalizedErrorState, LocalizedLoadingState } from './LocalizedStates';
import i18n from '../lib/i18n';

function renderState(state: ReactNode) {
  return render(<AppThemeProvider>{state}</AppThemeProvider>);
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('LocalizedStates', () => {
  it('uses a labeled skeleton by default and a compact inline loader on request', () => {
    const { rerender } = renderState(<LocalizedLoadingState />);
    expect(screen.getByRole('status', { name: 'Loading…' })).toHaveAttribute('aria-busy', 'true');

    rerender(<AppThemeProvider><LocalizedLoadingState inline /></AppThemeProvider>);
    expect(screen.getByRole('status')).toHaveTextContent('Loading…');
  });

  it('marks stale data without hiding the last known message', () => {
    renderState(<LocalizedErrorState message="Cached snapshot" stale />);
    expect(screen.getByText('Stale')).toBeInTheDocument();
    expect(screen.getByText('Cached snapshot')).toBeInTheDocument();
  });

  it('presents permission failures as warning states', () => {
    renderState(<LocalizedErrorState message="Permission denied. You do not have access to this operation." permissionDenied />);
    expect(screen.getByRole('alert')).toHaveClass('MuiAlert-standardWarning');
  });
});
