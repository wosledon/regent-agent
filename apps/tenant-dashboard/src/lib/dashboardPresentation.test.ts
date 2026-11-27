import { beforeEach, describe, expect, it } from 'vitest';
import type { components } from './api-client/schema';
import i18n from './i18n';
import { createTenantFormatter } from './formatting';
import { domainLabelKey } from './domainLabels';
import { resolveActionCenterNextAction, resolveDashboardCardPresentation, type DashboardPresentationContext } from './dashboardPresentation';

type DashboardCardData = components['schemas']['DashboardCard'];

function context(locale: string, timeZone = 'UTC'): DashboardPresentationContext {
  return {
    t: i18n.t.bind(i18n),
    format: createTenantFormatter(locale, timeZone, i18n.t('common.notAvailable')),
    domainLabel: value => {
      const key = domainLabelKey(value);
      return key ? i18n.t(key) : value;
    }
  };
}

function card(overrides: Partial<DashboardCardData> = {}): DashboardCardData {
  return {
    id: 'test', title: 'Legacy title', summary: 'Legacy summary', metric: 'Legacy metric',
    severity: 'low', freshness: 'fresh', dataAt: '2026-11-25T12:00:00Z', href: '/test', ...overrides
  };
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('dashboard presentation resolver', () => {
  it('localizes fixed copy and formats structured budget values with the active locale', async () => {
    const budget = card({
      id: 'budget', titleKey: 'dashboardPage.card.budget.title', summaryKey: 'dashboardPage.card.budget.summary',
      summaryValues: { spentMinor: 123450, budgetMinor: 200000, currency: 'USD' },
      metricPresentation: { kind: 'percent', value: 0.61725 }
    });
    expect(resolveDashboardCardPresentation(budget, context('en-US'))).toEqual({
      title: 'Budget & cost', summary: 'USD\u00a01,234.50 of USD\u00a02,000.00 used', metric: '61.7%'
    });

    await i18n.changeLanguage('zh-CN');
    expect(resolveDashboardCardPresentation(budget, context('zh-CN'))).toEqual({
      title: '预算成本', summary: '已使用 USD\u00a01,234.50 / USD\u00a02,000.00', metric: '61.7%'
    });
  });

  it('formats independent project counts and employee capacity without parsing legacy strings', () => {
    const projects = card({
      summaryKey: 'dashboardPage.card.projects.summary', summaryValues: { count: 1234, secondaryCount: 1 },
      metricPresentation: { kind: 'count', value: 1 }
    });
    expect(resolveDashboardCardPresentation(projects, context('en-US')).summary).toBe('1,234 projects · 1 at risk');
    const organization = card({
      summaryKey: 'dashboardPage.card.organization.summary', summaryValues: { activeCount: 3, totalCount: 4 },
      metricPresentation: { kind: 'count', value: 4 }
    });
    expect(resolveDashboardCardPresentation(organization, context('en-US')).summary).toBe('Available employees: 3 / 4 employees');
  });

  it('uses tenant time zone and active locale for decision timestamps while preserving domain data', async () => {
    const decision = card({
      titleKey: 'dashboardPage.card.decisions.title', summaryKey: 'dashboardPage.card.decisions.summary',
      summaryValues: { eventType: 'approval.decided' },
      metricPresentation: { kind: 'timestamp', timestamp: '2026-11-25T12:00:00Z' }
    });
    const english = resolveDashboardCardPresentation(decision, context('en-US', 'Asia/Shanghai'));
    expect(english.summary).toBe('Decision: approval.decided');
    expect(english.metric).toContain('11/25/2026');
    expect(english.metric).toContain('08:00:00 PM');

    await i18n.changeLanguage('zh-CN');
    const chinese = resolveDashboardCardPresentation(decision, context('zh-CN', 'Asia/Shanghai'));
    expect(chinese.title).toBe('最近决策');
    expect(chinese.summary).toBe('决策：approval.decided');
    expect(chinese.metric).toContain('2026/11/25');
    expect(chinese.metric).toContain('20:00:00');
  });

  it('keeps company names and status domain values as data', async () => {
    const company = card({
      titleKey: 'dashboardPage.card.company.title', summary: 'Acme Robotics', metric: 'Active',
      metricPresentation: { kind: 'text', text: 'Active' }
    });
    expect(resolveDashboardCardPresentation(company, context('en-US'))).toEqual({
      title: 'Company status', summary: 'Acme Robotics', metric: 'Active'
    });
    await i18n.changeLanguage('zh-CN');
    expect(resolveDashboardCardPresentation(company, context('zh-CN'))).toEqual({
      title: '公司状态', summary: 'Acme Robotics', metric: '活跃'
    });
  });

  it('falls back to legacy text for missing metadata and unknown future keys', () => {
    const legacy = card();
    expect(resolveDashboardCardPresentation(legacy, context('en-US'))).toEqual({
      title: 'Legacy title', summary: 'Legacy summary', metric: 'Legacy metric'
    });
    const unknown = card({
      titleKey: 'dashboardPage.future.title', summaryKey: 'dashboardPage.future.summary',
      metricPresentation: { kind: 'future-kind', value: 42 }
    });
    expect(resolveDashboardCardPresentation(unknown, context('en-US'))).toEqual({
      title: 'Legacy title', summary: 'Legacy summary', metric: 'Legacy metric'
    });
  });

  it('localizes known action keys and falls back safely for unknown actions', async () => {
    const action: components['schemas']['ActionCenterItem'] = {
      id: 'action-1', sourceType: 'Approval', title: 'Domain approval title', severity: 'high', owner: 'Owner',
      createdAt: '2026-11-25T12:00:00Z', slaDueAt: null, requiresBossDecision: true,
      nextAction: 'Legacy action', nextActionKey: 'dashboardPage.action.approval.review', href: '/approval'
    };
    expect(resolveActionCenterNextAction(action, i18n.t.bind(i18n))).toBe('Review evidence and decide');
    await i18n.changeLanguage('zh-CN');
    expect(resolveActionCenterNextAction(action, i18n.t.bind(i18n))).toBe('审核证据并作出决策');
    expect(resolveActionCenterNextAction({ ...action, nextActionKey: 'dashboardPage.action.future' }, i18n.t.bind(i18n))).toBe('Legacy action');
  });
});
