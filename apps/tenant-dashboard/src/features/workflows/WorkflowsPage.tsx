import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Button, Card, EmptyState, ErrorState, LoadingState, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import type { WorkflowRun } from '../../lib/domain-types';

export function WorkflowsPage() {
  const [run, setRun] = useState<WorkflowRun | null>(null);
  const { data, isLoading, error } = useQuery({ queryKey: ['workflows'], queryFn: api.workflows });
  const start = useMutation({ mutationFn: (id: string) => api.startWorkflow(id), onSuccess: setRun });
  const advance = useMutation({ mutationFn: (id: string) => api.advanceWorkflow(id), onSuccess: setRun });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  return (
    <div>
      <Text variant="h1">Workflows</Text>
      {data?.length === 0 ? <EmptyState title="No workflows" /> : null}
      {data?.map(workflow => (
        <Card key={workflow.id} title={`${workflow.name} v${workflow.version}`}>
          <Text>{workflow.steps.join(' → ')}</Text>
          <Button onClick={() => start.mutate(workflow.id)} loading={start.isPending}>Start run</Button>
        </Card>
      ))}
      {run ? <Card title="Current run"><Text>{run.status} · step {run.currentStep + 1}</Text><Button onClick={() => advance.mutate(run.id)} loading={advance.isPending}>Advance</Button></Card> : null}
    </div>
  );
}
