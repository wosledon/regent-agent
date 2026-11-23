import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, CircularProgress } from '@mui/material';
import { Button, Card, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function ApprovalPage() {
  const { approvalId = '' } = useParams();
  const queryClient = useQueryClient();
  const [reason, setReason] = useState('Reviewed from Tenant Dashboard.');
  const { data, isLoading, error } = useQuery({ queryKey: ['approvals', approvalId], queryFn: () => api.approval(approvalId) });
  const mutation = useMutation({
    mutationFn: (decision: string) => api.decideApproval(approvalId, { decision, reason }, crypto.randomUUID()),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['approvals'] });
      await queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    }
  });

  if (isLoading) return <CircularProgress aria-label="Loading approval" />;
  if (error) return <Alert severity="error">Failed to load approval: {error.message}</Alert>;
  if (!data) return <Alert severity="warning">Approval not found.</Alert>;
  return (
    <Card title={data.title} action={<StatusChip label={data.state} tone={data.state === 'pending' ? 'warning' : 'info'} />}>
      <Text variant="h2">{data.actionType}</Text>
      <Text>{data.impact}</Text>
      <Text variant="caption">Requester: {data.requester} · Expires: {new Date(data.expiresAt).toLocaleString()}</Text>
      <Text variant="caption">Evidence: {data.evidenceRefs.join(', ')}</Text>
      <TextInput value={reason} onChange={setReason} placeholder="Decision reason" />
      {mutation.error ? <Alert severity="error">{mutation.error.message}</Alert> : null}
      <Button tone="secondary" disabled={data.state !== 'pending' || mutation.isPending} onClick={() => mutation.mutate('approve')}>Approve</Button>
      <Button tone="danger" disabled={data.state !== 'pending' || mutation.isPending} onClick={() => mutation.mutate('reject')}>Reject</Button>
    </Card>
  );
}
