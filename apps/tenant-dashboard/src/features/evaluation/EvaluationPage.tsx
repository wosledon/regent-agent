import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState,
  PageHeader, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function EvaluationPage() {
  const queryClient = useQueryClient();
  const suites = useQuery({ queryKey: ['evaluation-suites'], queryFn: api.evaluationSuites });
  const runs = useQuery({ queryKey: ['evaluations'], queryFn: api.evaluations });
  const qa = useQuery({ queryKey: ['evaluation-qa'], queryFn: api.evaluationQa });
  const [candidateEmployeeId, setCandidateEmployeeId] = useState('11111111-1111-1111-1111-111111111112');
  const [repeats, setRepeats] = useState('3');
  const [sampleCount, setSampleCount] = useState('100');
  const [successes, setSuccesses] = useState('92');
  const [baselineSuccesses, setBaselineSuccesses] = useState('85');
  const [discordantA, setDiscordantA] = useState('10');
  const [discordantB, setDiscordantB] = useState('2');
  const [calibrationPassed, setCalibrationPassed] = useState('true');
  const run = useMutation({
    mutationFn: (suiteId: string) => api.runEvaluation(suiteId, candidateEmployeeId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['evaluations'] })
  });
  const gate = useMutation({
    mutationFn: () => api.runQaGate({
      suiteId: 'core', candidateEmployeeId, repeats: Number(repeats), sampleCount: Number(sampleCount),
      successes: Number(successes), baselineSuccesses: Number(baselineSuccesses),
      discordantA: Number(discordantA), discordantB: Number(discordantB), calibrationPassed: calibrationPassed === 'true'
    }), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['evaluation-qa'] })
  });

  if (suites.isLoading || runs.isLoading || qa.isLoading) return <LoadingState />;
  if (suites.error || runs.error || qa.error) return <ErrorState message={(suites.error ?? runs.error ?? qa.error)!.message} />;

  return (
    <Stack spacing={3}>
      <PageHeader title="Model evaluation" description="Versioned suites, independent judges, calibration and statistical release gates." />
      <Grid columns={2}>
        <Card title="Benchmark suites">
          {suites.data?.map(suite => (
            <Stack key={suite.id} direction="row" spacing={2}>
              <Text variant="h3">{suite.name} v{suite.version}</Text>
              <Text>{suite.caseCount} cases</Text>
              <Button onClick={() => run.mutate(suite.id)} loading={run.isPending}>Run evaluation</Button>
            </Stack>
          ))}
        </Card>
        <Card title="QA gate">
          <FormField label="Candidate employee ID"><TextInput value={candidateEmployeeId} onChange={setCandidateEmployeeId} /></FormField>
          <FormField label="Repeats"><TextInput value={repeats} onChange={setRepeats} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label="Sample count"><TextInput value={sampleCount} onChange={setSampleCount} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label="Successes"><TextInput value={successes} onChange={setSuccesses} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label="Baseline successes"><TextInput value={baselineSuccesses} onChange={setBaselineSuccesses} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label="Discordant A"><TextInput value={discordantA} onChange={setDiscordantA} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label="Discordant B"><TextInput value={discordantB} onChange={setDiscordantB} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label="Calibration passed">
            <Button tone={calibrationPassed === 'true' ? 'primary' : 'danger'} onClick={() => setCalibrationPassed(value => value === 'true' ? 'false' : 'true')}>
              {calibrationPassed === 'true' ? 'Yes' : 'No'}
            </Button>
          </FormField>
          <Button onClick={() => gate.mutate()} disabled={!candidateEmployeeId} loading={gate.isPending}>Evaluate QA gate</Button>
        </Card>
      </Grid>
      <Card title="Evaluation runs">
        <DataTable rows={runs.data ?? []} getRowId={row => row.id} columns={[
          { id: 'suite', header: 'Suite', render: row => row.suiteId },
          { id: 'candidate', header: 'Candidate', render: row => row.candidateEmployeeId },
          { id: 'score', header: 'Score', render: row => row.score },
          { id: 'safety', header: 'Safety', render: row => row.safetyPassed ? 'Pass' : 'Fail' },
          { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone={row.status === 'Approved' ? 'success' : 'error'} /> }
        ]} />
      </Card>
      <Grid columns={2}>
        <Card title="Judge calibration">
          <DataTable rows={qa.data?.calibrations ?? []} getRowId={row => `${row.sampleCount}:${row.agreement}`} columns={[
            { id: 'samples', header: 'Samples', render: row => row.sampleCount ?? 0 },
            { id: 'agreement', header: 'Agreement', render: row => `${Math.round(Number(row.agreement ?? 0) * 100)}%` },
            { id: 'kappa', header: 'Kappa', render: row => Number(row.kappa ?? 0).toFixed(2) },
            { id: 'status', header: 'Status', render: row => row.passed ? 'Pass' : 'Fail' }
          ]} />
        </Card>
        <Card title="QA gates">
          <DataTable rows={qa.data?.gates ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'suite', header: 'Suite', render: row => row.suiteId ?? '' },
            { id: 'interval', header: 'Wilson 95%', render: row => row.scoreInterval ? `${Math.round(Number(row.scoreInterval.low) * 100)}–${Math.round(Number(row.scoreInterval.high) * 100)}%` : '' },
            { id: 'contamination', header: 'Contaminated', render: row => row.contaminatedCases ?? 0 },
            { id: 'status', header: 'Status', render: row => <StatusChip label={row.status ?? ''} tone={row.status === 'Approved' ? 'success' : 'error'} /> }
          ]} />
        </Card>
      </Grid>
    </Stack>
  );
}
