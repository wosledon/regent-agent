import { beforeEach, describe, expect, it } from 'vitest';
import i18n from '../../lib/i18n';
import { createTenantFormatter } from '../../lib/formatting';
import { domainLabelKey } from '../../lib/domainLabels';
import type { CollaborationNode } from '../../lib/domain-types';
import { localizeCollaborationSubtitle, projectCollaborationNode, type CollaborationLocalization } from './localizedCollaboration';

function localization(locale: string): CollaborationLocalization {
  return {
    t: i18n.t.bind(i18n),
    format: createTenantFormatter(locale, 'UTC', i18n.t('common.notAvailable')),
    domainLabel: value => {
      const key = domainLabelKey(value);
      return key ? i18n.t(key) : value;
    }
  };
}

const employee: CollaborationNode = {
  id: 'employee:1', type: 'Employee', label: 'Engineering Lead', subtitle: 'EMP-001 · Engineering Lead',
  status: 'Active', employeeId: '1', depth: 0, x: 0, y: 0, memberEmployeeIds: [], activityCount: 7
};

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('collaboration display localization', () => {
  it('localizes known employee, conversation and project subtitle shapes without changing raw data', async () => {
    const conversation: CollaborationNode = {
      id: 'conversation:1', type: 'Conversation', label: 'Release room', subtitle: 'Project · 12 members · 34 messages',
      status: 'Active', conversationId: '1', depth: 0, x: 0, y: 0, memberEmployeeIds: ['1'], activityCount: 2
    };
    const project: CollaborationNode = {
      id: 'project:1', type: 'Project', label: 'Dashboard', subtitle: 'Active · 1234 tasks · Healthy',
      status: 'Healthy', projectId: '1', depth: 0, x: 0, y: 0, memberEmployeeIds: [], activityCount: 3
    };

    const projectedEmployee = projectCollaborationNode(employee, localization('en-US'));
    expect(projectedEmployee).toMatchObject({
      id: employee.id, employeeId: '1', status: 'Active', sourceSubtitle: employee.subtitle,
      subtitle: 'EMP-001 · Engineering Lead', room: 'Engineering Lead'
    });
    expect(projectCollaborationNode(conversation, localization('en-US')).subtitle).toBe('Project · 12 members · 34 messages');
    expect(projectCollaborationNode(project, localization('en-US')).subtitle).toBe('Active · 1,234 tasks · Healthy');

    await i18n.changeLanguage('zh-CN');
    expect(projectCollaborationNode(conversation, localization('zh-CN')).subtitle).toBe('项目 · 12 名成员 · 34 条消息');
    expect(projectCollaborationNode(project, localization('zh-CN')).subtitle).toBe('活跃 · 1,234 个任务 · 健康');
  });

  it('retains unknown server subtitles and the original source when projecting an already localized node', () => {
    const unknown = { ...employee, subtitle: 'SERVER_COMPOSED_DETAIL' };
    const projected = projectCollaborationNode(unknown, localization('en-US'));
    expect(projected.subtitle).toBe('SERVER_COMPOSED_DETAIL');
    expect(projected.sourceSubtitle).toBe('SERVER_COMPOSED_DETAIL');

    const localized = projectCollaborationNode(employee, localization('en-US'));
    expect(projectCollaborationNode(localized, localization('zh-CN')).sourceSubtitle).toBe(employee.subtitle);
    expect(localizeCollaborationSubtitle('Conversation', 'custom server value', localization('en-US')).subtitle).toBe('custom server value');
  });
});
