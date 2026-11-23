import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, EmptyState, ErrorState, FormField, Grid, LoadingState, PageHeader, Select, Stack, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function ButlerPage() {
  const queryClient = useQueryClient();
  const reports = useQuery({ queryKey: ['butler-reports'], queryFn: api.butlerReports });
  const schedules = useQuery({ queryKey: ['butler-schedules'], queryFn: api.butlerSchedules });
  const [question, setQuestion] = useState('How is the company doing?');
  const [answer, setAnswer] = useState<Awaited<ReturnType<typeof api.butlerAsk>> | null>(null);
  const [kind, setKind] = useState('Daily');
  const [frequency, setFrequency] = useState('Daily');
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['butler-reports'] }),
    queryClient.invalidateQueries({ queryKey: ['butler-schedules'] })
  ]);
  const ask = useMutation({ mutationFn: () => api.butlerAsk(question), onSuccess: setAnswer });
  const generate = useMutation({ mutationFn: () => api.generateButlerReport(kind), onSuccess: invalidate });
  const retry = useMutation({ mutationFn: (id: string) => api.retryButlerReport(id), onSuccess: invalidate });
  const saveSchedule = useMutation({
    mutationFn: (enabled: boolean) => api.saveButlerSchedule(kind, frequency, enabled), onSuccess: invalidate
  });
  if (reports.isLoading || schedules.isLoading) return <LoadingState />;
  if (reports.error || schedules.error) return <ErrorState message={(reports.error ?? schedules.error)!.message} />;
  return (
    <Stack spacing={3}>
      <PageHeader title="Butler" description="Ask about the company and manage deterministic scheduled reports." />
      <Grid columns={2}>
        <Card title="Ask the Butler">
          <FormField label="Question"><TextInput value={question} onChange={setQuestion} multiline minRows={2} placeholder="Question" /></FormField>
          <Button onClick={() => ask.mutate()} disabled={!question} loading={ask.isPending}>Ask</Button>
          {answer ? <Card title={`Intent: ${answer.intent ?? 'summary'}`}><Text variant="h3">{answer.answer}</Text><Text variant="caption">{(answer.evidenceRefs ?? []).join(' · ')}</Text></Card> : null}
        </Card>
        <Card title="Report schedule">
          <Select label="Report kind" value={kind} onChange={setKind} options={['Daily', 'Weekly', 'Budget', 'Incident', 'Custom'].map(value => ({ value, label: value }))} />
          <Select label="Frequency" value={frequency} onChange={setFrequency} options={['Daily', 'Weekly', 'Manual'].map(value => ({ value, label: value }))} />
          <Stack direction="row">
            <Button onClick={() => saveSchedule.mutate(true)} loading={saveSchedule.isPending}>Enable</Button>
            <Button tone="neutral" onClick={() => saveSchedule.mutate(false)} loading={saveSchedule.isPending}>Disable</Button>
            <Button tone="secondary" onClick={() => generate.mutate()} loading={generate.isPending}>Generate now</Button>
          </Stack>
          <DataTable rows={schedules.data ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'kind', header: 'Kind', render: row => row.kind ?? '' },
            { id: 'frequency', header: 'Frequency', render: row => row.frequency ?? '' },
            { id: 'enabled', header: 'Enabled', render: row => row.enabled ? 'Yes' : 'No' },
            { id: 'next', header: 'Next run', render: row => row.nextRunAt ? new Date(row.nextRunAt).toLocaleString() : 'Manual' }
          ]} />
        </Card>
      </Grid>
      <Card title="Reports">
        {!reports.data?.length ? <EmptyState title="No reports yet" description="Generate a report or wait for the scheduler." /> : null}
        <DataTable rows={reports.data ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'title', header: 'Report', render: row => row.title ?? '' },
          { id: 'summary', header: 'Summary', render: row => row.summary ?? '' },
          { id: 'status', header: 'Status', render: row => <StatusChip label={row.status ?? ''} tone={row.status === 'Completed' ? 'success' : 'warning'} /> },
          { id: 'created', header: 'Created', render: row => row.createdAt ? new Date(row.createdAt).toLocaleString() : '' },
          { id: 'actions', header: 'Actions', render: row => <Button tone="neutral" onClick={() => retry.mutate(row.id ?? '')} disabled={row.status === 'Completed'}>Retry</Button> }
        ]} />
      </Card>
    </Stack>
  );
}
