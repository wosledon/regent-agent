import { useQuery } from '@tanstack/react-query';
import { Card, DataTable, EmptyState, ErrorState, LoadingState, PageHeader, Stack } from '@regent/ui-shared';
import { api } from '../lib/api';

export function AuditPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['ops-audit'], queryFn: api.audit });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  return <Stack spacing={3}>
    <PageHeader title="Operations Audit" description="Operator, action, target and timestamp." />
    <Card title="Audit records">{!data?.length ? <EmptyState title="No operations yet" /> : null}
      <DataTable rows={data ?? []} getRowId={row => row.id} columns={[
        { id: 'operator', header: 'Operator', render: row => row.operatorId },
        { id: 'action', header: 'Action', render: row => row.action },
        { id: 'target', header: 'Target', render: row => row.target },
        { id: 'time', header: 'Time', render: row => new Date(row.createdAt).toLocaleString() }
      ]} />
    </Card>
  </Stack>;
}
