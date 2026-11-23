import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Button } from './Button';

describe('Button', () => {
  it('renders a semantic action and handles clicks', async () => {
    const onClick = vi.fn();
    render(<Button onClick={onClick}>Run task</Button>);
    await userEvent.click(screen.getByRole('button', { name: 'Run task' }));
    expect(onClick).toHaveBeenCalledOnce();
  });

  it('disables duplicate actions while loading', () => {
    render(<Button loading>Run task</Button>);
    expect(screen.getByRole('button')).toBeDisabled();
  });
});
