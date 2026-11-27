import { beforeEach, describe, expect, it } from 'vitest';
import i18n from './i18n';
import { createOpsFormatter } from './formatting';
import { localizedCount } from './localizedCount';

interface CountCase {
  key: string;
  en: readonly [string, string];
  zh: readonly [string, string];
}

const countCases: readonly CountCase[] = [
  { key: 'overviewPage.tenants', en: ['1 tenant', '2 tenants'], zh: ['1 个租户', '2 个租户'] },
  { key: 'overviewPage.activeGrants', en: ['1 active support grant', '2 active support grants'], zh: ['1 个活跃支持授权', '2 个活跃支持授权'] },
  { key: 'overviewPage.pendingApprovals', en: ['1 pending approval', '2 pending approvals'], zh: ['1 个待审批', '2 个待审批'] },
  { key: 'supportPage.grantCount', en: ['1 access request', '2 access requests'], zh: ['1 个访问请求', '2 个访问请求'] },
  { key: 'backupsPage.componentCount', en: ['1 component', '2 components'], zh: ['1 个组件', '2 个组件'] },
  { key: 'statusPage.componentCount', en: ['1 component', '2 components'], zh: ['1 个组件', '2 个组件'] },
  { key: 'statusPage.incidentCount', en: ['1 published incident', '2 published incidents'], zh: ['1 个已发布事件', '2 个已发布事件'] },
  { key: 'observabilityPage.alertCount', en: ['1 active alert', '2 active alerts'], zh: ['1 个活跃告警', '2 个活跃告警'] }
];

function pluralResourceKeys(value: unknown, prefix = ''): string[] {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return [];
  const entries = Object.entries(value as Record<string, unknown>);
  return entries.flatMap(([key, child]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    if (key.endsWith('_one') && entries.some(([sibling]) => sibling === `${key.slice(0, -4)}_other`)) {
      return [path.slice(0, -4)];
    }
    return pluralResourceKeys(child, path);
  });
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('localized Ops counts', () => {
  it('covers every visible plural resource', () => {
    const keys = pluralResourceKeys(i18n.getResourceBundle('en-US', 'translation')).sort();
    expect(keys).toEqual(countCases.map(testCase => testCase.key).sort());
  });

  it.each(countCases)('formats $key for singular and plural counts in both languages', async testCase => {
    const english = createOpsFormatter('en-US');
    expect(localizedCount(i18n.t, english, testCase.key, 1)).toBe(testCase.en[0]);
    expect(localizedCount(i18n.t, english, testCase.key, 2)).toBe(testCase.en[1]);

    await i18n.changeLanguage('zh-CN');
    const chinese = createOpsFormatter('zh-CN');
    expect(localizedCount(i18n.t, chinese, testCase.key, 1)).toBe(testCase.zh[0]);
    expect(localizedCount(i18n.t, chinese, testCase.key, 2)).toBe(testCase.zh[1]);
  });
});
