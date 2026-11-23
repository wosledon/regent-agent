import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Alert, CircularProgress } from '@mui/material';
import { Card, StatusChip, Text, Timeline, TimelineEntry, TimelineGroup } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function EmployeeDetailPage() {
  const { employeeId = '' } = useParams();
  const { data, isLoading, error } = useQuery({ queryKey: ['employees', employeeId, 'timeline'], queryFn: () => api.employeeTimeline(employeeId) });
  if (isLoading) return <CircularProgress aria-label="Loading employee timeline" />;
  if (error) return <Alert severity="error">Failed to load employee timeline: {error.message}</Alert>;
  if (!data) return <Alert severity="warning">Employee not found.</Alert>;
  return (
    <div>
      <Text variant="h1">{data.employee.displayName}</Text>
      <Text variant="caption">{data.employee.employeeNo} · {data.employee.position}</Text>
      <StatusChip label={data.employee.state} tone="info" />
      <Card title="Work timeline">
        <TimelineGroup label="Recent work">
          <Timeline>
            {data.entries.map(entry => (
              <TimelineEntry
                key={entry.id}
                timestamp={entry.occurredAt}
                title={entry.title}
                description={`${entry.currency} ${entry.costMinor.toLocaleString()} · ${entry.eventType}`}
                status={entry.status}
                nextAction={entry.nextAction ?? undefined}
                tone={entry.status === 'Blocked' ? 'error' : entry.status === 'InReview' ? 'warning' : 'info'}
              />
            ))}
          </Timeline>
        </TimelineGroup>
      </Card>
    </div>
  );
}
