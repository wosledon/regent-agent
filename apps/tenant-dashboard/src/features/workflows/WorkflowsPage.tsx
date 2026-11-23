import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, EmptyState, ErrorState, FormField, Grid, LoadingState, PageHeader,
  Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import type { WorkflowRun } from '../../lib/domain-types';

export function WorkflowsPage() {
  const queryClient = useQueryClient();
  const [run, setRun] = useState<WorkflowRun | null>(null);
  const [name, setName] = useState('');
  const [steps, setSteps] = useState('');
  const [scopePositionId, setScopePositionId] = useState('');
  const [decision, setDecision] = useState('Adopt');
  const [reviewer, setReviewer] = useState('Leader');
  const [note, setNote] = useState('Evidence reviewed');
  const { data, isLoading, error } = useQuery({ queryKey: ['workflows'], queryFn: api.workflows });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['workflows'] });
  const capture = useMutation({
    mutationFn: () => api.createWorkflow(name, steps.split('\n').map(value => value.trim()).filter(Boolean), scopePositionId),
    onSuccess: async () => { setName(''); setSteps(''); setScopePositionId(''); await invalidate(); }
  });
  const nominate = useMutation({ mutationFn: api.nominateWorkflow, onSuccess: invalidate });
  const review = useMutation({
    mutationFn: (id: string) => api.reviewWorkflow(id, decision, reviewer, note), onSuccess: invalidate
  });
  const start = useMutation({ mutationFn: api.startWorkflow, onSuccess: setRun });
  const advance = useMutation({ mutationFn: api.advanceWorkflow, onSuccess: setRun });

  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  return (
    <Stack spacing={3}>
      <PageHeader title="Workflows" description="Capture personal practice, nominate it upward, review immutably and run approved versions." />
      <Grid columns={2}>
        <Card title="Capture personal workflow">
          <Stack spacing={2}>
            <FormField label="Name"><TextInput value={name} onChange={setName} placeholder="Release checklist" /></FormField>
            <FormField label="Steps (one per line)"><TextInput multiline minRows={3} value={steps} onChange={setSteps} placeholder={'Build\nVerify\nRelease'} /></FormField>
            <FormField label="Scope position ID"><TextInput value={scopePositionId} onChange={setScopePositionId} /></FormField>
            <Button disabled={!name || !steps || !scopePositionId} loading={capture.isPending} onClick={() => capture.mutate()}>Capture workflow</Button>
          </Stack>
        </Card>
        <Card title="Review nomination">
          <Stack spacing={2}>
            <Select label="Decision" value={decision} onChange={setDecision} options={['Adopt', 'Pilot', 'Reject'].map(value => ({ value, label: value }))} />
            <FormField label="Reviewer"><TextInput value={reviewer} onChange={setReviewer} /></FormField>
            <FormField label="Review note"><TextInput value={note} onChange={setNote} /></FormField>
            <Text variant="caption">Use the Review action on a nominated version. Adopted and pilot versions are immutable.</Text>
          </Stack>
        </Card>
      </Grid>
      {data?.length === 0 ? <EmptyState title="No workflows" /> : null}
      {data?.map(workflow => (
        <Card key={workflow.id} title={`${workflow.name} v${workflow.version}`}
          action={<StatusChip label={workflow.status} tone={workflow.status === 'Rejected' ? 'error' : workflow.status === 'Personal' ? 'info' : 'success'} />}>
          <Stack spacing={1}>
            <Text>{workflow.steps.join(' → ')}</Text>
            <Text variant="caption">Author: {workflow.author ?? 'system'} · scope: {workflow.scopePositionId ?? 'company'}{workflow.reviewedBy ? ` · reviewed by ${workflow.reviewedBy}` : ''}</Text>
            <Stack direction="row" wrap>
              {workflow.status === 'Personal' ? <Button tone="secondary" onClick={() => nominate.mutate(workflow.id)}>Nominate</Button> : null}
              {workflow.status === 'Nominated' ? <Button onClick={() => review.mutate(workflow.id)}>Review</Button> : null}
              {['Active', 'Adopted', 'Pilot'].includes(workflow.status) ? <Button tone="neutral" onClick={() => start.mutate(workflow.id)}>Start run</Button> : null}
            </Stack>
          </Stack>
        </Card>
      ))}
      {run ? <Card title="Current run"><Text>{run.status} · step {run.currentStep + 1}</Text><Button onClick={() => advance.mutate(run.id)} loading={advance.isPending}>Advance</Button></Card> : null}
    </Stack>
  );
}
