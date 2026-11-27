import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import {
  Avatar, Button, Card, ContentContainer, DetailList, EmptyState, FilterBar, Icon,
  PageHeader, SemanticLink, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../app/useAuth';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { api } from '../../lib/api-client/api';
import type { ChatMessage } from '../../lib/domain-types';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';
import { localizedCount } from '../../lib/localizedCount';

export function MessagesPage() {
  const { session } = useAuth();
  const canReadChat = session?.effectiveAccess?.canReadChat === true;
  const canReadCollaboration = session?.effectiveAccess?.canReadCollaboration === true;
  const isObserver = session?.effectiveAccess?.collaborationScope === 'observer';
  const format = useTenantFormatting(canReadChat);
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const [searchParams, setSearchParams] = useSearchParams();
  const [query, setQuery] = useState('');
  const [mobileThreadOpen, setMobileThreadOpen] = useState(Boolean(searchParams.get('conversation')));
  const conversations = useQuery({ queryKey: ['conversations'], queryFn: api.conversations, enabled: canReadChat });
  const employees = useQuery({ queryKey: ['employees'], queryFn: api.employees, enabled: canReadChat });
  const requestedId = searchParams.get('conversation');
  const selectedId = requestedId === null
    ? conversations.data?.[0]?.id ?? ''
    : conversations.data?.some(item => item.id === requestedId) ? requestedId : '';
  const conversation = conversations.data?.find(item => item.id === selectedId);
  const messages = useQuery({
    queryKey: ['messages', selectedId],
    queryFn: () => api.messages(selectedId),
    enabled: canReadChat && Boolean(selectedId)
  });
  const employeeName = (id: string) => employees.data?.find(employee => employee.id === id)?.displayName ?? id;
  const filteredConversations = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    return (conversations.data ?? []).filter(item =>
      !normalizedQuery || `${item.title} ${item.kind} ${item.scopeType} ${item.createdBy}`.toLowerCase().includes(normalizedQuery));
  }, [conversations.data, query]);

  if (!canReadChat) {
    return <ContentContainer><EmptyState title={t('access.permissionDenied.title')} description={t('access.permissionDenied.description')} /></ContentContainer>;
  }
  if (conversations.isLoading || employees.isLoading) return <LocalizedLoadingState />;
  if (conversations.error || employees.error) return <LocalizedErrorState error={(conversations.error ?? employees.error)} />;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('messagesPage.messagesCollaboration')}
          description={t('messagesPage.readOnlyControlPlaneViewOfAgentOperatedGroupsRepliesMentionsEvidenceAndA')}
          actions={canReadCollaboration ? <SemanticLink href="/organization/collaboration">{t('messagesPage.openCollaborationWorkspace')}</SemanticLink> : undefined}
          meta={<StatusChip label={t(isObserver ? 'collaborationPage.observerAccess' : 'collaborationPage.participantAccess')} tone="info" />}
        />
        <Stack direction="row" wrap spacing={2} alignItems="flex-start">
          <Stack
            spacing={2}
            sx={{
              flex: '1 1 300px',
              minWidth: 0,
              display: mobileThreadOpen ? { xs: 'none', md: 'flex' } : 'flex'
            }}
          >
            <Card
              title={t('messagesPage.conversations')}
              headingLevel="h2"
              action={<StatusChip label={localizedCount(t, format, 'messagesPage.groupCount', conversations.data?.length ?? 0)} tone="info" />}
              padding="small"
            >
              {conversations.data?.length ? (
                <FilterBar>
                  <Stack sx={{ width: '100%' }}>
                    <TextInput
                      value={query}
                      onChange={setQuery}
                      placeholder={t('collaborationPage.searchPeopleGroupsProjectsOrEvents')}
                      startAdornment={<Icon name="search" />}
                      inputProps={{ 'aria-label': t('collaborationPage.searchCollaboration') }}
                    />
                  </Stack>
                </FilterBar>
              ) : null}
              {!conversations.data?.length ? (
                <EmptyState
                  title={t(isObserver ? 'messagesPage.observerEmptyTitle' : 'messagesPage.noConversations')}
                  description={t(isObserver ? 'messagesPage.observerEmptyDescription' : 'messagesPage.noConversationsDescription')}
                />
              ) : null}
              <Stack spacing={1}>
                {filteredConversations.map(item => (
                  <Button
                    key={item.id}
                    tone={item.id === selectedId ? 'primary' : 'neutral'}
                    onClick={() => {
                      setSearchParams({ conversation: item.id });
                      setMobileThreadOpen(true);
                    }}
                    aria-pressed={item.id === selectedId}
                    startIcon={<Icon name="messages" />}
                    sx={{ justifyContent: 'flex-start', textAlign: 'left', width: '100%', height: 'auto', minHeight: 64, p: 1.25 }}
                  >
                    <Stack spacing={0.25} alignItems="flex-start" sx={{ minWidth: 0 }}>
                      <Text variant="h3">{item.title}</Text>
                      <Text variant="caption">{localizedCount(t, format, 'messagesPage.members', item.memberEmployeeIds.length, { kind: domainLabel(item.kind) })}</Text>
                      <StatusChip label={domainLabel(item.scopeType)} tone="neutral" />
                    </Stack>
                  </Button>
                ))}
              </Stack>
            </Card>
          </Stack>
          <Stack
            spacing={2}
            sx={{
              flex: '2 1 620px',
              minWidth: 0,
              display: mobileThreadOpen ? 'flex' : { xs: 'none', md: 'flex' }
            }}
          >
            {conversation ? (
              <Card
                title={conversation.title}
                headingLevel="h2"
                action={<Stack direction="row" spacing={1} wrap>
                  <Button size="small" tone="neutral" onClick={() => setMobileThreadOpen(false)} sx={{ display: { xs: 'inline-flex', md: 'none' }, minWidth: 0 }} startIcon={<Icon name="arrowLeft" />}>
                    {t('messagesPage.conversations')}
                  </Button>
                  <StatusChip label={domainLabel(conversation.kind)} tone="warning" />
                </Stack>}
              >
                <Stack direction="row" spacing={1.5} wrap alignItems="center">
                  {conversation.memberEmployeeIds.map(id => (
                    <Stack key={id} direction="row" spacing={1} alignItems="center">
                      <Avatar name={employeeName(id)} size="small" />
                      <Text variant="caption">{employeeName(id)}</Text>
                    </Stack>
                  ))}
                </Stack>
                <DetailList
                  columns={2}
                  dense
                  items={[
                    { label: t('common.scope'), value: domainLabel(conversation.scopeType) },
                    { label: t('projectsPage.owner'), value: conversation.createdBy },
                    { label: t('common.created'), value: format.dateTime(conversation.createdAt), mono: true }
                  ]}
                />
                {canReadCollaboration ? <SemanticLink href={`/organization/collaboration?view=network&entityType=conversation&entityId=${conversation.id}`}>{t('messagesPage.inspectGroupRelationships')}</SemanticLink> : null}
              </Card>
            ) : null}
            <Card title={t('messagesPage.thread')} headingLevel="h2" action={<StatusChip label={localizedCount(t, format, 'messagesPage.messageCount', messages.data?.length ?? 0)} tone="success" />}>
              {!selectedId ? <EmptyState title={t('messagesPage.conversationUnavailable')} description={t('messagesPage.conversationUnavailableDescription')} />
                : messages.isLoading ? <LocalizedLoadingState />
                  : messages.error ? <LocalizedErrorState error={messages.error} onRetry={() => void messages.refetch()} />
                    : !messages.data?.length ? <EmptyState
                      title={t('messagesPage.noMessages')}
                      description={t(isObserver ? 'messagesPage.observerNoMessagesDescription' : 'messagesPage.noMessagesDescription')}
                    />
                      : messages.data.map(message => (
                        <MessageEvidenceItem key={message.id} message={message} employeeName={employeeName} />
                      ))}
            </Card>
            <Card tone="info" padding="small">
              <Stack direction="row" spacing={1.5} alignItems="flex-start">
                <Icon name="info" sx={{ mt: 0.25, color: 'info.main' }} />
                <Stack spacing={0.25}>
                  <Text variant="bodyStrong">{t('messagesPage.agentOperatedConversation')}</Text>
                  <Text variant="secondary">{t('messagesPage.agentOperatedDescription', { endpoint: '/agent/v1/conversations' })}</Text>
                </Stack>
              </Stack>
            </Card>
          </Stack>
        </Stack>
      </Stack>
    </ContentContainer>
  );
}

function MessageEvidenceItem({ message, employeeName }: { message: ChatMessage; employeeName: (id: string) => string }) {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  return (
    <Stack spacing={1} sx={{ p: 1.5, borderLeft: 3, borderColor: message.priority === 'P0' ? 'error.main' : 'primary.main', bgcolor: 'background.default', borderRadius: 1 }}>
      <Stack direction="row" spacing={1} wrap alignItems="center">
        <Avatar name={message.sender} size="small" />
        <Text variant="h3">{message.sender}</Text>
        <StatusChip label={domainLabel(message.priority)} tone={message.priority === 'P0' ? 'error' : 'warning'} />
        <Text variant="caption">{t('messagesPage.seq', { value0: format.dateTime(message.createdAt), value1: format.number(toFiniteNumber(message.seq)) })} · {domainLabel(message.messageType)}</Text>
      </Stack>
      <Text>{message.body}</Text>
      <Stack spacing={0.25}>
        {message.replyToMessageId ? <Text variant="caption">{t('messagesPage.replyTo', { value0: message.replyToMessageId })}</Text> : null}
        {message.mentionedEmployeeIds.length ? <Text variant="caption">{t('messagesPage.mentions', { value0: message.mentionedEmployeeIds.map(employeeName).join(', ') })}</Text> : null}
        {message.evidenceRefs.length ? <Text variant="mono" sx={{ overflowWrap: 'anywhere' }}>{t('messagesPage.evidence', { value0: message.evidenceRefs.join(', ') })}</Text> : null}
      </Stack>
    </Stack>
  );
}
