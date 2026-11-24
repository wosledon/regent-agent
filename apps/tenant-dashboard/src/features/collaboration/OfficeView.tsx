import { Box } from '@mui/material';
import { Avatar, Button, Card, EmptyState, Stack, StatusChip, Text } from '@regent/ui-shared';
import type { CollaborationNode } from '../../lib/domain-types';

interface OfficeViewProps {
  nodes: CollaborationNode[];
  selectedId?: string | null;
  onSelect: (node: CollaborationNode) => void;
}

export function OfficeView({ nodes, selectedId, onSelect }: OfficeViewProps) {
  const employees = nodes.filter(node => node.type === 'Employee');
  const conversations = nodes.filter(node => node.type === 'Conversation');
  const projects = nodes.filter(node => node.type === 'Project');
  const rooms = new Map<string, CollaborationNode[]>();
  for (const employee of employees) {
    const room = employee.subtitle.split('·')[1]?.trim() || 'Unassigned';
    rooms.set(room, [...(rooms.get(room) ?? []), employee]);
  }
  if (!employees.length) return <EmptyState title="No employees yet" description="Start the company to open the office." />;

  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', xl: 'minmax(0, 2fr) minmax(280px, 1fr)' }, gap: 2 }}>
      <Card title="Office floor" action={<StatusChip label={`${employees.length} employees`} tone="info" />}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
          {[...rooms.entries()].map(([room, members]) => (
            <Box key={room} sx={{ minWidth: 0, p: 2, border: '1px dashed', borderColor: 'divider', borderRadius: 3, bgcolor: 'background.default' }}>
              <Stack direction="row" spacing={1} sx={{ mb: 2 }}>
                <Text variant="h3">{room}</Text>
                <StatusChip label={`${members.length} seats`} tone="neutral" />
              </Stack>
              <Stack spacing={1}>
                {members.map(employee => (
                  <Button
                    key={employee.id}
                    tone={selectedId === employee.id ? 'primary' : 'neutral'}
                    onClick={() => onSelect(employee)}
                    sx={{ justifyContent: 'flex-start', textAlign: 'left', height: 'auto', p: 1.5, width: '100%', maxWidth: '100%' }}
                    aria-label={`Inspect ${employee.label}`}
                  >
                    <Stack direction="row" spacing={1.5} alignItems="center">
                      <Avatar name={employee.label} status={employee.status} />
                      <Stack spacing={0}>
                        <Text variant="h3">{employee.label}</Text>
                        <Text variant="caption">{employee.subtitle}</Text>
                        <Text variant="caption">{employee.status} · {employee.activityCount} traceable events</Text>
                      </Stack>
                    </Stack>
                  </Button>
                ))}
              </Stack>
            </Box>
          ))}
        </Box>
      </Card>
      <Stack spacing={2}>
        <Card title="Meeting rooms" action={<StatusChip label={`${conversations.length} groups`} tone="warning" />}>
          {conversations.length ? conversations.map(conversation => (
            <Button key={conversation.id} tone={selectedId === conversation.id ? 'primary' : 'neutral'} onClick={() => onSelect(conversation)} sx={{ justifyContent: 'flex-start' }}>
              <Stack spacing={0} alignItems="flex-start">
                <Text variant="h3">{conversation.label}</Text>
                <Text variant="caption">{conversation.subtitle}</Text>
              </Stack>
            </Button>
          )) : <EmptyState title="No meeting rooms" description="Create a group to start a shared conversation." />}
        </Card>
        <Card title="Project rooms" action={<StatusChip label={`${projects.length} projects`} tone="success" />}>
          {projects.length ? projects.map(project => (
            <Button key={project.id} tone={selectedId === project.id ? 'primary' : 'neutral'} onClick={() => onSelect(project)} sx={{ justifyContent: 'flex-start' }}>
              <Stack spacing={0} alignItems="flex-start">
                <Text variant="h3">{project.label}</Text>
                <Text variant="caption">{project.subtitle}</Text>
              </Stack>
            </Button>
          )) : <EmptyState title="No projects" description="Project work will appear here." />}
        </Card>
      </Stack>
    </Box>
  );
}
