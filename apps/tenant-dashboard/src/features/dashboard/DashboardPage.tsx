import { useQuery } from '@tanstack/react-query';
import { Alert, CircularProgress, Grid } from '@mui/material';
import { Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import { DashboardCard } from './DashboardCard';

export function DashboardPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });
  if (isLoading) return <CircularProgress aria-label="Loading dashboard" />;
  if (error) return <Alert severity="error">Failed to load dashboard: {error.message}</Alert>;
  if (!data) return null;
  return (
    <div>
      <Text variant="h1">{data.companyName}</Text>
      <Text variant="caption">Generated at {new Date(data.generatedAt).toLocaleString()}</Text>
      <Grid container spacing={2} sx={{ mt: 2 }}>
        {data.cards.map(card => <Grid key={card.id} size={{ xs: 12, md: 6, lg: 4 }}><DashboardCard card={card} /></Grid>)}
      </Grid>
    </div>
  );
}
