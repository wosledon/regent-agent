import { useQuery } from '@tanstack/react-query';
import { DataTable, EmptyState, ErrorState, LoadingState, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function PerformancePage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['kpis'], queryFn: api.kpis });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  if (!data?.length) return <EmptyState title="No KPI snapshots" />;
  return (
    <div>
      <Text variant="h1">Performance</Text>
      <DataTable rows={data} getRowId={row => row.id} columns={[
        { id: 'metric', header: 'Metric', render: row => row.metricKey },
        { id: 'subject', header: 'Subject', render: row => `${row.subjectType}: ${row.subjectId}` },
        { id: 'value', header: 'Value', render: row => row.value },
        { id: 'target', header: 'Target', render: row => row.target },
        { id: 'status', header: 'Status', render: row => row.status }
      ]} />
    </div>
  );
}
