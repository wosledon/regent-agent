import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Select } from './Select';

const options = [
  { value: 'password', label: 'Password' },
  { value: 'register', label: 'Create account' }
] as const;

describe('Select', () => {
  it('keeps a stable whitespace-free label association when the label language changes', () => {
    const onChange = vi.fn();
    const { rerender } = render(<Select label="Authentication mode" value="password" options={options} onChange={onChange} />);
    const label = screen.getByText('Authentication mode', { selector: 'label' });
    const select = screen.getByRole('combobox', { name: 'Authentication mode' });

    expect(label.id).not.toMatch(/\s/);
    expect(select).toHaveAccessibleName('Authentication mode');
    expect(select.getAttribute('aria-labelledby')).toContain(label.id);

    rerender(<Select label="认证方式" value="password" options={options} onChange={onChange} />);

    expect(screen.getByText('认证方式', { selector: 'label' })).toHaveAttribute('id', label.id);
    expect(screen.getByRole('combobox', { name: '认证方式' })).toHaveAccessibleName('认证方式');
  });

  it('generates unique label IDs for separate selects with the same visible label', () => {
    render(<>
      <Select label="Authentication mode" value="password" options={options} onChange={() => undefined} />
      <Select label="Authentication mode" value="register" options={options} onChange={() => undefined} />
    </>);

    const labels = screen.getAllByText('Authentication mode', { selector: 'label' });
    const selects = screen.getAllByRole('combobox', { name: 'Authentication mode' });
    expect(new Set(labels.map(label => label.id)).size).toBe(2);
    expect(selects[0]?.getAttribute('aria-labelledby')).toContain(labels[0]?.id);
    expect(selects[1]?.getAttribute('aria-labelledby')).toContain(labels[1]?.id);
  });
});
