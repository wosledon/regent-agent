import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { Box } from '@mui/material';
import { Avatar, Button, Card, EmptyState, ErrorState, LoadingState, PageHeader, SemanticLink, Stack, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function MessagesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const conversations = useQuery({ queryKey: ['conversations'], queryFn: api.conversations });
  const employees = useQuery({ queryKey: ['employees'], queryFn: api.employees });
  const selectedId = searchParams.get('conversation') ?? conversations.data?.[0]?.id ?? '';
  const conversation = conversations.data?.find(item => item.id === selectedId);
  const messages = useQuery({
    queryKey: ['messages', selectedId],
    queryFn: () => api.messages(selectedId),
    enabled: Boolean(selectedId)
  });
  const employeeName = (id: string) => employees.data?.find(employee => employee.id === id)?.displayName ?? id;

  if (conversations.isLoading || employees.isLoading) return <LoadingState />;
  if (conversations.error || employees.error) return <ErrorState message={(conversations.error ?? employees.error)!.message} />;

  return (
    <Stack spacing={3}>
      <PageHeader
        title="Messages & collaboration"
        description="Read-only control plane view of Agent-operated groups, replies, mentions, evidence and audit-linked collaboration."
        actions={<SemanticLink href="/organization/collaboration">Open collaboration workspace</SemanticLink>}
      />
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '300px minmax(0, 1fr)' }, gap: 2, alignItems: 'start' }}>
        <Card title="Conversations" action={<StatusChip label={`${conversations.data?.length ?? 0} groups`} tone="info" />}>
          {!conversations.data?.length ? <EmptyState title="No conversations" description="Create a group from the collaboration workspace." /> : null}
          {conversations.data?.map(item => (
            <Button
              key={item.id}
              tone={item.id === selectedId ? 'primary' : 'neutral'}
              onClick={() => setSearchParams({ conversation: item.id })}
              sx={{ justifyContent: 'flex-start', width: '100%', height: 'auto', p: 1.5 }}
            >
              <Stack spacing={0} alignItems="flex-start">
                <Text variant="h3">{item.title}</Text>
                <Text variant="caption">{item.kind} · {item.memberEmployeeIds.length} members</Text>
              </Stack>
            </Button>
          ))}
        </Card>
        <Stack spacing={2}>
          {conversation ? (
            <Card title={conversation.title} action={<StatusChip label={conversation.kind} tone="warning" />}>
              <Stack direction="row" wrap spacing={2}>
                {conversation.memberEmployeeIds.map(id => (
                  <Stack key={id} direction="row" spacing={1} alignItems="center">
                    <Avatar name={employeeName(id)} size="small" />
                    <Text variant="caption">{employeeName(id)}</Text>
                  </Stack>
                ))}
              </Stack>
              <Text variant="caption">Created by {conversation.createdBy} · {conversation.scopeType} scope · {new Date(conversation.createdAt).toLocaleString()}</Text>
              <SemanticLink href={`/organization/collaboration?view=network&entityType=conversation&entityId=${conversation.id}`}>Inspect group relationships</SemanticLink>
            </Card>
          ) : null}
          <Card title="Thread" action={<StatusChip label={`${messages.data?.length ?? 0} messages`} tone="success" />}>
            {messages.isLoading ? <LoadingState /> : null}
            {messages.error ? <ErrorState message={messages.error.message} /> : null}
            {!messages.data?.length ? <EmptyState title="No messages" description="Send the first message in this group." /> : null}
            {messages.data?.map(message => (
              <Box key={message.id} sx={{ p: 1.5, borderLeft: 3, borderColor: message.priority === 'P0' ? 'error.main' : 'primary.main', bgcolor: 'background.default', borderRadius: 1 }}>
                <Stack direction="row" spacing={1} alignItems="center" wrap>
                  <Avatar name={message.sender} size="small" />
                  <Text variant="h3">{message.sender}</Text>
                  <StatusChip label={message.priority} tone={message.priority === 'P0' ? 'error' : 'warning'} />
                  <Text variant="caption">{new Date(message.createdAt).toLocaleString()} · seq {message.seq}</Text>
                </Stack>
                <Text>{message.body}</Text>
                {message.replyToMessageId ? <Text variant="caption">Reply to {message.replyToMessageId}</Text> : null}
                {message.mentionedEmployeeIds.length ? <Text variant="caption">Mentions: {message.mentionedEmployeeIds.map(employeeName).join(', ')}</Text> : null}
                {message.evidenceRefs.length ? <Text variant="caption">Evidence: {message.evidenceRefs.join(', ')}</Text> : null}
              </Box>
            ))}
          </Card>
          <Card title="Agent-operated conversation">
            <Text>Messages and replies are sent by employee Agents through <code>/agent/v1/conversations</code>. Human users observe the process here and use governance pages for high-impact decisions.</Text>
          </Card>
        </Stack>
      </Box>
    </Stack>
  );
}
