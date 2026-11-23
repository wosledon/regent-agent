import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Card, DataTable, EmptyState, ErrorState, FreshnessBadge, LoadingState, PageHeader, SemanticLink, Stack, StatusChip } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import type { components } from '../../lib/api-client/schema';

type ProjectDetail = components['schemas']['ProjectDetail'];

export function ProjectDetailPage() {
  const { projectId = '' } = useParams();
  const { data, isLoading, error } = useQuery({ queryKey: ['projects', projectId], queryFn: () => api.project(projectId) });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  if (!data) return <EmptyState title="Project not found" description="Return to the project list and choose another project." />;
  return <ProjectDetailView project={data} />;
}

function ProjectDetailView({ project }: { project: ProjectDetail }) {
  return (
    <Stack spacing={3}>
      <PageHeader title={project.project.name} description="Project milestones, tasks and evidence-gated delivery." actions={<SemanticLink href={`/projects/${project.project.id}/delivery`}>Open delivery lifecycle</SemanticLink>} />
      <Stack direction="row" spacing={1}>
        <StatusChip label={project.project.stage} tone="info" />
        <FreshnessBadge freshness={project.project.freshness} />
      </Stack>
      <Card title="Milestones">
        <DataTable rows={project.milestones} getRowId={row => row.id} columns={[
          { id: 'name', header: 'Milestone', render: row => row.name },
          { id: 'target', header: 'Target', render: row => new Date(row.targetAt).toLocaleDateString() },
          { id: 'status', header: 'Status', render: row => row.status }
        ]} />
      </Card>
      <Card title="Tasks">
        <DataTable rows={project.tasks} getRowId={row => row.id} columns={[
          { id: 'title', header: 'Task', render: row => row.title },
          { id: 'type', header: 'Type', render: row => row.taskType },
          { id: 'assignee', header: 'Assignee', render: row => row.assignee },
          { id: 'status', header: 'Status', render: row => row.status }
        ]} />
      </Card>
    </Stack>
  );
}
