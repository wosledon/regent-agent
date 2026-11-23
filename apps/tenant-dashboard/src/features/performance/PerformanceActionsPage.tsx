import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState, PageHeader, Select, Stack, StatusChip, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function PerformanceActionsPage() {
  const queryClient = useQueryClient();
  const actions = useQuery({ queryKey: ['performance-actions'], queryFn: api.performanceActions });
  const improvements = useQuery({ queryKey: ['improvements'], queryFn: api.improvements });
  const [employeeId, setEmployeeId] = useState('');
  const [actionType, setActionType] = useState('Coaching');
  const [reason, setReason] = useState('');
  const [approvalId, setApprovalId] = useState('');
  const [category, setCategory] = useState('process');
  const [summary, setSummary] = useState('');
  const [benefit, setBenefit] = useState('');
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['performance-actions'] }),
    queryClient.invalidateQueries({ queryKey: ['improvements'] })
  ]);
  const perform = useMutation({ mutationFn: () => api.performAction(employeeId, actionType, reason, approvalId || undefined), onSuccess: async () => { setReason(''); setApprovalId(''); await invalidate(); } });
  const propose = useMutation({ mutationFn: () => api.proposeImprovement(employeeId, category, summary, benefit), onSuccess: async () => { setSummary(''); setBenefit(''); await invalidate(); } });
  const appeal = useMutation({ mutationFn: ({ id, reason }: { id: string; reason: string }) => api.appealPerformanceAction(id, reason), onSuccess: invalidate });
  const resolveAppeal = useMutation({ mutationFn: ({ id, upheld }: { id: string; upheld: boolean }) => api.resolvePerformanceAppeal(id, upheld), onSuccess: invalidate });
  const reviewImprovement = useMutation({ mutationFn: ({ id, approved }: { id: string; approved: boolean }) => api.reviewImprovement(id, approved), onSuccess: invalidate });
  if (actions.isLoading || improvements.isLoading) return <LoadingState />;
  const error = actions.error ?? improvements.error;
  if (error) return <ErrorState message={error.message} />;
  return <Stack spacing={3}>
    <PageHeader title="Performance actions" description="AI-assisted non-termination actions, human-approved termination and appeals." />
    <Grid columns={2}>
      <Card title="Perform action">
        <FormField label="Employee ID"><TextInput value={employeeId} onChange={setEmployeeId} /></FormField>
        <Select label="Action" value={actionType} onChange={setActionType} options={['Coaching', 'Transfer', 'Demotion', 'Freeze', 'Terminate', 'Reinstate'].map(value => ({ value, label: value }))} />
        <FormField label="Reason"><TextInput value={reason} onChange={setReason} multiline minRows={2} /></FormField>
        {actionType === 'Terminate' ? <FormField label="Boss approval ID"><TextInput value={approvalId} onChange={setApprovalId} /></FormField> : null}
        <Button tone={actionType === 'Terminate' ? 'danger' : 'primary'} onClick={() => perform.mutate()} disabled={!employeeId || !reason || (actionType === 'Terminate' && !approvalId)} loading={perform.isPending}>Execute action</Button>
        <DataTable rows={actions.data ?? []} getRowId={row => row.id} columns={[
          { id: 'employee', header: 'Employee', render: row => row.employeeId },
          { id: 'action', header: 'Action', render: row => row.actionType },
          { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone={row.actionType === 'Terminate' ? 'error' : 'info'} /> },
          { id: 'appeal', header: 'Appeal', render: row => <Stack direction="row" wrap>
            <Button tone="neutral" onClick={() => appeal.mutate({ id: row.id, reason: 'Employee requests independent review' })} disabled={row.appealStatus !== 'None' && row.appealStatus !== 'Rejected'}>Appeal</Button>
            {row.appealStatus === 'Open' ? <Button tone="secondary" onClick={() => resolveAppeal.mutate({ id: row.id, upheld: true })}>Uphold</Button> : null}
            {row.appealStatus === 'Open' ? <Button tone="danger" onClick={() => resolveAppeal.mutate({ id: row.id, upheld: false })}>Reverse</Button> : null}
          </Stack> }
        ]} />
      </Card>
      <Card title="Employee improvement proposal">
        <FormField label="Employee ID"><TextInput value={employeeId} onChange={setEmployeeId} /></FormField>
        <Select label="Category" value={category} onChange={setCategory} options={['process', 'skill', 'tool', 'model', 'organization'].map(value => ({ value, label: value }))} />
        <FormField label="Summary"><TextInput value={summary} onChange={setSummary} multiline minRows={2} /></FormField>
        <FormField label="Expected benefit"><TextInput value={benefit} onChange={setBenefit} /></FormField>
        <Button tone="secondary" onClick={() => propose.mutate()} disabled={!employeeId || !summary || !benefit} loading={propose.isPending}>Submit proposal</Button>
        <DataTable rows={improvements.data ?? []} getRowId={row => row.id} columns={[
          { id: 'category', header: 'Category', render: row => row.category },
          { id: 'summary', header: 'Summary', render: row => row.summary },
          { id: 'status', header: 'Status', render: row => <Stack direction="row" wrap>
            <StatusChip label={row.status} tone={row.status === 'Approved' ? 'success' : row.status === 'Rejected' ? 'error' : 'warning'} />
            {row.status === 'Proposed' ? <Button tone="secondary" onClick={() => reviewImprovement.mutate({ id: row.id, approved: true })}>Approve</Button> : null}
            {row.status === 'Proposed' ? <Button tone="danger" onClick={() => reviewImprovement.mutate({ id: row.id, approved: false })}>Reject</Button> : null}
          </Stack> }
        ]} />
      </Card>
    </Grid>
  </Stack>;
}
