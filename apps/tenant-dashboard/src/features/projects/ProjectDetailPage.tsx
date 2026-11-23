import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Alert, CircularProgress } from '@mui/material';
import { Card, DataTable, FreshnessBadge, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import type { components } from '../../lib/api-client/schema';

type ProjectDetail = components['schemas']['ProjectDetail'];

export function ProjectDetailPage() {
  const { projectId = '' } = useParams();
  const { data, isLoading, error } = useQuery({ queryKey: ['projects', projectId], queryFn: () => api.project(projectId) });
  if (isLoading) return <CircularProgress aria-label="Loading project" />;
  if (error) return <Alert severity="error">Failed to load project: {error.message}</Alert>;
  if (!data) return <Alert severity="warning">Project not found.</Alert>;
  return <ProjectDetailView project={data} />;
}

function ProjectDetailView({ project }: { project: ProjectDetail }) {
  return (
    <div>
      <Text variant="h1">{project.project.name}</Text>
      <StatusChip label={project.project.stage} tone="info" />
      <FreshnessBadge freshness={project.project.freshness} />
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
    </div>
  );
}
