import type { TFunction } from 'i18next';
import type { components } from './api-client/schema';
import { toFiniteNumber, type TenantFormatter } from './formatting';
import { localizedCount } from './localizedCount';

type DashboardCardData = components['schemas']['DashboardCard'];
type ActionCenterItemData = components['schemas']['ActionCenterItem'];

export interface DashboardPresentationContext {
  t: TFunction;
  format: TenantFormatter;
  domainLabel: (value: string) => string;
}

export interface ResolvedDashboardCard {
  title: string;
  summary: string;
  metric: string | null;
}

function translateWithFallback(
  t: TFunction,
  key: string,
  fallback: string,
  values: Record<string, string | number> = {}
): string {
  const translated = t(key, { ...values, defaultValue: fallback });
  return translated && translated !== key ? translated : fallback;
}

function summaryCopy(card: DashboardCardData, context: DashboardPresentationContext): string | null {
  const key = card.summaryKey;
  if (!key) return null;
  const values = card.summaryValues;
  const { t, format } = context;
  switch (key) {
    case 'dashboardPage.card.pending.empty':
    case 'dashboardPage.card.budget.multiCurrency':
    case 'dashboardPage.card.decisions.empty':
      return translateWithFallback(t, key, card.summary);
    case 'dashboardPage.card.pending.summary': {
      const count = toFiniteNumber(values?.count);
      return count === null ? null : localizedCount(t, format, key, count) || card.summary;
    }
    case 'dashboardPage.card.projects.summary': {
      const count = toFiniteNumber(values?.count);
      const riskCount = toFiniteNumber(values?.secondaryCount);
      if (count === null || riskCount === null) return null;
      return translateWithFallback(t, key, card.summary, {
        projects: localizedCount(t, format, 'dashboardPage.card.projects.projectCount', count),
        risks: localizedCount(t, format, 'dashboardPage.card.projects.riskCount', riskCount)
      });
    }
    case 'dashboardPage.card.budget.summary': {
      const spent = toFiniteNumber(values?.spentMinor);
      const budget = toFiniteNumber(values?.budgetMinor);
      const currency = values?.currency;
      if (spent === null || budget === null || !currency) return null;
      return translateWithFallback(t, key, card.summary, {
        spent: format.moneyMinor(spent, currency),
        budget: format.moneyMinor(budget, currency)
      });
    }
    case 'dashboardPage.card.organization.summary': {
      const active = toFiniteNumber(values?.activeCount);
      const total = toFiniteNumber(values?.totalCount);
      if (active === null || total === null) return null;
      return translateWithFallback(t, key, card.summary, {
        active: format.number(active),
        employees: localizedCount(t, format, 'dashboardPage.card.organization.employeeCount', total)
      });
    }
    case 'dashboardPage.card.risk.summary': {
      const count = toFiniteNumber(values?.count);
      return count === null ? null : localizedCount(t, format, key, count) || card.summary;
    }
    case 'dashboardPage.card.decisions.summary':
      return values?.eventType
        ? translateWithFallback(t, key, card.summary, { eventType: values.eventType })
        : null;
    default:
      return null;
  }
}

function metricCopy(card: DashboardCardData, context: DashboardPresentationContext): string | null {
  const metric = card.metricPresentation;
  if (!metric) return card.metric;
  const unavailable = context.t('common.notAvailable');
  let formatted: string | null = null;
  switch (metric.kind) {
    case 'count': {
      const value = toFiniteNumber(metric.value);
      if (value !== null) formatted = context.format.number(value);
      break;
    }
    case 'money': {
      const value = toFiniteNumber(metric.value);
      if (value !== null && metric.currency) formatted = context.format.moneyMinor(value, metric.currency);
      break;
    }
    case 'percent': {
      const value = toFiniteNumber(metric.value);
      if (value !== null) formatted = context.format.percent(value);
      break;
    }
    case 'timestamp':
      if (metric.timestamp) formatted = context.format.dateTime(metric.timestamp);
      break;
    case 'text':
      if (metric.text) formatted = context.domainLabel(metric.text);
      break;
    default:
      return card.metric;
  }
  return formatted && formatted !== unavailable ? formatted : card.metric;
}

export function resolveDashboardCardPresentation(
  card: DashboardCardData,
  context: DashboardPresentationContext
): ResolvedDashboardCard {
  return {
    title: card.titleKey ? translateWithFallback(context.t, card.titleKey, card.title) : card.title,
    summary: summaryCopy(card, context) ?? card.summary,
    metric: metricCopy(card, context)
  };
}

export function resolveActionCenterNextAction(action: ActionCenterItemData, t: TFunction): string {
  return action.nextActionKey ? translateWithFallback(t, action.nextActionKey, action.nextAction) : action.nextAction;
}
