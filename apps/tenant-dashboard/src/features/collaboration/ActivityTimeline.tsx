import { Card, EmptyState, StatusChip, Timeline } from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import type { CollaborationActivity } from '../../lib/domain-types';
import { LocalizedTimelineEntry } from '../common/LocalizedTimelineEntry';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { localizedCount } from '../../lib/localizedCount';

interface ActivityTimelineProps {
  activities: CollaborationActivity[];
  onActivitySelect: (activity: CollaborationActivity) => void;
}

export function ActivityTimeline({ activities, onActivitySelect }: ActivityTimelineProps) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  if (!activities.length) return <Card><EmptyState title={t('collaborationPage.noTraceableActivity')} description={t('collaborationPage.tryADifferentFilterOrSendTheFirstMessage')} compact /></Card>;
  return (
    <Card title={t('collaborationPage.processTimeline')} headingLevel="h2" padding="small" action={<StatusChip label={localizedCount(t, format, 'collaborationPage.events', activities.length)} tone="info" />}>
      <Timeline>
        {activities.map(activity => (
          <LocalizedTimelineEntry
            key={activity.id}
            timestampLabel={format.dateTime(activity.occurredAt)}
            title={activity.title}
            description={[
              t('collaborationPage.activityDescription', {
                actor: activity.actor, actorType: domainLabel(activity.actorType), type: domainLabel(activity.type),
                sourceType: domainLabel(activity.sourceType), sourceId: activity.sourceId,
                audit: activity.auditHash ? t('collaborationPage.auditHash', { value0: activity.auditHash.slice(0, 12) }) : ''
              }),
              activity.taskId ? `${t('common.task')}: ${activity.taskId}` : ''
            ].filter(Boolean).join(' · ')}
            status={domainLabel(activity.type)}
            nextAction={activity.evidenceRefs.length ? t('collaborationPage.evidence', { value0: activity.evidenceRefs.join(', ') }) : undefined}
            tone={activity.type === 'Audit' ? 'neutral' : activity.type === 'Message' ? 'warning' : 'info'}
            onClick={() => onActivitySelect(activity)}
          />
        ))}
      </Timeline>
    </Card>
  );
}
