import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ErrorState } from './ErrorState';

describe('ErrorState', () => {
  it('replaces sensitive 403 details with non-sensitive permission guidance', () => {
    render(<ErrorState status={403} message="Forbidden: tenant internal resource identifier" />);
    expect(screen.getByText('Permission denied')).toBeInTheDocument();
    expect(screen.getByText('You do not have access to this resource.')).toBeInTheDocument();
    expect(screen.queryByText(/tenant internal resource identifier/)).not.toBeInTheDocument();
  });

  it('preserves ordinary messages and retry behavior', () => {
    const retry = vi.fn();
    render(<ErrorState message="Service unavailable" onRetry={retry} />);
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(retry).toHaveBeenCalledOnce();
    expect(screen.getByText('Service unavailable')).toBeInTheDocument();
  });
});
