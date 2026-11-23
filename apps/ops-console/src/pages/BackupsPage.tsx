import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, EmptyState, ErrorState, LoadingState, PageHeader, Stack, Text } from '@regent/ui-shared';
import { api } from '../lib/api';

export function BackupsPage() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['ops-backups'], queryFn: api.backups });
  const save = useMutation({ mutationFn: (enabled: boolean) => api.setBackup(enabled),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['ops-backups'] }) });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  const profile = data?.[0];
  if (!profile) return <EmptyState title="No backup profile" />;
  return <Stack spacing={3}>
    <PageHeader title="Backups" description="Optional local backup profile and retention." />
    <Card title="Local backup profile">
      <Text>Status: {profile.enabled ? 'Enabled' : 'Disabled'}</Text>
      <Text>Components: {profile.components}</Text>
      <Text>Retention: {profile.retentionDays} days</Text>
      <Button onClick={() => save.mutate(!profile.enabled)} loading={save.isPending}>{profile.enabled ? 'Disable' : 'Enable'}</Button>
    </Card>
  </Stack>;
}
