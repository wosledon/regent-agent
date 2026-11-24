import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { GraphCanvas } from './GraphCanvas';

describe('GraphCanvas', () => {
  it('exposes graph nodes as keyboard-accessible selections', async () => {
    const onSelect = vi.fn();
    render(<GraphCanvas
      nodes={[{ id: 'employee:1', label: 'Controller', subtitle: 'Company lead', x: 40, y: 40, kind: 'Employee', status: 'Active' }]}
      edges={[]}
      onSelect={onSelect}
    />);
    const node = screen.getByRole('button', { name: 'Controller, Company lead' });
    node.focus();
    await userEvent.keyboard('{Enter}');
    expect(onSelect).toHaveBeenCalledOnce();
    expect(onSelect.mock.calls[0][0].id).toBe('employee:1');
  });

  it('provides zoom controls without hiding the graph', async () => {
    render(<GraphCanvas nodes={[]} edges={[]} onSelect={() => undefined} />);
    await userEvent.click(screen.getByRole('button', { name: 'Zoom in' }));
    await userEvent.click(screen.getByRole('button', { name: 'Reset zoom' }));
    expect(screen.getByRole('img', { name: 'Organization collaboration graph' })).toBeInTheDocument();
  });
});
