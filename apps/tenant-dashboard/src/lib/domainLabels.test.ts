import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import i18n from './i18n';
import { domainLabelKey, useDomainLabels } from './domainLabels';
import openApiSource from '../../../../contracts/openapi/controlplane.json?raw';

function openApiStringEnums(value: unknown, values = new Set<string>()): Set<string> {
  if (!value || typeof value !== 'object') return values;
  if (Array.isArray(value)) {
    for (const child of value) openApiStringEnums(child, values);
    return values;
  }
  for (const [key, child] of Object.entries(value)) {
    if (key === 'enum' && Array.isArray(child)) {
      for (const item of child) if (typeof item === 'string') values.add(item);
    } else {
      openApiStringEnums(child, values);
    }
  }
  return values;
}

const openApiEnums = openApiStringEnums(JSON.parse(openApiSource) as unknown);

const dynamicDomainLabels = [
  ['brief_ready', 'Brief ready', '简报就绪'],
  ['rate_limited', 'Rate limited', '已限流'],
  ['pending_review', 'Pending review', '待审核'],
  ['Running', 'Running', '运行中'],
  ['Invalid', 'Invalid', '无效'],
  ['Claimed', 'Claimed', '已认领'],
  ['Done', 'Completed', '已完成'],
  ['Filled', 'Filled', '已填补'],
  ['Assessed', 'Assessed', '已评估'],
  ['Offered', 'Offered', '已发出录用通知'],
  ['ReadyForReview', 'Ready for review', '待审核'],
  ['Reassigned', 'Reassigned', '已改派'],
  ['Decided', 'Decided', '已裁决'],
  ['Adjudicated', 'Adjudicated', '已裁定'],
  ['RestoreRequired', 'Restore required', '需要恢复'],
  ['Revoked', 'Revoked', '已撤销'],
  ['Purged', 'Purged', '已清除'],
  ['RestoreVerified', 'Restore verified', '恢复已验证'],
  ['RolledBack', 'Rolled back', '已回滚'],
  ['RolledBackRestore', 'Rolled back to restore point', '已回滚至恢复点'],
  ['ReadyToResume', 'Ready to resume', '可恢复'],
  ['Interrupted', 'Interrupted', '已中断'],
  ['NeedMoreContext', 'Need more context', '需要更多上下文'],
  ['Answered', 'Answered', '已答复'],
  ['Allocated', 'Allocated', '已分配'],
  ['Available', 'Available', '可用'],
  ['Used', 'Used', '已使用'],
  ['Review', 'Review', '审核'],
  ['Company', 'Company', '公司'],
  ['Position', 'Position', '岗位'],
  ['Topic', 'Topic', '主题'],
  ['Work', 'Work', '工作'],
  ['Coordination', 'Coordination', '协作'],
  ['Observability', 'Observability', '可观测性'],
  ['Chat', 'Chat', '聊天'],
  ['TaskAssignment', 'Task assignment', '任务分配'],
  ['DelegationRequest', 'Delegation request', '委派请求'],
  ['HelpRequest', 'Help request', '求助请求'],
  ['HandoffRequest', 'Handoff request', '交接请求'],
  ['ReviewRequest', 'Review request', '审核请求'],
  ['DecisionRequest', 'Decision request', '决策请求'],
  ['Discussion', 'Discussion', '讨论'],
  ['Ack', 'Acknowledged', '已确认'],
  ['Reject', 'Reject', '拒绝'],
  ['StatusUpdate', 'Status update', '状态更新'],
  ['Complete', 'Completed', '已完成']
] as const;

describe('domain labels', () => {
  it('maps server enum values to existing localized resources', async () => {
    await i18n.changeLanguage('en-US');
    expect(domainLabelKey('Active')).toBe('common.domain.active');
    expect(i18n.t(domainLabelKey('CoOwner')!)).toBe('Co-owner');
    expect(i18n.t(domainLabelKey('InProgress')!)).toBe('In progress');
    expect(i18n.t(domainLabelKey('knowledge_base')!)).toBe('Knowledge base');
    expect(domainLabelKey('Assigned')).toBe('common.domain.assigned');
    expect(domainLabelKey('Read')).toBe('common.domain.read');
    expect(domainLabelKey('technical.unknown')).toBeNull();

    await i18n.changeLanguage('zh-CN');
    expect(i18n.t(domainLabelKey('Active')!)).toBe('活跃');
    expect(i18n.t(domainLabelKey('Quarantined')!)).toBe('已隔离');
  });

  it.each([
    ['brief_ready', ['brief_ready', 'brief-ready', 'brief ready', 'briefReady', 'BriefReady']],
    ['rate_limited', ['rate_limited', 'rate-limited', 'rate limited', 'rateLimited', 'RateLimited']],
    ['pending_review', ['pending_review', 'pending-review', 'pending review', 'pendingReview', 'PendingReview']],
    ['knowledge_base', ['knowledge_base', 'knowledge-base', 'knowledge base', 'knowledgeBase', 'KnowledgeBase']],
    ['ReadyToResume', ['ReadyToResume', 'ready_to_resume', 'ready-to-resume', 'ready to resume']],
    ['CoOwner', ['CoOwner', 'co_owner', 'co-owner', 'co owner']],
    ['RestoreRequired', ['RestoreRequired', 'restore_required', 'restore-required', 'restore required']]
  ] as const)('canonicalizes enum separators and casing for %s', (value, variants) => {
    const key = domainLabelKey(value);
    expect(key).not.toBeNull();
    for (const variant of variants) expect(domainLabelKey(variant)).toBe(key);
  });

  it('maps every OpenAPI string enum through an existing localized resource', async () => {
    await i18n.changeLanguage('en-US');
    expect(openApiEnums.size).toBeGreaterThan(0);
    for (const value of openApiEnums) {
      const key = domainLabelKey(value);
      expect(key, value).not.toBeNull();
      expect(i18n.exists(key!), value).toBe(true);
    }
  });

  it.each(dynamicDomainLabels)('localizes dynamic API label %s in both languages', async (value, english, chinese) => {
    await i18n.changeLanguage('en-US');
    const key = domainLabelKey(value);
    expect(key).not.toBeNull();
    expect(i18n.t(key!)).toBe(english);

    await i18n.changeLanguage('zh-CN');
    expect(i18n.t(key!)).toBe(chinese);
  });

  it('uses localized Unknown for missing and unrecognized user-facing statuses', async () => {
    const { result } = renderHook(() => useDomainLabels());
    await act(async () => { await i18n.changeLanguage('en-US'); });
    expect(result.current(undefined)).toBe('Unknown');
    expect(result.current('technical.unknown')).toBe('Unknown');

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(result.current('technical.unknown')).toBe('未知');
  });
});
