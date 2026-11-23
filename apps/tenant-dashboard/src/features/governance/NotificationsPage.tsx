import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, EmptyState, ErrorState, LoadingState, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function NotificationsPage() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['notifications'], queryFn: api.notifications });
  const action = useMutation({
    mutationFn: ({ id, verb }: { id: string; verb: 'read' | 'acknowledge' | 'archive' }) => api.notificationAction(id, verb),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] })
  });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  if (!data?.length) return <EmptyState title="No notifications" />;
  return (
    <div>
      <Text variant="h1">Notifications</Text>
      {data.map(item => (
        <Card key={item.id} title={item.title} action={<StatusChip label={item.level} tone="info" />}>
          <Text variant="caption">{item.status} · {new Date(item.createdAt).toLocaleString()}</Text>
          <Button tone="neutral" onClick={() => action.mutate({ id: item.id, verb: 'acknowledge' })}>Acknowledge</Button>
          <Button tone="neutral" onClick={() => action.mutate({ id: item.id, verb: 'archive' })}>Archive</Button>
        </Card>
      ))}
    </div>
  );
}
