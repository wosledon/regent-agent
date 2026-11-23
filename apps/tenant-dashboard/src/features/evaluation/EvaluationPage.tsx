import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, ErrorState, LoadingState, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function EvaluationPage() {
  const queryClient = useQueryClient();
  const suites = useQuery({ queryKey: ['evaluation-suites'], queryFn: api.evaluationSuites });
  const runs = useQuery({ queryKey: ['evaluations'], queryFn: api.evaluations });
  const run = useMutation({
    mutationFn: (suiteId: string) => api.runEvaluation(suiteId, 'engineering'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['evaluations'] })
  });
  if (suites.isLoading || runs.isLoading) return <LoadingState />;
  if (suites.error || runs.error) return <ErrorState message={(suites.error ?? runs.error)!.message} />;
  return (
    <div>
      <Text variant="h1">Model Evaluation</Text>
      {suites.data?.map(suite => (
        <Card key={suite.id} title={`${suite.name} v${suite.version}`}>
          <Text>{suite.caseCount} cases</Text>
          <Button onClick={() => run.mutate(suite.id)} loading={run.isPending}>Run evaluation</Button>
        </Card>
      ))}
      <Card title="Runs">
        <DataTable rows={runs.data ?? []} getRowId={row => row.id} columns={[
          { id: 'suite', header: 'Suite', render: row => row.suiteId },
          { id: 'candidate', header: 'Candidate', render: row => row.candidateEmployeeId },
          { id: 'score', header: 'Score', render: row => row.score },
          { id: 'safety', header: 'Safety', render: row => row.safetyPassed ? 'Pass' : 'Fail' },
          { id: 'status', header: 'Status', render: row => row.status }
        ]} />
      </Card>
    </div>
  );
}
