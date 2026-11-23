import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AppThemeProvider } from '@regent/ui-shared';
import { App } from './App';

describe('Ops Console', () => {
  it('renders the operator sign-in page', () => {
    const client = new QueryClient();
    render(<QueryClientProvider client={client}><MemoryRouter><AppThemeProvider><App /></AppThemeProvider></MemoryRouter></QueryClientProvider>);
    expect(screen.getByText('Operations sign in')).toBeInTheDocument();
  });
});
