import type { TFunction } from 'i18next';
import type { CollaborationNode } from '../../lib/domain-types';
import type { TenantFormatter } from '../../lib/formatting';
import { localizedCount } from '../../lib/localizedCount';

export interface CollaborationDisplayNode extends CollaborationNode {
  sourceSubtitle: string;
  room: string;
}

export interface CollaborationLocalization {
  t: TFunction;
  format: TenantFormatter;
  domainLabel: (value: string) => string;
}

export function localizeCollaborationSubtitle(
  type: string,
  sourceSubtitle: string,
  { t, format, domainLabel }: CollaborationLocalization
): { subtitle: string; room: string } {
  const parts = sourceSubtitle.split('·').map(part => part.trim());
  if (type === 'Employee' && parts.length === 2 && parts.every(Boolean)) {
    return {
      subtitle: t('collaborationPage.employeeSubtitle', { employeeNo: parts[0], position: parts[1] }),
      room: parts[1]
    };
  }

  const conversation = /^([^·]+?)\s*·\s*(\d+)\s+members?\s*·\s*(\d+)\s+messages?$/i.exec(sourceSubtitle);
  if (type === 'Conversation' && conversation) {
    const [, kind, memberCount, messageCount] = conversation;
    return {
      subtitle: t('collaborationPage.conversationSubtitle', {
        kind: domainLabel(kind),
        members: localizedCount(t, format, 'collaborationPage.memberCount', Number(memberCount)),
        messages: localizedCount(t, format, 'messagesPage.messageCount', Number(messageCount))
      }),
      room: t('collaborationPage.unassignedRoom')
    };
  }

  const project = /^([^·]+?)\s*·\s*(\d+)\s+tasks?\s*·\s*([^·]+)$/i.exec(sourceSubtitle);
  if (type === 'Project' && project) {
    const [, stage, taskCount, health] = project;
    return {
      subtitle: t('collaborationPage.projectSubtitle', {
        stage: domainLabel(stage),
        tasks: localizedCount(t, format, 'collaborationPage.taskCount', Number(taskCount)),
        health: domainLabel(health.trim())
      }),
      room: t('collaborationPage.unassignedRoom')
    };
  }

  return { subtitle: sourceSubtitle, room: t('collaborationPage.unassignedRoom') };
}

export function projectCollaborationNode(node: CollaborationNode, localization: CollaborationLocalization): CollaborationDisplayNode {
  const sourceSubtitle = 'sourceSubtitle' in node && typeof node.sourceSubtitle === 'string' ? node.sourceSubtitle : node.subtitle;
  const { subtitle, room } = localizeCollaborationSubtitle(node.type, sourceSubtitle, localization);
  return { ...node, subtitle, sourceSubtitle, room };
}
