import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Box, Drawer, IconButton } from '@mui/material';
import { Avatar, Button, Card, EmptyState, ErrorState, LoadingState, Select, SemanticLink, Stack, StatusChip, Text, Timeline, TimelineEntry } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import type { CollaborationNode } from '../../lib/domain-types';

interface TracePanelProps {
  node: CollaborationNode | null;
  employees: Awaited<ReturnType<typeof api.employees>>;
  onClose: () => void;
}

export function TracePanel({ node, employees, onClose }: TracePanelProps) {
  const queryClient = useQueryClient();
  const [memberId, setMemberId] = useState('');
  const entityType = node?.type.toLowerCase() ?? '';
  const entityId = node?.employeeId ?? node?.conversationId ?? node?.projectId ?? '';
  const trace = useQuery({
    queryKey: ['collaboration', 'trace', entityType, entityId],
    queryFn: () => api.collaborationTrace(entityType, entityId),
    enabled: Boolean(node)
  });
  const messages = useQuery({
    queryKey: ['messages', node?.conversationId],
    queryFn: () => api.messages(node!.conversationId!),
    enabled: node?.type === 'Conversation'
  });
  const addMember = useMutation({
    mutationFn: () => api.addConversationMember(node!.conversationId!, memberId),
    onSuccess: async () => {
      setMemberId('');
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['collaboration'] }),
        queryClient.invalidateQueries({ queryKey: ['conversations'] }),
        queryClient.invalidateQueries({ queryKey: ['messages', node?.conversationId] })
      ]);
    }
  });
  const employeeName = (id: string) => employees.find(employee => employee.id === id)?.displayName ?? id;

  return (
    <Drawer anchor="right" open={Boolean(node)} onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 520 }, p: 3 } } }}>
      {node ? (
        <Stack spacing={3}>
          <Stack direction="row" spacing={2} alignItems="flex-start">
            {node.type === 'Employee' ? <Avatar name={node.label} status={node.status} size="large" /> : null}
            <Stack spacing={0.5} sx={{ flexGrow: 1 }}>
              <Text variant="h2">{node.label}</Text>
              <Text variant="caption">{node.subtitle}</Text>
              <StatusChip label={`${node.type} · ${node.status}`} tone="info" />
            </Stack>
            <IconButton aria-label="Close trace" onClick={onClose}>×</IconButton>
          </Stack>
          <Stack direction="row" spacing={2}>
            {node.employeeId ? <SemanticLink href={`/organization/employees/${node.employeeId}`}>Employee timeline</SemanticLink> : null}
            {node.projectId ? <SemanticLink href={`/projects/${node.projectId}`}>Project detail</SemanticLink> : null}
            {node.conversationId ? <SemanticLink href={`/messages?conversation=${node.conversationId}`}>Open messages</SemanticLink> : null}
          </Stack>
          {node.type === 'Conversation' ? (
            <Card title="Group members" action={<StatusChip label={`${node.memberEmployeeIds.length} members`} tone="warning" />}>
              <Stack direction="row" wrap spacing={2}>
                {node.memberEmployeeIds.map(id => (
                  <Stack key={id} direction="row" spacing={1} alignItems="center">
                    <Avatar name={employeeName(id)} size="small" />
                    <Text variant="caption">{employeeName(id)}</Text>
                  </Stack>
                ))}
              </Stack>
              <Stack direction="row" spacing={1} alignItems="flex-end">
                <Box sx={{ flexGrow: 1 }}>
                  <Select label="Add member" value={memberId} onChange={setMemberId} options={[
                    { value: '', label: 'Select employee' },
                    ...employees.filter(employee => !node.memberEmployeeIds.includes(employee.id)).map(employee => ({ value: employee.id, label: employee.displayName }))
                  ]} />
                </Box>
                <Button tone="secondary" disabled={!memberId} loading={addMember.isPending} onClick={() => addMember.mutate()}>Add</Button>
              </Stack>
            </Card>
          ) : null}
          {node.type === 'Conversation' ? (
            <Card title="Conversation" action={<StatusChip label={`${messages.data?.length ?? 0} messages`} tone="info" />}>
              {messages.isLoading ? <LoadingState /> : null}
              {messages.error ? <ErrorState message={messages.error.message} /> : null}
              {!messages.data?.length ? <EmptyState title="No messages" description="Send a message from the messages page." /> : null}
              {messages.data?.map(message => (
                <Box key={message.id} sx={{ p: 1.5, borderLeft: 3, borderColor: message.priority === 'P0' ? 'error.main' : 'primary.main', bgcolor: 'background.default', borderRadius: 1 }}>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Avatar name={message.sender} size="small" />
                    <Text variant="h3">{message.sender}</Text>
                    <StatusChip label={message.priority} tone={message.priority === 'P0' ? 'error' : 'warning'} />
                  </Stack>
                  <Text>{message.body}</Text>
                  <Text variant="caption">{new Date(message.createdAt).toLocaleString()} · {message.messageType} · seq {message.seq}</Text>
                  {message.replyToMessageId ? <Text variant="caption">Reply to {message.replyToMessageId}</Text> : null}
                  {message.mentionedEmployeeIds.length ? <Text variant="caption">Mentions: {message.mentionedEmployeeIds.map(employeeName).join(', ')}</Text> : null}
                  {message.evidenceRefs.length ? <Text variant="caption">Evidence: {message.evidenceRefs.join(', ')}</Text> : null}
                </Box>
              ))}
            </Card>
          ) : null}
          <Card title="Traceable process" action={<StatusChip label={`${trace.data?.activities.length ?? 0} events`} tone="success" />}>
            {trace.isLoading ? <LoadingState /> : null}
            {trace.error ? <ErrorState message={trace.error.message} /> : null}
            {!trace.data?.activities.length ? <EmptyState title="No linked events" description="New work, messages and audits will appear here." /> : null}
            <Timeline>
              {trace.data?.activities.map(activity => (
                <TimelineEntry
                  key={activity.id}
                  timestamp={activity.occurredAt}
                  title={activity.title}
                  description={`${activity.actor} · ${activity.sourceType}:${activity.sourceId}`}
                  status={activity.type}
                  nextAction={activity.evidenceRefs.length ? `Evidence: ${activity.evidenceRefs.join(', ')}` : activity.auditHash ? `Audit hash: ${activity.auditHash}` : undefined}
                  tone={activity.type === 'Audit' ? 'neutral' : activity.type === 'Message' ? 'warning' : 'info'}
                />
              ))}
            </Timeline>
          </Card>
        </Stack>
      ) : null}
    </Drawer>
  );
}
