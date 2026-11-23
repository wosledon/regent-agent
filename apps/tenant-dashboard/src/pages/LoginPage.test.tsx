import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AppThemeProvider } from '@regent/ui-shared';
import { AuthProvider } from '../app/AuthProvider';
import { LoginPage } from './LoginPage';

describe('LoginPage', () => {
  it('renders the development sign-in form', () => {
    render(<MemoryRouter><AppThemeProvider><AuthProvider><LoginPage /></AuthProvider></AppThemeProvider></MemoryRouter>);
    expect(screen.getByPlaceholderText('owner@example.test')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
  });
});
