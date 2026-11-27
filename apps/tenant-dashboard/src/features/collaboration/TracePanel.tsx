import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Drawer } from '@mui/material';
import {
  Avatar, Card, DetailList, EmptyState, Icon, SemanticIconButton, SemanticLink,
  Stack, StatusChip, Text, Timeline
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../app/useAuth';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedFreshnessBadge } from '../common/LocalizedFreshnessBadge';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedTimelineEntry } from '../common/LocalizedTimelineEntry';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';
import { localizedCount } from '../../lib/localizedCount';
import type { ChatMessage, CollaborationNode } from '../../lib/domain-types';
import { projectCollaborationNode } from './localizedCollaboration';
import { collaborationFreshness } from './collaborationView';

interface TracePanelProps {
  node: CollaborationNode | null;
  employees: Awaited<ReturnType<typeof api.employees>>;
  onClose: () => void;
}

export function TracePanel({ node: sourceNode, employees, onClose }: TracePanelProps) {
  const { session } = useAuth();
  const canReadCollaboration = session?.effectiveAccess?.canReadCollaboration === true;
  const canReadChat = session?.effectiveAccess?.canReadChat === true;
  const isObserver = session?.effectiveAccess?.collaborationScope === 'observer';
  const format = useTenantFormatting(canReadCollaboration);
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const node = useMemo(() => sourceNode ? projectCollaborationNode(sourceNode, { t, format, domainLabel }) : null,
    [sourceNode, t, format, domainLabel]);
  const entityType = node?.type.toLowerCase() ?? '';
  const entityId = node?.employeeId ?? node?.conversationId ?? node?.projectId ?? '';
  const trace = useQuery({
    queryKey: ['collaboration', 'trace', entityType, entityId],
    queryFn: () => api.collaborationTrace(entityType, entityId),
    enabled: canReadCollaboration && Boolean(node)
  });
  const messages = useQuery({
    queryKey: ['messages', node?.conversationId],
    queryFn: () => api.messages(node!.conversationId!),
    enabled: canReadChat && node?.type === 'Conversation'
  });
  const employeeName = (id: string) => employees.find(employee => employee.id === id)?.displayName ?? id;
  const traceFreshness = collaborationFreshness(trace.data?.generatedAt);

  return (
    <Drawer
      anchor="right"
      open={Boolean(node)}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 640 }, p: 0, borderLeft: 1, borderColor: 'divider' } } }}
    >
      {node ? (
        <Stack spacing={0} sx={{ minHeight: '100%' }}>
          <Stack spacing={2} sx={{ p: 3, borderBottom: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
            <Stack direction="row" spacing={2} alignItems="flex-start">
              <Avatar name={node.label} status={node.status} size="large" />
              <Stack spacing={0.5} sx={{ flexGrow: 1, minWidth: 0 }}>
                <Text variant="h2">{node.label}</Text>
                <Text variant="caption">{node.subtitle}</Text>
                <Stack direction="row" spacing={1} wrap>
                  <StatusChip label={t('collaborationPage.nodeStatus', { type: domainLabel(node.type), status: domainLabel(node.status) })} tone="info" />
                  <StatusChip label={t(isObserver ? 'collaborationPage.observerAccess' : 'collaborationPage.participantAccess')} tone="neutral" />
                </Stack>
              </Stack>
              <SemanticIconButton label={t('collaborationPage.closeTrace')} onClick={onClose}><Icon name="close" /></SemanticIconButton>
            </Stack>
            <Stack direction="row" spacing={2} wrap>
              {node.employeeId ? <SemanticLink href={`/organization/employees/${node.employeeId}`}>{t('collaborationPage.employeeTimeline')}</SemanticLink> : null}
              {node.projectId ? <SemanticLink href={`/projects/${node.projectId}`}>{t('collaborationPage.projectDetail')}</SemanticLink> : null}
              {node.conversationId && canReadChat ? <SemanticLink href={`/messages?conversation=${node.conversationId}`}>{t('collaborationPage.openMessages')}</SemanticLink> : null}
            </Stack>
          </Stack>
          <Stack spacing={3} sx={{ p: 3 }}>
            <Card padding="small">
              <DetailList
                columns={2}
                dense
                items={[
                  { label: t('collaborationPage.nodeType'), value: domainLabel(node.type) },
                  { label: t('common.status'), value: <Stack direction="row" spacing={1} wrap><Text variant="body">{domainLabel(node.status)}</Text>{trace.data ? <LocalizedFreshnessBadge freshness={traceFreshness} /> : null}</Stack> },
                  { label: t('collaborationPage.traceableEvents'), value: format.number(node.activityCount) }
                ]}
              />
            </Card>
            {node.type === 'Conversation' ? (
              <Card title={t('collaborationPage.groupMembers')} headingLevel="h2" action={<StatusChip label={localizedCount(t, format, 'collaborationPage.memberCount', node.memberEmployeeIds.length)} tone="warning" />}>
                <Stack direction="row" wrap spacing={2}>
                  {node.memberEmployeeIds.map(id => (
                    <Stack key={id} direction="row" spacing={1} alignItems="center">
                      <Avatar name={employeeName(id)} size="small" />
                      <Text variant="caption">{employeeName(id)}</Text>
                    </Stack>
                  ))}
                </Stack>
                <Text variant="caption">{t('collaborationPage.membershipIsManagedByEmployeeAgentsTheControlPlaneOnlyDisplaysTheCurrent')}</Text>
              </Card>
            ) : null}
            {node.type === 'Conversation' && canReadChat ? (
              <Card title={t('collaborationPage.conversation')} headingLevel="h2" action={<StatusChip label={localizedCount(t, format, 'messagesPage.messageCount', messages.data?.length ?? 0)} tone="info" />}>
                {messages.isLoading ? <LocalizedLoadingState />
                  : messages.error ? <LocalizedErrorState error={messages.error} onRetry={() => void messages.refetch()} />
                    : !messages.data?.length ? <EmptyState
                      title={t('collaborationPage.noMessages')}
                      description={t(isObserver ? 'collaborationPage.observerNoMessagesDescription' : 'collaborationPage.noMessagesDescription')}
                    />
                      : messages.data.map(message => (
                        <TraceMessageItem key={message.id} message={message} employeeName={employeeName} />
                      ))}
              </Card>
            ) : null}
            <Card
              title={t('collaborationPage.traceableProcess')}
              headingLevel="h2"
              action={<Stack direction="row" spacing={1} wrap>
                <StatusChip label={localizedCount(t, format, 'collaborationPage.events', trace.data?.activities.length ?? 0)} tone="success" />
                {trace.data ? <LocalizedFreshnessBadge freshness={traceFreshness} /> : null}
              </Stack>}
            >
              {trace.isLoading ? <LocalizedLoadingState />
                : trace.error ? <LocalizedErrorState error={trace.error} onRetry={() => void trace.refetch()} />
                  : !trace.data?.activities.length ? <EmptyState title={t('collaborationPage.noLinkedEvents')} description={t('collaborationPage.newWorkMessagesAndAuditsWillAppearHere')} />
                    : <Timeline>
                      {trace.data.activities.map(activity => (
                        <LocalizedTimelineEntry
                          key={activity.id}
                          timestampLabel={format.dateTime(activity.occurredAt)}
                          title={activity.title}
                          description={[
                            t('collaborationPage.traceDescription', {
                              actor: activity.actor, actorType: domainLabel(activity.actorType), sourceType: domainLabel(activity.sourceType), sourceId: activity.sourceId
                            }),
                            activity.taskId ? `${t('common.task')}: ${activity.taskId}` : ''
                          ].filter(Boolean).join(' · ')}
                          status={domainLabel(activity.type)}
                          nextAction={activity.evidenceRefs.length
                            ? t('collaborationPage.evidence', { value0: activity.evidenceRefs.join(', ') })
                            : activity.auditHash ? t('collaborationPage.auditHashLabel', { value0: activity.auditHash }) : undefined}
                          tone={activity.type === 'Audit' ? 'neutral' : activity.type === 'Message' ? 'warning' : 'info'}
                        />
                      ))}
                    </Timeline>}
            </Card>
          </Stack>
        </Stack>
      ) : null}
    </Drawer>
  );
}

function TraceMessageItem({ message, employeeName }: { message: ChatMessage; employeeName: (id: string) => string }) {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  return (
    <Stack spacing={1} sx={{ p: 1.5, borderLeft: 3, borderColor: message.priority === 'P0' ? 'error.main' : 'primary.main', bgcolor: 'background.default', borderRadius: 1 }}>
      <Stack direction="row" spacing={1} wrap alignItems="center">
        <Avatar name={message.sender} size="small" />
        <Text variant="h3">{message.sender}</Text>
        <StatusChip label={domainLabel(message.priority)} tone={message.priority === 'P0' ? 'error' : 'warning'} />
        <Text variant="caption">{t('collaborationPage.seq', { value0: format.dateTime(message.createdAt), value1: domainLabel(message.messageType), value2: format.number(toFiniteNumber(message.seq)) })}</Text>
      </Stack>
      <Text>{message.body}</Text>
      <Stack spacing={0.25}>
        {message.replyToMessageId ? <Text variant="caption">{t('collaborationPage.replyTo', { value0: message.replyToMessageId })}</Text> : null}
        {message.mentionedEmployeeIds.length ? <Text variant="caption">{t('collaborationPage.mentions', { value0: message.mentionedEmployeeIds.map(employeeName).join(', ') })}</Text> : null}
        {message.evidenceRefs.length ? <Text variant="mono" sx={{ overflowWrap: 'anywhere' }}>{t('collaborationPage.evidence', { value0: message.evidenceRefs.join(', ') })}</Text> : null}
      </Stack>
    </Stack>
  );
}
