import { useQuery } from '@tanstack/react-query';
import { Button, Card, EmptyState, ErrorState, LoadingState, Stack, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

interface MemoryReviewQueueProps {
  onChanged: () => void | Promise<void>;
}

export function MemoryReviewQueue({ onChanged }: MemoryReviewQueueProps) {
  const queue = useQuery({ queryKey: ['memory-review-queue'], queryFn: api.memoryReviewQueue });
  if (queue.isLoading) return <LoadingState />;
  if (queue.error) return <ErrorState message={queue.error.message} />;
  if (queue.data?.length === 0) return <Card title="Publication review queue"><EmptyState title="No candidate claims" /></Card>;

  return (
    <Card title="Publication review queue">
      <Stack spacing={2}>
        {queue.data?.map(claim => (
          <Stack key={claim.id} spacing={1}>
            <Stack direction="row" wrap>
              <StatusChip label={claim.scope} tone="info" />
              <Text>{claim.content}</Text>
            </Stack>
            <Text variant="caption">Source: {claim.sourceRef} · author: {claim.author ?? 'unknown'} · confidence {claim.confidence}</Text>
            <Stack direction="row" wrap>
              <Button onClick={() => publish(claim.id, 'Approve')}>Publish</Button>
              <Button tone="danger" onClick={() => publish(claim.id, 'Reject')}>Reject</Button>
            </Stack>
          </Stack>
        ))}
      </Stack>
    </Card>
  );

  async function publish(id: string, decision: 'Approve' | 'Reject') {
    await api.publishMemory(id, decision);
    await onChanged();
  }
}
