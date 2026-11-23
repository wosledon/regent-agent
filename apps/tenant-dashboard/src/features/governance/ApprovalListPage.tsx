import { useQuery } from '@tanstack/react-query';
import { Alert, CircularProgress } from '@mui/material';
import { Card, SemanticLink, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function ApprovalListPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });
  if (isLoading) return <CircularProgress aria-label="Loading approvals" />;
  if (error) return <Alert severity="error">Failed to load approvals: {error.message}</Alert>;
  const approvals = data?.actions.filter(action => action.sourceType === 'Approval') ?? [];
  return (
    <div>
      <Text variant="h1">Approvals</Text>
      {approvals.length === 0 ? <Text>No pending approvals.</Text> : null}
      {approvals.map(approval => (
        <Card key={approval.id} title={approval.title} action={<StatusChip tone="warning" label={approval.severity} />}>
          <Text>{approval.nextAction}</Text>
          <SemanticLink href={approval.href}>Review approval</SemanticLink>
        </Card>
      ))}
    </div>
  );
}
