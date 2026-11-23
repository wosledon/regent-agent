import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, EmptyState, ErrorState, LoadingState, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function MemoryPage() {
  const queryClient = useQueryClient();
  const [query, setQuery] = useState('');
  const [correction, setCorrection] = useState('Updated after review.');
  const { data, isLoading, error } = useQuery({ queryKey: ['memories', query], queryFn: () => api.memories(query) });
  const correct = useMutation({
    mutationFn: (id: string) => api.correctMemory(id, correction, 'Reviewed from dashboard'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['memories'] })
  });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  return (
    <div>
      <Text variant="h1">Knowledge & Memory</Text>
      <TextInput value={query} onChange={setQuery} placeholder="Search memory" />
      <TextInput value={correction} onChange={setCorrection} placeholder="Correction text" />
      {data?.length === 0 ? <EmptyState title="No matching memory" /> : null}
      {data?.map(item => (
        <Card key={item.id} title={`${item.scope} · v${item.version}`} action={<StatusChip label={item.taint} tone="info" />}>
          <Text>{item.content}</Text>
          <Text variant="caption">Source: {item.sourceRef} · Confidence: {item.confidence}</Text>
          <Button tone="neutral" onClick={() => correct.mutate(item.id)} loading={correct.isPending}>Create correction</Button>
        </Card>
      ))}
    </div>
  );
}
