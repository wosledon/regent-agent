import { screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import { OrganizationPage } from './OrganizationPage';

const apiMocks = vi.hoisted(() => ({
  charter: vi.fn(), charterTemplates: vi.fn(), positions: vi.fn(), employees: vi.fn(), tenants: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.charter.mockResolvedValue({ mission: 'Build', constraints: [], monthlyBudgetMinor: 100, currency: 'USD', governance: {}, version: 1 });
  apiMocks.charterTemplates.mockRejectedValue(new Error('Templates unavailable'));
  apiMocks.positions.mockResolvedValue([]);
  apiMocks.employees.mockResolvedValue([]);
  apiMocks.tenants.mockResolvedValue([]);
});

describe('OrganizationPage charter-template isolation', () => {
  it('keeps organization content available when only templates fail', async () => {
    renderPage(<OrganizationPage />);
    expect(await screen.findByText('Templates unavailable')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Organization' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Create tenant' })).toBeInTheDocument();
    expect(screen.getByDisplayValue('Build')).toBeInTheDocument();
  });
});
