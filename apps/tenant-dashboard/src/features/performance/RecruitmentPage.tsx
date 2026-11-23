import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState, PageHeader, Select, Stack, StatusChip, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function RecruitmentPage() {
  const queryClient = useQueryClient();
  const interviews = useQuery({ queryKey: ['recruitment-interviews'], queryFn: () => api.recruitmentInterviews() });
  const appeals = useQuery({ queryKey: ['recruitment-appeals'], queryFn: api.recruitmentAppeals });
  const audits = useQuery({ queryKey: ['fairness-audits'], queryFn: api.fairnessAudits });
  const positions = useQuery({ queryKey: ['positions'], queryFn: api.positions });
  const [candidateId, setCandidateId] = useState('');
  const [stage, setStage] = useState('Screening');
  const [score, setScore] = useState('80');
  const [evidence, setEvidence] = useState('{}');
  const [appealReason, setAppealReason] = useState('');
  const [positionId, setPositionId] = useState('');
  const [period, setPeriod] = useState('2026-11');
  const [sampleSize, setSampleSize] = useState('100');
  const [ratio, setRatio] = useState('0.9');
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['recruitment-interviews'] }),
    queryClient.invalidateQueries({ queryKey: ['recruitment-appeals'] }),
    queryClient.invalidateQueries({ queryKey: ['fairness-audits'] })
  ]);
  const record = useMutation({ mutationFn: () => api.recordInterview({ id: crypto.randomUUID(), candidateId, stage, status: 'Completed', score: Number(score), evidenceJson: evidence, createdAt: new Date().toISOString() }), onSuccess: invalidate });
  const appeal = useMutation({ mutationFn: () => api.createCandidateAppeal(candidateId, appealReason), onSuccess: async () => { setAppealReason(''); await invalidate(); } });
  const resolve = useMutation({ mutationFn: ({ id, resolution }: { id: string; resolution: string }) => api.resolveCandidateAppeal(id, resolution), onSuccess: invalidate });
  const audit = useMutation({ mutationFn: () => api.runFairnessAudit(positionId, period, Number(sampleSize), Number(ratio)), onSuccess: invalidate });
  if (interviews.isLoading || appeals.isLoading || audits.isLoading || positions.isLoading) return <LoadingState />;
  const error = interviews.error ?? appeals.error ?? audits.error ?? positions.error;
  if (error) return <ErrorState message={error.message} />;
  return <Stack spacing={3}>
    <PageHeader title="Recruitment governance" description="Interview stages, candidate appeals and fairness audits." />
    <Grid columns={2}>
      <Card title="Interview stage">
        <FormField label="Candidate ID"><TextInput value={candidateId} onChange={setCandidateId} /></FormField>
        <Select label="Stage" value={stage} onChange={setStage} options={['Screening', 'Written', 'Practical', 'Interview', 'Offer'].map(value => ({ value, label: value }))} />
        <FormField label="Score"><TextInput value={score} onChange={setScore} inputProps={{ inputMode: 'decimal' }} /></FormField>
        <FormField label="Evidence JSON"><TextInput value={evidence} onChange={setEvidence} /></FormField>
        <Button onClick={() => record.mutate()} disabled={!candidateId} loading={record.isPending}>Record stage</Button>
        <DataTable rows={interviews.data ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'candidate', header: 'Candidate', render: row => row.candidateId ?? '' },
          { id: 'stage', header: 'Stage', render: row => row.stage ?? '' },
          { id: 'score', header: 'Score', render: row => row.score ?? 0 }
        ]} />
      </Card>
      <Card title="Candidate appeal">
        <FormField label="Candidate ID"><TextInput value={candidateId} onChange={setCandidateId} /></FormField>
        <FormField label="Reason"><TextInput value={appealReason} onChange={setAppealReason} multiline minRows={2} /></FormField>
        <Button tone="secondary" onClick={() => appeal.mutate()} disabled={!candidateId || !appealReason} loading={appeal.isPending}>Open appeal</Button>
        <DataTable rows={appeals.data ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'candidate', header: 'Candidate', render: row => row.candidateId ?? '' },
          { id: 'status', header: 'Status', render: row => <StatusChip label={row.status ?? ''} tone="info" /> },
          { id: 'actions', header: 'Actions', render: row => <Button tone="neutral" onClick={() => resolve.mutate({ id: row.id ?? '', resolution: 'Retest completed' })} disabled={row.status !== 'Open'}>Resolve</Button> }
        ]} />
      </Card>
    </Grid>
    <Card title="Fairness audit">
      <Select label="Position" value={positionId} onChange={setPositionId} options={[{ value: '', label: 'Select position' }, ...(positions.data ?? []).map(position => ({ value: position.id, label: position.name }))]} />
      <FormField label="Period"><TextInput value={period} onChange={setPeriod} /></FormField>
      <FormField label="Sample size"><TextInput value={sampleSize} onChange={setSampleSize} /></FormField>
      <FormField label="Selection rate ratio"><TextInput value={ratio} onChange={setRatio} /></FormField>
      <Button onClick={() => audit.mutate()} disabled={!positionId} loading={audit.isPending}>Run audit</Button>
      <DataTable rows={audits.data ?? []} getRowId={row => row.id ?? ''} columns={[
        { id: 'position', header: 'Position', render: row => row.positionId ?? '' },
        { id: 'samples', header: 'Samples', render: row => row.sampleSize ?? 0 },
        { id: 'ratio', header: 'Ratio', render: row => row.selectionRateRatio ?? 0 },
        { id: 'status', header: 'Status', render: row => row.status ?? '' }
      ]} />
    </Card>
  </Stack>;
}
