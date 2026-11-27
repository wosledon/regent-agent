import { beforeEach, describe, expect, it } from 'vitest';
import i18n from './i18n';
import { createTenantFormatter } from './formatting';
import { localizedCount } from './localizedCount';

const countCases = [
  ['dashboardPage.card.pending.summary', '1 approval needs your decision', '2 approvals need your decision', '1 项审批需要你的决策', '2 项审批需要你的决策'],
  ['dashboardPage.card.projects.projectCount', '1 project', '2 projects', '1 个项目', '2 个项目'],
  ['dashboardPage.card.projects.riskCount', '1 at risk', '2 at risk', '1 个存在风险', '2 个存在风险'],
  ['dashboardPage.card.organization.employeeCount', '1 employee', '2 employees', '1 名员工', '2 名员工'],
  ['dashboardPage.card.risk.summary', '1 high-risk notification', '2 high-risk notifications', '1 条高风险通知', '2 条高风险通知'],
  ['messagesPage.groupCount', '1 group', '2 groups', '1 个群组', '2 个群组'],
  ['messagesPage.messageCount', '1 message', '2 messages', '1 条消息', '2 条消息'],
  ['messagesPage.members', 'Group · 1 member', 'Group · 2 members', '群组 · 1 名成员', '群组 · 2 名成员', { kind: 'Group' }],
  ['collaborationPage.taskCount', '1 task', '2 tasks', '1 个任务', '2 个任务'],
  ['collaborationPage.activeCount', '1 active item', '2 active items', '1 个活跃项', '2 个活跃项'],
  ['collaborationPage.employeeCount', '1 employee', '2 employees', '1 名员工', '2 名员工'],
  ['collaborationPage.seatCount', '1 seat', '2 seats', '1 个席位', '2 个席位'],
  ['collaborationPage.groupCount', '1 group', '2 groups', '1 个群组', '2 个群组'],
  ['collaborationPage.projectCount', '1 project', '2 projects', '1 个项目', '2 个项目'],
  ['collaborationPage.memberCount', '1 member', '2 members', '1 名成员', '2 名成员'],
  ['collaborationPage.coordinationRequests', '1 coordination request', '2 coordination requests', '1 个协作请求', '2 个协作请求'],
  ['collaborationPage.delegationCount', '1 delegation', '2 delegations', '1 个委派', '2 个委派'],
  ['collaborationPage.handoffCount', '1 handoff', '2 handoffs', '1 个交接', '2 个交接'],
  ['collaborationPage.interruptCount', '1 interrupt', '2 interrupts', '1 次打断', '2 次打断'],
  ['collaborationPage.events', '1 event', '2 events', '1 个事件', '2 个事件'],
  ['collaborationPage.traceableEvents2', 'Active · 1 traceable event', 'Active · 2 traceable events', '活跃 · 1 条可追踪事件', '活跃 · 2 条可追踪事件', { status: 'Active' }],
  ['deliveryPage.acceptanceReportCount', '1 acceptance report', '2 acceptance reports', '1 份验收报告', '2 份验收报告'],
  ['deliveryPage.doneEvidenceRecordCount', '1 DoneEvidence record', '2 DoneEvidence records', '1 条 DoneEvidence 记录', '2 条 DoneEvidence 记录'],
  ['deliveryPage.findingCount', '1 finding', '2 findings', '1 条研究发现', '2 条研究发现'],
  ['deliveryPage.reviewCount', '1 review', '2 reviews', '1 次审核', '2 次审核'],
  ['evaluationsPage.cases', '1 case', '2 cases', '1 个用例', '2 个用例'],
  ['organizationPage.employeesInTheCurrentTenant', 'The current tenant has 1 employee.', 'The current tenant has 2 employees.', '当前租户有 1 名员工。', '当前租户有 2 名员工。'],
  ['governancePage.deletionProofsRetained', '1 deletion proof retained.', '2 deletion proofs retained.', '保留 1 份删除证明。', '保留 2 份删除证明。'],
  ['runtimePolicyPage.unallocatedEntries', '1 unallocated entry', '2 unallocated entries', '1 条未归属账目', '2 条未归属账目']
] as const;

const cases = countCases.map(([key, enOne, enOther, zhOne, zhOther, values]) => ({
  key, enOne, enOther, zhOne, zhOther, values
}));

function formatter(locale: string) {
  return createTenantFormatter(locale, 'UTC', i18n.t('common.notAvailable'));
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('localized counts', () => {
  it.each(cases)('selects English singular and plural for $key', ({ key, enOne, enOther, values }) => {
    const format = formatter('en-US');
    expect(localizedCount(i18n.t.bind(i18n), format, key, 1, values)).toBe(enOne);
    expect(localizedCount(i18n.t.bind(i18n), format, key, 2, values)).toBe(enOther);
  });

  it.each(cases)('keeps natural Chinese count copy for $key', async ({ key, zhOne, zhOther, values }) => {
    await i18n.changeLanguage('zh-CN');
    const format = formatter('zh-CN');
    const localizedValues: Record<string, string | number> | undefined = key === 'messagesPage.members'
      ? { kind: i18n.t('common.domain.group') }
      : key === 'collaborationPage.traceableEvents2'
        ? { status: i18n.t('common.domain.active') }
        : values;
    expect(localizedCount(i18n.t.bind(i18n), format, key, 1, localizedValues)).toBe(zhOne);
    expect(localizedCount(i18n.t.bind(i18n), format, key, 2, localizedValues)).toBe(zhOther);
  });

  it('uses the raw count for plural selection and Intl only for display', () => {
    expect(localizedCount(i18n.t.bind(i18n), formatter('en-US'), 'messagesPage.groupCount', 1234)).toBe('1,234 groups');
  });
});
