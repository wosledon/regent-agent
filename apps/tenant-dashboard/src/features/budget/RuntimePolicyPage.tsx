import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState,
  PageHeader, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function RuntimePolicyPage() {
  const queryClient = useQueryClient();
  const policy = useQuery({ queryKey: ['runtime-policy'], queryFn: api.runtimePolicy });
  const summary = useQuery({ queryKey: ['cost-summary'], queryFn: () => api.costSummary() });
  const [projectId, setProjectId] = useState('');
  const [softLimit, setSoftLimit] = useState('1000');
  const [hardLimit, setHardLimit] = useState('1200');
  const [currency, setCurrency] = useState('CNY');
  const [sourceEventId, setSourceEventId] = useState('');
  const [amount, setAmount] = useState('10');
  const [category, setCategory] = useState('Model');
  const [alwaysAvailable, setAlwaysAvailable] = useState('true');
  const [timeZone, setTimeZone] = useState('Asia/Shanghai');
  const [windowStart, setWindowStart] = useState('09:00');
  const [windowEnd, setWindowEnd] = useState('17:00');
  const [windowKind, setWindowKind] = useState('Silent');
  const [maxConcurrent, setMaxConcurrent] = useState('8');
  const [maxQueue, setMaxQueue] = useState('100');
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['runtime-policy'] }),
    queryClient.invalidateQueries({ queryKey: ['cost-summary'] })
  ]);
  const setBudget = useMutation({
    mutationFn: () => api.setBudget({ projectId, softLimit: Number(softLimit), hardLimit: Number(hardLimit), currency, carryForward: false }),
    onSuccess: async () => { setProjectId(''); await invalidate(); }
  });
  const recordCost = useMutation({
    mutationFn: () => api.recordCost({
      id: crypto.randomUUID(), sourceEventId, taskId: null, projectId: projectId || null,
      employeeId: null, model: 'local-cheap', amount: Number(amount), currency, category,
      allocationStatus: 'Direct', correctionOf: null, reason: null, createdAt: new Date().toISOString()
    }), onSuccess: async () => { setSourceEventId(''); await invalidate(); }
  });
  const updateSchedule = useMutation({
    mutationFn: () => api.updateSchedule({
      timeZone, alwaysAvailable: alwaysAvailable === 'true', emergencyWake: true, version: 1,
      windows: [{ days: 'Mon,Tue,Wed,Thu,Fri', startLocal: windowStart, endLocal: windowEnd, kind: windowKind }]
    }), onSuccess: invalidate
  });
  const updateCapacity = useMutation({
    mutationFn: () => api.updateCapacity({
      maxConcurrentTasks: Number(maxConcurrent), maxQueueLength: Number(maxQueue),
      apiP95TargetMs: 500, noisyNeighborCpuPercent: 80, version: 1
    }), onSuccess: invalidate
  });

  if (policy.isLoading || summary.isLoading) return <LoadingState />;
  if (policy.error || summary.error) return <ErrorState message={(policy.error ?? summary.error)!.message} />;
  const state = policy.data;

  return (
    <Stack spacing={3}>
      <PageHeader title="Budget, cost & scheduling" description="Original-currency accounting, deterministic budgets, work windows and backpressure." />
      <Grid columns={2}>
        <Card title="Cost ledger">
          <FormField label="Source event ID"><TextInput value={sourceEventId} onChange={setSourceEventId} placeholder="usage:unique-event" /></FormField>
          <FormField label="Project ID (optional)"><TextInput value={projectId} onChange={setProjectId} /></FormField>
          <FormField label="Amount"><TextInput value={amount} onChange={setAmount} inputProps={{ inputMode: 'decimal' }} /></FormField>
          <FormField label="Currency"><TextInput value={currency} onChange={setCurrency} /></FormField>
          <Select label="Category" value={category} onChange={setCategory} options={['Model', 'Tool', 'Storage', 'Correction'].map(value => ({ value, label: value }))} />
          <Button onClick={() => recordCost.mutate()} disabled={!sourceEventId || Number(amount) < 0} loading={recordCost.isPending}>Record cost once</Button>
          <DataTable rows={state?.costs ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'event', header: 'Event', render: row => row.sourceEventId ?? '' },
            { id: 'amount', header: 'Amount', render: row => `${row.amount ?? 0} ${row.currency ?? ''}` },
            { id: 'allocation', header: 'Allocation', render: row => row.allocationStatus ?? '' },
            { id: 'project', header: 'Project', render: row => row.projectId ?? 'Unallocated' }
          ]} />
        </Card>
        <Card title="Original-currency totals">
          <DataTable rows={summary.data?.totals ?? []} getRowId={row => row.currency} columns={[
            { id: 'currency', header: 'Currency', render: row => row.currency },
            { id: 'amount', header: 'Amount', render: row => row.amount.toLocaleString() }
          ]} />
          <Text>{summary.data?.unallocatedCount ?? 0} unallocated entries</Text>
          {summary.data?.warnings.map(warning => <StatusChip key={warning} label={warning} tone="warning" />)}
          <Text variant="caption">Currencies are never converted or summed together.</Text>
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card title="Project budget">
          <FormField label="Project ID"><TextInput value={projectId} onChange={setProjectId} /></FormField>
          <FormField label="Soft limit"><TextInput value={softLimit} onChange={setSoftLimit} inputProps={{ inputMode: 'decimal' }} /></FormField>
          <FormField label="Hard limit"><TextInput value={hardLimit} onChange={setHardLimit} inputProps={{ inputMode: 'decimal' }} /></FormField>
          <FormField label="Currency"><TextInput value={currency} onChange={setCurrency} /></FormField>
          <Button onClick={() => setBudget.mutate()} disabled={!projectId || Number(hardLimit) < Number(softLimit)} loading={setBudget.isPending}>Save budget</Button>
          <DataTable rows={state?.budgets ?? []} getRowId={row => `${row.projectId}:${row.currency}`} columns={[
            { id: 'project', header: 'Project', render: row => row.projectId ?? '' },
            { id: 'soft', header: 'Soft', render: row => `${row.softLimit ?? 0} ${row.currency ?? ''}` },
            { id: 'hard', header: 'Hard', render: row => `${row.hardLimit ?? 0} ${row.currency ?? ''}` }
          ]} />
        </Card>
        <Card title="Work schedule">
          <Select label="Availability" value={alwaysAvailable} onChange={setAlwaysAvailable} options={[{ value: 'true', label: '24x7 configurable' }, { value: 'false', label: 'Use windows' }]} />
          <FormField label="IANA time zone"><TextInput value={timeZone} onChange={setTimeZone} /></FormField>
          <FormField label="Window start"><TextInput value={windowStart} onChange={setWindowStart} placeholder="09:00" /></FormField>
          <FormField label="Window end"><TextInput value={windowEnd} onChange={setWindowEnd} placeholder="17:00" /></FormField>
          <Select label="Window kind" value={windowKind} onChange={setWindowKind} options={[{ value: 'Silent', label: 'Silent' }, { value: 'Cheap', label: 'Cheap price' }]} />
          <Button onClick={() => updateSchedule.mutate()} loading={updateSchedule.isPending}>Save schedule</Button>
        </Card>
      </Grid>
      <Card title="Capacity and backpressure">
        <FormField label="Max concurrent tasks"><TextInput value={maxConcurrent} onChange={setMaxConcurrent} inputProps={{ inputMode: 'numeric' }} /></FormField>
        <FormField label="Max queue length"><TextInput value={maxQueue} onChange={setMaxQueue} inputProps={{ inputMode: 'numeric' }} /></FormField>
        <Button tone="secondary" onClick={() => updateCapacity.mutate()} loading={updateCapacity.isPending}>Save capacity policy</Button>
        <Text>{state?.capacity?.maxConcurrentTasks ?? 0} concurrent · {state?.capacity?.maxQueueLength ?? 0} queued · P95 {state?.capacity?.apiP95TargetMs ?? 0} ms</Text>
      </Card>
    </Stack>
  );
}
