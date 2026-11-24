import { useQuery } from '@tanstack/react-query';
import { Card, EmptyState, ErrorState, Grid, LoadingState, PageHeader, SemanticLink, Stack, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function EmployeeListPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['employees'], queryFn: api.employees });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  if (!data?.length) return <EmptyState title="No employees" description="Start the company to create the first Controller." />;
  return (
    <Stack spacing={3}>
      <PageHeader title="Organization" description="Employee work, state and permanent employment identity." actions={<Stack direction="row" spacing={2}><SemanticLink href="/organization/collaboration">Collaboration workspace</SemanticLink><SemanticLink href="/organization/manage">Manage organization</SemanticLink></Stack>} />
      <Grid columns={3}>
        {data.map(employee => (
          <Card key={employee.id} title={employee.displayName} action={<StatusChip label={employee.state} tone="info" />}>
            <Text variant="caption">{employee.employeeNo} · {employee.position}</Text>
            {employee.stateReason ? <Text>{employee.stateReason}</Text> : null}
            <SemanticLink href={`/organization/employees/${employee.id}`}>View work timeline</SemanticLink>
          </Card>
        ))}
      </Grid>
    </Stack>
  );
}
