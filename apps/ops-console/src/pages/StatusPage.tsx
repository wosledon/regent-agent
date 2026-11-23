import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, ErrorState, FormField, LoadingState, PageHeader, Select, Stack, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { api, type PlatformIncident } from '../lib/api';

export function StatusPage({ operational = false }: { operational?: boolean }) {
  const queryClient = useQueryClient();
  const status = useQuery({ queryKey: ['platform-status'], queryFn: api.status, refetchInterval: 30_000 });
  const [title, setTitle] = useState('');
  const [severity, setSeverity] = useState('SEV3');
  const [message, setMessage] = useState('');
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['platform-status'] });
  const refreshStatus = useMutation({ mutationFn: api.refreshStatus, onSuccess: refresh });
  const createIncident = useMutation({
    mutationFn: () => api.createIncident(title, severity, message),
    onSuccess: async () => { setTitle(''); setMessage(''); await refresh(); }
  });
  const resolve = useMutation({ mutationFn: api.resolveIncident, onSuccess: refresh });
  if (status.isLoading) return <LoadingState />;
  if (status.error) return <ErrorState message={status.error.message} />;
  const data = status.data;
  return (
    <Stack spacing={3}>
      <PageHeader title="Platform status" description="Sanitized local service availability and published incidents."
        actions={operational ? <Button onClick={() => refreshStatus.mutate()} loading={refreshStatus.isPending}>Refresh health</Button> : undefined} />
      <Card title="Overall status" action={<StatusChip label={data?.overallStatus ?? 'Unknown'} tone={data?.overallStatus === 'Operational' ? 'success' : 'warning'} />}>
        <Text variant="caption">Checked at {data?.checkedAt ? new Date(data.checkedAt).toLocaleString() : 'never'}</Text>
      </Card>
      <Card title="Components">
        <DataTable rows={data?.components ?? []} getRowId={row => row.name} columns={[
          { id: 'name', header: 'Component', render: row => row.name },
          { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone={row.status === 'Operational' ? 'success' : row.status === 'Down' ? 'error' : 'warning'} /> },
          { id: 'latency', header: 'Latency', render: row => `${row.latencyMs} ms` },
          { id: 'message', header: 'Message', render: row => row.message }
        ]} />
      </Card>
      <Card title="Published incidents">
        <DataTable rows={data?.incidents ?? []} getRowId={row => row.id} columns={[
          { id: 'title', header: 'Incident', render: row => row.title },
          { id: 'severity', header: 'Severity', render: row => row.severity },
          { id: 'message', header: 'Message', render: row => row.message },
          ...(operational ? [{ id: 'actions', header: 'Actions', render: (row: PlatformIncident) =>
            <Button tone="neutral" onClick={() => resolve.mutate(row.id)}>Resolve</Button> }] : [])
        ]} />
      </Card>
      {operational ? <Card title="Publish incident">
        <FormField label="Title"><TextInput value={title} onChange={setTitle} /></FormField>
        <Select label="Severity" value={severity} onChange={setSeverity} options={['SEV1', 'SEV2', 'SEV3', 'SEV4'].map(value => ({ value, label: value }))} />
        <FormField label="Sanitized message"><TextInput value={message} onChange={setMessage} multiline minRows={2} /></FormField>
        <Button tone="danger" onClick={() => createIncident.mutate()} disabled={!title || !message} loading={createIncident.isPending}>Publish incident</Button>
      </Card> : null}
    </Stack>
  );
}
