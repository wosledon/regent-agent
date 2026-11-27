import { screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import i18n from '../../lib/i18n';
import { BudgetPage } from './BudgetPage';

const apiMocks = vi.hoisted(() => ({ projects: vi.fn(), localization: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(async () => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
  apiMocks.projects.mockResolvedValue([
    { id: 'project-usd-1', name: 'USD project one', stage: 'active', health: 'healthy', owner: 'Owner', targetAt: '2026-12-01T00:00:00Z', budgetMinor: 10000, spentMinor: 2500, currency: 'USD', freshness: 'fresh' },
    { id: 'project-usd-2', name: 'USD project two', stage: 'active', health: 'degraded', owner: 'Owner', targetAt: '2026-12-01T00:00:00Z', budgetMinor: 5000, spentMinor: 6000, currency: 'USD', freshness: 'stale' },
    { id: 'project-eur-1', name: 'EUR project', stage: 'active', health: 'healthy', owner: 'Owner', targetAt: '2026-12-01T00:00:00Z', budgetMinor: 20000, spentMinor: 10000, currency: 'EUR', freshness: 'fresh' }
  ]);
  await i18n.changeLanguage('en-US');
});

describe('BudgetPage currency grouping', () => {
  it('keeps totals separated by original currency and exposes project drill-down', async () => {
    renderPage(<BudgetPage />);

    expect(await screen.findByRole('heading', { name: 'USD' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'EUR' })).toBeInTheDocument();
    expect(screen.getByText('USD 150.00')).toBeInTheDocument();
    expect(screen.getByText('USD 85.00')).toBeInTheDocument();
    expect(screen.getByText('USD 65.00')).toBeInTheDocument();
    expect(screen.getAllByText('EUR 200.00').length).toBeGreaterThan(0);
    expect(screen.getAllByText('EUR 100.00').length).toBeGreaterThan(0);
    expect(screen.queryByText('USD 350.00')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'USD project one' })).toHaveAttribute('href', '/projects/project-usd-1');
    expect(screen.getByRole('link', { name: 'EUR project' })).toHaveAttribute('href', '/projects/project-eur-1');
  });
});
