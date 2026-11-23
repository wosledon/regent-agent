import { useQuery } from '@tanstack/react-query';
import { Alert, CircularProgress, Grid } from '@mui/material';
import { Card, SemanticLink, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function EmployeeListPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['employees'], queryFn: api.employees });
  if (isLoading) return <CircularProgress aria-label="Loading employees" />;
  if (error) return <Alert severity="error">Failed to load employees: {error.message}</Alert>;
  return (
    <div>
      <Text variant="h1">Organization</Text>
      <Grid container spacing={2} sx={{ mt: 2 }}>
        {data?.map(employee => (
          <Grid key={employee.id} size={{ xs: 12, md: 6, lg: 4 }}>
            <Card title={employee.displayName} action={<StatusChip label={employee.state} tone="info" />}>
              <Text variant="caption">{employee.employeeNo} · {employee.position}</Text>
              {employee.stateReason ? <Text>{employee.stateReason}</Text> : null}
              <SemanticLink href={`/organization/employees/${employee.id}`}>View work timeline</SemanticLink>
            </Card>
          </Grid>
        ))}
      </Grid>
    </div>
  );
}
