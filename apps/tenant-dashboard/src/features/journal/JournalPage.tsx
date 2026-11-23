import { useQuery } from '@tanstack/react-query';
import { Card, ErrorState, LoadingState, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function JournalPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  const decision = data?.cards.find(card => card.id === 'decisions');
  return (
    <div>
      <Text variant="h1">Company Journal</Text>
      <Card title="Recent decision"><Text>{decision?.summary}</Text><Text variant="caption">{decision?.metric}</Text></Card>
      <Card title="Audit integrity"><Text>Hash chain enabled · local SQLite event store</Text></Card>
    </div>
  );
}
