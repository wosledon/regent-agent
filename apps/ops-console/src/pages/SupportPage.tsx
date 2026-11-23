import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, EmptyState, ErrorState, LoadingState, PageHeader, Stack, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../lib/api';

export function SupportPage() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['ops-grants'], queryFn: api.grants });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['ops-grants'] });
  const create = useMutation({ mutationFn: () => api.createGrant('demo'), onSuccess: invalidate });
  const approve = useMutation({ mutationFn: api.approveGrant, onSuccess: invalidate });
  const revoke = useMutation({ mutationFn: api.revokeGrant, onSuccess: invalidate });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  return <Stack spacing={3}>
    <PageHeader title="Support Grants" description="Time-limited, scoped and audited tenant access."
      actions={<Button onClick={() => create.mutate()} loading={create.isPending}>Request 30-minute read-only grant</Button>} />
    {!data?.length ? <EmptyState title="No grants" /> : null}
    <Card title="Access requests"><DataTable rows={data ?? []} getRowId={row => row.id} columns={[
      { id: 'tenant', header: 'Tenant', render: row => row.tenantId },
      { id: 'scope', header: 'Scope', render: row => row.scope },
      { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone={row.status === 'Active' ? 'success' : 'warning'} /> },
      { id: 'expires', header: 'Expires', render: row => new Date(row.expiresAt).toLocaleString() },
      { id: 'actions', header: 'Actions', render: row => <Stack direction="row">
        {row.status === 'Pending' ? <Button onClick={() => approve.mutate(row.id)}>Approve</Button> : null}
        {row.status === 'Active' ? <Button tone="danger" onClick={() => revoke.mutate(row.id)}>Revoke</Button> : null}
      </Stack> }
    ]} /><Text variant="caption">Approval and revocation are recorded in the operations audit.</Text></Card>
  </Stack>;
}
