import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, ErrorState, FormField, LoadingState, PageHeader, Select, Stack, StatusChip, TextInput } from '@regent/ui-shared';
import { api } from '../lib/api';

export function ObservabilityPage() {
  const queryClient = useQueryClient();
  const profile = useQuery({ queryKey: ['observability'], queryFn: api.observability });
  const evaluations = useQuery({ queryKey: ['slo-evaluations'], queryFn: api.evaluateSlos });
  const [draft, setDraft] = useState<ReturnType<typeof observabilityDefaults> | null>(null);
  const [sloName, setSloName] = useState('api_availability');
  const [metric, setMetric] = useState('api.success');
  const [target, setTarget] = useState('0.99');
  const save = useMutation({ mutationFn: () => api.saveObservability(draft ?? profile.data!), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['observability'] }) });
  const saveSlo = useMutation({ mutationFn: () => api.saveSlo({ name: sloName, metric, target: Number(target), windowDays: 30 }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['slo-evaluations'] }) });
  if (profile.isLoading || evaluations.isLoading) return <LoadingState />;
  const error = profile.error ?? evaluations.error;
  if (error) return <ErrorState message={error.message} />;
  const value = draft ?? profile.data ?? observabilityDefaults();
  return <Stack spacing={3}>
    <PageHeader title="Observability" description="Local telemetry profile, SLOs and error budgets." />
    <Card title="Telemetry profile">
      <Select label="Mode" value={value.mode} onChange={mode => setDraft({ ...value, mode })} options={['off', 'local', 'otlp', 'elastic'].map(mode => ({ value: mode, label: mode }))} />
      <FormField label="Service name"><TextInput value={value.serviceName} onChange={serviceName => setDraft({ ...value, serviceName })} /></FormField>
      <FormField label="Metric interval seconds"><TextInput value={String(value.metricIntervalSeconds)} onChange={metricIntervalSeconds => setDraft({ ...value, metricIntervalSeconds: Number(metricIntervalSeconds) })} /></FormField>
      <Button onClick={() => save.mutate()} loading={save.isPending}>Save profile</Button>
    </Card>
    <Card title="SLO definition">
      <FormField label="Name"><TextInput value={sloName} onChange={setSloName} /></FormField>
      <FormField label="Metric"><TextInput value={metric} onChange={setMetric} /></FormField>
      <FormField label="Target"><TextInput value={target} onChange={setTarget} /></FormField>
      <Button tone="secondary" onClick={() => saveSlo.mutate()} loading={saveSlo.isPending}>Save SLO</Button>
      <DataTable rows={evaluations.data ?? []} getRowId={row => row.name} columns={[
        { id: 'name', header: 'SLO', render: row => row.name },
        { id: 'actual', header: 'Actual', render: row => `${(row.actual * 100).toFixed(2)}%` },
        { id: 'target', header: 'Target', render: row => `${(row.target * 100).toFixed(2)}%` },
        { id: 'budget', header: 'Error budget', render: row => `${(row.errorBudgetRemaining * 100).toFixed(1)}%` },
        { id: 'status', header: 'Status', render: row => <StatusChip label={row.passed ? 'Passed' : 'Failed'} tone={row.passed ? 'success' : 'error'} /> }
      ]} />
    </Card>
  </Stack>;
}

function observabilityDefaults() {
  return { mode: 'local', serviceName: 'regent-agent', elasticEnabled: false, metricIntervalSeconds: 15 };
}
