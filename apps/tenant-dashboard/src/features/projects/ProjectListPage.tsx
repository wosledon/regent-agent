import { useQuery } from '@tanstack/react-query';
import { Alert, CircularProgress, Grid } from '@mui/material';
import { Card, FreshnessBadge, SemanticLink, StatusChip, Text, type ChipTone } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

const healthTone: Record<string, ChipTone> = {
  on_track: 'success', at_risk: 'warning', blocked: 'error', pending_acceptance: 'info', live: 'success', failed: 'error'
};

export function ProjectListPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['projects'], queryFn: api.projects });
  if (isLoading) return <CircularProgress aria-label="Loading projects" />;
  if (error) return <Alert severity="error">Failed to load projects: {error.message}</Alert>;
  return (
    <div>
      <Text variant="h1">Projects</Text>
      <Grid container spacing={2} sx={{ mt: 2 }}>
        {data?.map(project => (
          <Grid key={project.id} size={{ xs: 12, md: 6 }}>
            <Card title={project.name} action={<FreshnessBadge freshness={project.freshness} />}>
              <StatusChip tone={healthTone[project.health] ?? 'neutral'} label={project.health} />
              <Text>Owner: {project.owner}</Text>
              <Text variant="caption">Target: {new Date(project.targetAt).toLocaleDateString()}</Text>
              <Text>{project.currency} {project.spentMinor.toLocaleString()} / {project.budgetMinor.toLocaleString()}</Text>
              <SemanticLink href={`/projects/${project.id}`}>Open project</SemanticLink>
            </Card>
          </Grid>
        ))}
      </Grid>
    </div>
  );
}
