import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, EmptyState, ErrorState, LoadingState, PageHeader, Stack, StatusChip } from '@regent/ui-shared';
import { api } from '../lib/api';

export function TenantsPage() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['ops-tenants'], queryFn: api.tenants });
  const update = useMutation({ mutationFn: ({ id, status }: { id: string; status: string }) => api.setTenantStatus(id, status),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['ops-tenants'] }) });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  if (!data?.length) return <EmptyState title="No tenants" />;
  return <Stack spacing={3}>
    <PageHeader title="Tenants" description="Suspend or reactivate local tenant accounts." />
    <Card title="Tenant accounts"><DataTable rows={data} getRowId={row => row.id} columns={[
      { id: 'name', header: 'Tenant', render: row => row.name },
      { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone={row.status === 'Active' ? 'success' : 'warning'} /> },
      { id: 'employees', header: 'Employees', render: row => row.employees },
      { id: 'runners', header: 'Runners', render: row => row.runnerCount },
      { id: 'actions', header: 'Actions', render: row => <Button tone={row.status === 'Active' ? 'danger' : 'secondary'}
        onClick={() => update.mutate({ id: row.id, status: row.status === 'Active' ? 'Suspended' : 'Active' })}>
        {row.status === 'Active' ? 'Suspend' : 'Activate'}
      </Button> }
    ]} /></Card>
  </Stack>;
}
