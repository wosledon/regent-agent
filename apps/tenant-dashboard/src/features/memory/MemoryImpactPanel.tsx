import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, Card, DataTable, EmptyState, ErrorState, FormField, LoadingState, Select, Stack, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

interface MemoryImpactPanelProps {
  claimId: string;
  onChanged: () => void | Promise<void>;
}

export function MemoryImpactPanel({ claimId, onChanged }: MemoryImpactPanelProps) {
  const [targetType, setTargetType] = useState('Task');
  const [targetId, setTargetId] = useState('');
  const [risk, setRisk] = useState('High');
  const [requiredAction, setRequiredAction] = useState('Invalidate context and re-retrieve');
  const impacts = useQuery({
    queryKey: ['memory-impact', claimId],
    queryFn: () => api.memoryImpact(claimId),
    enabled: Boolean(claimId)
  });

  if (!claimId) return <Card title="Impact graph"><EmptyState title="Select a recalled claim" description="Choose a search hit to inspect where the claim was used." /></Card>;
  if (impacts.isLoading) return <LoadingState />;
  if (impacts.error) return <ErrorState message={impacts.error.message} />;

  return (
    <Card title="Impact graph">
      <Stack spacing={2}>
        <TextInput value={claimId} readOnly placeholder="Claim ID" />
        <Select label="Target type" value={targetType} onChange={setTargetType} options={[
          { value: 'Task', label: 'Task' },
          { value: 'Summary', label: 'Summary' },
          { value: 'Decision', label: 'Decision' },
          { value: 'Review', label: 'Review' },
          { value: 'Release', label: 'Release' },
          { value: 'ExternalAction', label: 'External action' },
          { value: 'Context', label: 'Context' }
        ]} />
        <FormField label="Target ID"><TextInput value={targetId} onChange={setTargetId} placeholder="task-123" /></FormField>
        <FormField label="Risk"><TextInput value={risk} onChange={setRisk} /></FormField>
        <FormField label="Required action"><TextInput value={requiredAction} onChange={setRequiredAction} /></FormField>
        <Button tone="secondary" disabled={!targetId.trim()} onClick={recordImpact}>Record impact</Button>
        {impacts.data?.length === 0 ? <EmptyState title="No recorded impact" /> : null}
        <DataTable rows={impacts.data ?? []} getRowId={row => row.id} columns={[
          { id: 'target', header: 'Target', render: row => `${row.targetType}:${row.targetId}` },
          { id: 'version', header: 'Version', render: row => row.claimVersion },
          { id: 'risk', header: 'Risk', render: row => row.risk },
          { id: 'action', header: 'Required action', render: row => row.requiredAction },
          { id: 'used', header: 'Used at', render: row => new Date(row.usedAt).toLocaleString() }
        ]} />
      </Stack>
    </Card>
  );

  async function recordImpact() {
    await api.recordMemoryImpact(claimId, { targetType, targetId, risk, requiredAction });
    setTargetId('');
    await onChanged();
  }
}
