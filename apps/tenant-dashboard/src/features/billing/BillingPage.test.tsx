import { fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import i18n from '../../lib/i18n';
import { BillingPage } from './BillingPage';

const apiMocks = vi.hoisted(() => ({
  billing: vi.fn(), billingPlans: vi.fn(), checkout: vi.fn(), payLocal: vi.fn(), localization: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

const basicPlan = {
  id: 'plan-basic', name: 'Basic', monthlyAmountMinor: 10000, currency: 'USD',
  maxEmployees: 5, maxProjects: 3, maxStorageBytes: 1000, monthlyModelBudgetMinor: 5000
};
const proPlan = { ...basicPlan, id: 'plan-pro', name: 'Pro', monthlyAmountMinor: 20000, maxEmployees: 20, maxProjects: 20 };

beforeEach(async () => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
  apiMocks.billingPlans.mockResolvedValue([basicPlan, proPlan]);
  apiMocks.billing.mockResolvedValue({
    plan: basicPlan,
    subscription: {
      tenantId: 'tenant-1', planId: 'plan-basic', status: 'Active', currency: 'USD',
      currentPeriodStart: '2026-11-01T00:00:00Z', currentPeriodEnd: '2026-12-01T00:00:00Z', version: 2
    },
    quotas: [
      { resource: 'employees', used: 2, limit: 5, exceeded: false },
      { resource: 'projects', used: 4, limit: 3, exceeded: true }
    ],
    invoices: [
      { id: 'invoice-pending', tenantId: 'tenant-1', planId: 'plan-basic', amountMinor: 10000, currency: 'USD', status: 'Pending', paymentProvider: 'Local', createdAt: '2026-11-01T00:00:00Z', paidAt: null, paymentRef: null },
      { id: 'invoice-paid', tenantId: 'tenant-1', planId: 'plan-basic', amountMinor: 10000, currency: 'USD', status: 'Paid', paymentProvider: 'Local', createdAt: '2026-10-01T00:00:00Z', paidAt: '2026-10-01T00:00:00Z', paymentRef: 'local' }
    ],
    ledger: [
      { id: 'ledger-1', tenantId: 'tenant-1', invoiceId: 'invoice-paid', entryType: 'Charge', amountMinor: 10000, currency: 'USD', priceVersion: '1', createdAt: '2026-10-01T00:00:00Z' }
    ]
  });
  apiMocks.checkout.mockResolvedValue({});
  apiMocks.payLocal.mockResolvedValue({});
  await i18n.changeLanguage('en-US');
});

describe('BillingPage operations', () => {
  it('keeps plan, quota and ledger actions scoped to their rows', async () => {
    renderPage(<BillingPage />);

    expect(await screen.findByRole('heading', { name: 'Plan & Billing' })).toBeInTheDocument();
    expect(screen.getByText('Plan quotas')).toBeInTheDocument();
    expect(screen.getByText('Invoices and ledger')).toBeInTheDocument();
    const planButtons = screen.getAllByRole('button', { name: 'Select' });
    expect(planButtons[0]).toBeDisabled();
    expect(planButtons[1]).toBeEnabled();

    fireEvent.click(planButtons[1]);
    await waitFor(() => expect(apiMocks.checkout).toHaveBeenCalledWith('plan-pro'));

    fireEvent.click(screen.getByRole('button', { name: 'Pay locally' }));
    await waitFor(() => expect(apiMocks.payLocal).toHaveBeenCalledWith('invoice-pending'));
  });
});
