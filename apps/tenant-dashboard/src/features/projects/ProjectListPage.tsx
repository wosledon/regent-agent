import { useQuery } from '@tanstack/react-query';
import { Card, EmptyState, ErrorState, FreshnessBadge, Grid, LoadingState, PageHeader, SemanticLink, Stack, StatusChip, Text, type ChipTone } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

const healthTone: Record<string, ChipTone> = {
  on_track: 'success', at_risk: 'warning', blocked: 'error', pending_acceptance: 'info', live: 'success', failed: 'error'
};

export function ProjectListPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['projects'], queryFn: api.projects });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  if (!data?.length) return <EmptyState title="No projects" description="Create a project from the company charter to begin delivery." />;
  return (
    <Stack spacing={3}>
      <PageHeader title="Projects" description="Deterministic health, delivery freshness and evidence-gated progress." />
      <Grid columns={2}>
        {data.map(project => (
          <Card key={project.id} title={project.name} action={<FreshnessBadge freshness={project.freshness} />}>
            <StatusChip tone={healthTone[project.health] ?? 'neutral'} label={project.health} />
            <Text>Owner: {project.owner}</Text>
            <Text variant="caption">Target: {new Date(project.targetAt).toLocaleDateString()}</Text>
            <Text>{project.currency} {project.spentMinor.toLocaleString()} / {project.budgetMinor.toLocaleString()}</Text>
            <SemanticLink href={`/projects/${project.id}`}>Open project</SemanticLink>
          </Card>
        ))}
      </Grid>
    </Stack>
  );
}
