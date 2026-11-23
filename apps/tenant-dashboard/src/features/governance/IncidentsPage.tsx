import { EmptyState, Text } from '@regent/ui-shared';

export function IncidentsPage() {
  return (
    <div>
      <Text variant="h1">Security Incidents</Text>
      <EmptyState title="No open incidents" description="Local incident records will appear here." />
    </div>
  );
}
