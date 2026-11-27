import { act, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import i18n from '../../lib/i18n';
import { DashboardPage } from './DashboardPage';

const apiMocks = vi.hoisted(() => ({ dashboard: vi.fn(), localization: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  apiMocks.dashboard.mockReset();
  apiMocks.localization.mockReset();
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'Asia/Shanghai', currency: 'USD', version: 1 });
  apiMocks.dashboard.mockResolvedValue({
    companyName: 'Acme Robotics', companyStatus: 'Active', generatedAt: '2026-11-25T12:00:00Z', actions: [],
    cards: [
      {
        id: 'company', title: '公司状态', summary: 'Acme Robotics', metric: 'Active', severity: 'low', freshness: 'fresh',
        dataAt: '2026-11-25T12:00:00Z', href: '/', titleKey: 'dashboardPage.card.company.title',
        metricPresentation: { kind: 'text', text: 'Active' }
      },
      {
        id: 'budget', title: '预算成本', summary: '已使用 123450 / 200000 USD', metric: '61.7%', severity: 'low', freshness: 'fresh',
        dataAt: '2026-11-25T12:00:00Z', href: '/budget', titleKey: 'dashboardPage.card.budget.title',
        summaryKey: 'dashboardPage.card.budget.summary', summaryValues: { spentMinor: 123450, budgetMinor: 200000, currency: 'USD' },
        metricPresentation: { kind: 'percent', value: 0.61725 }
      },
      {
        id: 'decisions', title: '最近决策', summary: 'approval.decided', metric: '2026-11-25 12:00:00Z', severity: 'low', freshness: 'fresh',
        dataAt: '2026-11-25T12:00:00Z', href: '/journal', titleKey: 'dashboardPage.card.decisions.title',
        summaryKey: 'dashboardPage.card.decisions.summary', summaryValues: { eventType: 'approval.decided' },
        metricPresentation: { kind: 'timestamp', timestamp: '2026-11-25T12:00:00Z' }
      }
    ]
  });
});

describe('DashboardPage localized presentation', () => {
  it('renders en-US fixed copy, Intl budget values and tenant-time-zone decision timestamps', async () => {
    renderPage(<DashboardPage />);
    expect(await screen.findByRole('heading', { name: 'Acme Robotics' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Company status' })).toBeInTheDocument();
    expect(screen.getByText('USD 1,234.50 of USD 2,000.00 used')).toBeInTheDocument();
    expect(screen.getByText('61.7%')).toBeInTheDocument();
    expect(screen.getByText('Decision: approval.decided')).toBeInTheDocument();
    expect((await screen.findAllByText(/11\/25\/2026.*08:00:00 PM/)).length).toBeGreaterThan(0);
  });

  it('renders zh-CN fixed copy without translating company names or audit event types', async () => {
    renderPage(<DashboardPage />);
    await screen.findByRole('heading', { name: 'Acme Robotics' });
    await act(async () => { await i18n.changeLanguage('zh-CN'); });

    expect(screen.getByRole('heading', { name: '公司状态' })).toBeInTheDocument();
    expect(screen.getByText('已使用 USD 1,234.50 / USD 2,000.00')).toBeInTheDocument();
    expect(screen.getByText('决策：approval.decided')).toBeInTheDocument();
    expect((await screen.findAllByText(/2026\/11\/25.*20:00:00/)).length).toBeGreaterThan(0);
    expect(screen.getByRole('heading', { name: 'Acme Robotics' })).toBeInTheDocument();
  });
});
