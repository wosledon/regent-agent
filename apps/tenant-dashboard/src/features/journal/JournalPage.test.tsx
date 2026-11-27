import { act, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import i18n from '../../lib/i18n';
import { JournalPage } from './JournalPage';

const apiMocks = vi.hoisted(() => ({ dashboard: vi.fn(), localization: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  apiMocks.dashboard.mockReset();
  apiMocks.localization.mockReset();
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'Asia/Shanghai', currency: 'USD', version: 1 });
  apiMocks.dashboard.mockResolvedValue({ cards: [{ id: 'decisions', summary: 'Approved release', metric: '1 decision' }] });
});

describe('JournalPage storage wording', () => {
  it('does not claim a fixed SQLite store', async () => {
    renderPage(<JournalPage />);
    expect(await screen.findByText('Hash chain enabled · backend audit event store')).toBeInTheDocument();
    expect(screen.queryByText(/SQLite/)).not.toBeInTheDocument();
  });

  it('localizes decision copy and timestamp while retaining legacy fallback', async () => {
    apiMocks.dashboard.mockResolvedValue({ cards: [{
      id: 'decisions', title: '最近决策', summary: 'approval.decided', metric: '2026-11-25 12:00:00Z',
      severity: 'low', freshness: 'fresh', dataAt: '2026-11-25T12:00:00Z', href: '/journal',
      titleKey: 'dashboardPage.card.decisions.title', summaryKey: 'dashboardPage.card.decisions.summary',
      summaryValues: { eventType: 'approval.decided' },
      metricPresentation: { kind: 'timestamp', timestamp: '2026-11-25T12:00:00Z' }
    }] });
    const { client } = renderPage(<JournalPage />);
    expect(await screen.findByText('Decision: approval.decided')).toBeInTheDocument();
    expect(screen.getByText(/11\/25\/2026.*08:00:00 PM/)).toBeInTheDocument();

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByText('决策：approval.decided')).toBeInTheDocument();
    expect(screen.getByText(/2026\/11\/25.*20:00:00/)).toBeInTheDocument();

    apiMocks.dashboard.mockResolvedValue({ cards: [{ id: 'decisions', summary: 'Legacy decision', metric: 'Legacy timestamp' }] });
    await act(async () => { await client.refetchQueries({ queryKey: ['dashboard'] }); });
    expect(await screen.findByText('Legacy decision')).toBeInTheDocument();
    expect(screen.getByText('Legacy timestamp')).toBeInTheDocument();
  });
});
