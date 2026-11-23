import { useQuery } from '@tanstack/react-query';
import { Card, EmptyState, ErrorState, LoadingState, SemanticLink, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function ActionCenterPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  if (!data?.actions.length) return <EmptyState title="No open actions" description="The company has no pending escalations." />;
  return (
    <div>
      <Text variant="h1">Action Center</Text>
      {data.actions.map(action => (
        <Card key={`${action.sourceType}-${action.id}`} title={action.title} action={<StatusChip tone={action.severity === 'critical' ? 'error' : 'warning'} label={action.severity} />}>
          <Text>{action.nextAction}</Text>
          <Text variant="caption">Owner: {action.owner} · Source: {action.sourceType}</Text>
          <SemanticLink href={action.href}>Open</SemanticLink>
        </Card>
      ))}
    </div>
  );
}
