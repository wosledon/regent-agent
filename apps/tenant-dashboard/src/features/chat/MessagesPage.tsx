import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, EmptyState, ErrorState, LoadingState, Text, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function MessagesPage() {
  const queryClient = useQueryClient();
  const [selected, setSelected] = useState('general');
  const [body, setBody] = useState('');
  const conversations = useQuery({ queryKey: ['conversations'], queryFn: api.conversations });
  const messages = useQuery({ queryKey: ['messages', selected], queryFn: () => api.messages(selected) });
  const send = useMutation({
    mutationFn: () => api.sendMessage(selected, body),
    onSuccess: () => {
      setBody('');
      return queryClient.invalidateQueries({ queryKey: ['messages', selected] });
    }
  });
  if (conversations.isLoading) return <LoadingState />;
  if (conversations.error) return <ErrorState message={conversations.error.message} />;
  return (
    <div>
      <Text variant="h1">Messages</Text>
      <Card title="Conversations">
        {conversations.data?.map(conversation => (
          <Button key={conversation.id} tone="neutral" onClick={() => setSelected(conversation.id)}>{conversation.title}</Button>
        ))}
      </Card>
      <Card title="Thread">
        {messages.isLoading ? <LoadingState /> : null}
        {messages.error ? <ErrorState message={messages.error.message} /> : null}
        {!messages.data?.length ? <EmptyState title="No messages" /> : null}
        {messages.data?.map(message => <Card key={message.id} title={`${message.sender} · ${message.priority}`}><Text>{message.body}</Text></Card>)}
        <TextInput value={body} onChange={setBody} placeholder="Write a message" />
        <Button onClick={() => send.mutate()} loading={send.isPending} disabled={!body.trim()}>Send</Button>
      </Card>
    </div>
  );
}
