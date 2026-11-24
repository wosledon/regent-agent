import { Card, EmptyState, Timeline, TimelineEntry, Text } from '@regent/ui-shared';
import type { CollaborationActivity } from '../../lib/domain-types';

interface ActivityTimelineProps {
  activities: CollaborationActivity[];
  onActivitySelect: (activity: CollaborationActivity) => void;
}

export function ActivityTimeline({ activities, onActivitySelect }: ActivityTimelineProps) {
  if (!activities.length) return <EmptyState title="No traceable activity" description="Try a different filter or send the first message." />;
  return (
    <Card title="Process timeline" action={<Text variant="caption">{activities.length} events</Text>}>
      <Timeline>
        {activities.map(activity => (
          <TimelineEntry
            key={activity.id}
            timestamp={activity.occurredAt}
            title={activity.title}
            description={`${activity.actor} · ${activity.type} · ${activity.sourceType}:${activity.sourceId}${activity.auditHash ? ` · audit ${activity.auditHash.slice(0, 12)}` : ''}`}
            status={activity.type}
            nextAction={activity.evidenceRefs.length ? `Evidence: ${activity.evidenceRefs.join(', ')}` : undefined}
            tone={activity.type === 'Audit' ? 'neutral' : activity.type === 'Message' ? 'warning' : 'info'}
            onClick={() => onActivitySelect(activity)}
          />
        ))}
      </Timeline>
    </Card>
  );
}
