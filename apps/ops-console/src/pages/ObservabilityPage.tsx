import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, ErrorState, FormField, LoadingState, PageHeader, Select, Stack, StatusChip, TextInput } from '@regent/ui-shared';
import { api } from '../lib/api';

export function ObservabilityPage() {
  const queryClient = useQueryClient();
  const profile = useQuery({ queryKey: ['observability'], queryFn: api.observability });
  const evaluations = useQuery({ queryKey: ['slo-evaluations'], queryFn: api.evaluateSlos });
  const alerts = useQuery({ queryKey: ['observability-alerts'], queryFn: api.alerts });
  const [draft, setDraft] = useState<ReturnType<typeof observabilityDefaults> | null>(null);
  const [sloName, setSloName] = useState('api_availability');
  const [metric, setMetric] = useState('api.success');
  const [target, setTarget] = useState('0.99');
  const [fingerprint, setFingerprint] = useState('');
  const [silenceReason, setSilenceReason] = useState('');
  const invalidateAlerts = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['observability-alerts'] }),
    queryClient.invalidateQueries({ queryKey: ['slo-evaluations'] })
  ]);
  const acknowledge = useMutation({ mutationFn: api.acknowledgeAlert, onSuccess: invalidateAlerts });
  const resolve = useMutation({ mutationFn: (id: string) => api.resolveAlert(id, 'Recovered and verified by SRE'), onSuccess: invalidateAlerts });
  const silence = useMutation({
    mutationFn: () => api.silenceAlert(fingerprint, silenceReason, 60),
    onSuccess: async () => { setFingerprint(''); setSilenceReason(''); await invalidateAlerts(); }
  });
  const save = useMutation({ mutationFn: () => api.saveObservability(draft ?? profile.data!), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['observability'] }) });
  const saveSlo = useMutation({ mutationFn: () => api.saveSlo({ name: sloName, metric, target: Number(target), windowDays: 30 }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['slo-evaluations'] }) });
  if (profile.isLoading || evaluations.isLoading || alerts.isLoading) return <LoadingState />;
  const error = profile.error ?? evaluations.error ?? alerts.error;
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
    <Card title="Alert routing and lifecycle">
      <DataTable rows={(alerts.data ?? []).filter(alert => alert.status !== 'Resolved')} getRowId={row => row.id} columns={[
        { id: 'level', header: 'Level', render: row => <StatusChip label={row.level} tone={row.level === 'L3' ? 'error' : row.level === 'L2' ? 'warning' : 'info'} /> },
        { id: 'slo', header: 'SLO', render: row => row.sloName },
        { id: 'status', header: 'Status', render: row => row.status },
        { id: 'owner', header: 'Owner', render: row => row.owner },
        { id: 'message', header: 'Impact', render: row => row.message },
        { id: 'actions', header: 'Actions', render: row => <Stack direction="row" spacing={1}>
          <Button onClick={() => acknowledge.mutate(row.id)} disabled={row.status === 'Acknowledged' || row.status === 'Silenced'}>Ack</Button>
          <Button tone="secondary" onClick={() => resolve.mutate(row.id)}>Resolve</Button>
          <Button tone="secondary" onClick={() => { setFingerprint(row.fingerprint); setSilenceReason(row.message); }}>Silence</Button>
        </Stack> }
      ]} />
      <FormField label="Silence fingerprint"><TextInput value={fingerprint} onChange={setFingerprint} /></FormField>
      <FormField label="Silence reason"><TextInput value={silenceReason} onChange={setSilenceReason} /></FormField>
      <Button onClick={() => silence.mutate()} disabled={!fingerprint || !silenceReason} loading={silence.isPending}>Approve 60 minute silence</Button>
    </Card>
  </Stack>;
}

function observabilityDefaults() {
  return { mode: 'local', serviceName: 'regent-agent', elasticEnabled: false, metricIntervalSeconds: 15 };
}
