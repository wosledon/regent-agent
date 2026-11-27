import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { AppThemeProvider } from '@regent/ui-shared';

export function field(label: string, index = 0): HTMLElement {
  const labelNode = screen.getAllByText(label, { selector: 'label' })[index];
  const controlId = labelNode?.getAttribute('for');
  const control = (controlId ? document.getElementById(controlId) : null)
    ?? labelNode?.parentElement?.querySelector('input, textarea, [role="combobox"]');
  if (!(control instanceof HTMLElement)) throw new Error(`No form control found for ${label}`);
  return control;
}

export async function findField(label: string, index = 0): Promise<HTMLElement> {
  await screen.findAllByText(label, { selector: 'label' });
  return field(label, index);
}

export function renderPage(ui: ReactNode, initialEntries: string[] = ['/']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return {
    ...render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={initialEntries}>
          <AppThemeProvider>{ui}</AppThemeProvider>
        </MemoryRouter>
      </QueryClientProvider>
    ),
    client
  };
}
