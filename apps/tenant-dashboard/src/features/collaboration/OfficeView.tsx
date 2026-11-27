import {
  Avatar, Button, Card, EmptyState, Icon, Stack, StatusChip, Text
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import type { CollaborationNode } from '../../lib/domain-types';
import type { CollaborationDisplayNode } from './localizedCollaboration';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { localizedCount } from '../../lib/localizedCount';

interface OfficeViewProps {
  nodes: CollaborationDisplayNode[];
  selectedId?: string | null;
  onSelect: (node: CollaborationNode) => void;
}

interface RoomButtonProps {
  node: CollaborationDisplayNode;
  selected: boolean;
  onSelect: (node: CollaborationNode) => void;
  ariaLabel: string;
}

function RoomButton({ node, selected, onSelect, ariaLabel }: RoomButtonProps) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  return (
    <Button
      tone={selected ? 'primary' : 'neutral'}
      onClick={() => onSelect(node)}
      aria-label={ariaLabel}
      aria-pressed={selected}
      startIcon={<Icon name={node.type === 'Conversation' ? 'messages' : 'projects'} />}
      sx={{
        justifyContent: 'flex-start',
        textAlign: 'left',
        height: 'auto',
        minHeight: 64,
        p: 1.5,
        width: '100%',
        maxWidth: '100%',
        borderColor: selected ? 'primary.main' : 'divider'
      }}
    >
      <Stack spacing={0.25} alignItems="flex-start" sx={{ minWidth: 0 }}>
        <Text variant="h3">{node.label}</Text>
        <Text variant="caption">{node.subtitle}</Text>
        <Text variant="caption">{localizedCount(t, format, 'collaborationPage.traceableEvents2', node.activityCount, { status: domainLabel(node.status) })}</Text>
      </Stack>
    </Button>
  );
}

export function OfficeView({ nodes, selectedId, onSelect }: OfficeViewProps) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const employees = nodes.filter(node => node.type === 'Employee');
  const conversations = nodes.filter(node => node.type === 'Conversation');
  const projects = nodes.filter(node => node.type === 'Project');
  const rooms = new Map<string, CollaborationDisplayNode[]>();
  for (const employee of employees) {
    rooms.set(employee.room, [...(rooms.get(employee.room) ?? []), employee]);
  }
  if (!employees.length) return <EmptyState title={t('collaborationPage.noEmployeesYet')} description={t('collaborationPage.startTheCompanyToOpenTheOffice')} />;

  return (
    <Stack direction="row" wrap spacing={2} alignItems="flex-start">
      <Stack sx={{ flex: '2 1 620px', minWidth: 0 }}>
        <Card title={t('collaborationPage.officeFloor')} headingLevel="h2" action={<StatusChip label={localizedCount(t, format, 'collaborationPage.employeeCount', employees.length)} tone="info" />}>
          <Stack spacing={2}>
            {[...rooms.entries()].map(([room, members]) => (
              <Stack key={room} spacing={1.5} sx={{ minWidth: 0, p: 2, border: 1, borderColor: 'divider', borderRadius: 2, bgcolor: 'background.default' }}>
                <Stack direction="row" spacing={1} alignItems="center" wrap>
                  <Icon name="organization" sx={{ color: 'text.secondary' }} />
                  <Text variant="h3">{room}</Text>
                  <StatusChip label={localizedCount(t, format, 'collaborationPage.seatCount', members.length)} tone="neutral" />
                </Stack>
                <Stack spacing={1}>
                  {members.map(employee => (
                    <Button
                      key={employee.id}
                      tone={selectedId === employee.id ? 'primary' : 'neutral'}
                      onClick={() => onSelect(employee)}
                      startIcon={<Avatar name={employee.label} status={employee.status} size="small" />}
                      sx={{ justifyContent: 'flex-start', textAlign: 'left', height: 'auto', minHeight: 64, p: 1.25, width: '100%', maxWidth: '100%' }}
                      aria-label={t('collaborationPage.inspectEmployee', { name: employee.label })}
                      aria-pressed={selectedId === employee.id}
                    >
                      <Stack spacing={0.25} alignItems="flex-start" sx={{ minWidth: 0 }}>
                        <Stack direction="row" spacing={1} wrap alignItems="center">
                          <Text variant="h3">{employee.label}</Text>
                          <StatusChip label={domainLabel(employee.status)} tone={employee.status === 'Busy' ? 'warning' : employee.status === 'Blocked' ? 'error' : 'success'} />
                        </Stack>
                        <Text variant="caption">{employee.subtitle}</Text>
                        <Text variant="caption">{localizedCount(t, format, 'collaborationPage.traceableEvents2', employee.activityCount, { status: domainLabel(employee.status) })}</Text>
                      </Stack>
                    </Button>
                  ))}
                </Stack>
              </Stack>
            ))}
          </Stack>
        </Card>
      </Stack>
      <Stack spacing={2} sx={{ flex: '1 1 300px', minWidth: 0 }}>
        <Card title={t('collaborationPage.meetingRooms')} headingLevel="h2" action={<StatusChip label={localizedCount(t, format, 'collaborationPage.groupCount', conversations.length)} tone="warning" />}>
          {conversations.length ? conversations.map(conversation => (
            <RoomButton
              key={conversation.id}
              node={conversation}
              selected={selectedId === conversation.id}
              onSelect={onSelect}
              ariaLabel={t('collaborationPage.graphNodeLabel', { name: conversation.label, subtitle: conversation.subtitle })}
            />
          )) : <EmptyState title={t('collaborationPage.noMeetingRooms')} description={t('collaborationPage.createAGroupToStartASharedConversation')} compact />}
        </Card>
        <Card title={t('collaborationPage.projectRooms')} headingLevel="h2" action={<StatusChip label={localizedCount(t, format, 'collaborationPage.projectCount', projects.length)} tone="success" />}>
          {projects.length ? projects.map(project => (
            <RoomButton
              key={project.id}
              node={project}
              selected={selectedId === project.id}
              onSelect={onSelect}
              ariaLabel={t('collaborationPage.graphNodeLabel', { name: project.label, subtitle: project.subtitle })}
            />
          )) : <EmptyState title={t('collaborationPage.noProjects')} description={t('collaborationPage.projectWorkWillAppearHere')} compact />}
        </Card>
      </Stack>
    </Stack>
  );
}
