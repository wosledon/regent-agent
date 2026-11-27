import { act, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import i18n from '../../lib/i18n';
import { ActionCenterPage } from './ActionCenterPage';

const apiMocks = vi.hoisted(() => ({ dashboard: vi.fn(), localization: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  apiMocks.dashboard.mockReset();
  apiMocks.localization.mockReset();
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
  apiMocks.dashboard.mockResolvedValue({
    companyName: 'Acme', companyStatus: 'Active', generatedAt: '2026-11-25T12:00:00Z', cards: [],
    actions: [
      {
        id: 'action-1', sourceType: 'Approval', title: 'Production release approval', severity: 'high', owner: 'Owner',
        createdAt: '2026-11-25T12:00:00Z', slaDueAt: null, requiresBossDecision: true,
        nextAction: 'Review evidence and decide', nextActionKey: 'dashboardPage.action.approval.review', href: '/approval'
      },
      {
        id: 'action-2', sourceType: 'Project', title: 'Blocked migration', severity: 'medium', owner: 'Lead',
        createdAt: '2026-11-25T12:00:00Z', slaDueAt: null, requiresBossDecision: false,
        nextAction: 'Future server action', nextActionKey: 'dashboardPage.action.future', href: '/project'
      }
    ]
  });
});

describe('ActionCenterPage localized next actions', () => {
  it('renders localized fixed actions in both languages and preserves domain titles', async () => {
    renderPage(<ActionCenterPage />);
    expect(await screen.findByText('Review evidence and decide')).toBeInTheDocument();
    expect(screen.getByText('Future server action')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Production release approval' })).toBeInTheDocument();

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByText('审核证据并作出决策')).toBeInTheDocument();
    expect(screen.getByText('Future server action')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Production release approval' })).toBeInTheDocument();
  });
});
