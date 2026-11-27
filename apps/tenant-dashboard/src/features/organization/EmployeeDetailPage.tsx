import { useMemo } from 'react';
import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Avatar, Card, ContentContainer, DetailList, EmptyState, Grid, MetricCard, PageHeader,
  SemanticLink, Stack, StatusChip, Text, Timeline, TimelineGroup, type ChipTone
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedFreshnessBadge } from '../common/LocalizedFreshnessBadge';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedTimelineEntry } from '../common/LocalizedTimelineEntry';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';

function workEntryTone(status: string): ChipTone {
  switch (status.toLowerCase()) {
    case 'blocked':
    case 'failed':
      return 'error';
    case 'inreview':
    case 'pendingreview':
    case 'pending_review':
      return 'warning';
    case 'completed':
    case 'accepted':
    case 'done':
      return 'success';
    default:
      return 'info';
  }
}

export function EmployeeDetailPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const { employeeId = '' } = useParams();
  const { data, isLoading, error } = useQuery({ queryKey: ['employees', employeeId, 'timeline'], queryFn: () => api.employeeTimeline(employeeId) });
  const evidenceEntries = useMemo(() => (data?.entries ?? []).filter(entry => entry.evidenceRefs.length > 0), [data?.entries]);
  const nextActionEntries = useMemo(() => (data?.entries ?? []).filter(entry => Boolean(entry.nextAction)), [data?.entries]);
  const evidenceCount = useMemo(() => (data?.entries ?? []).reduce((total, entry) => total + entry.evidenceRefs.length, 0), [data?.entries]);

  if (isLoading) return <LocalizedLoadingState label={t('organizationPage.loadingEmployeeTimeline')} />;
  if (error) return <LocalizedErrorState error={error} />;
  if (!data) return <ContentContainer><EmptyState title={t('organizationPage.employeeNotFound')} description={t('common.notAvailable')} /></ContentContainer>;

  const { employee, entries } = data;
  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={employee.displayName}
          description={`${employee.employeeNo} · ${employee.position}`}
          actions={<Stack direction="row" spacing={2} wrap alignItems="center">
            <SemanticLink href="/organization">{t('organizationPage.organization')}</SemanticLink>
            <StatusChip label={domainLabel(employee.state)} tone={employee.state.toLowerCase() === 'active' ? 'success' : employee.state.toLowerCase() === 'busy' ? 'warning' : 'info'} />
            <LocalizedFreshnessBadge freshness={employee.freshness} />
          </Stack>}
        />
        <Card padding="small">
          <Stack direction="row" spacing={2} wrap alignItems="center">
            <Avatar name={employee.displayName} status={employee.state} size="large" />
            <Stack spacing={0.5} sx={{ flex: '1 1 280px', minWidth: 0 }}>
              <Text variant="h2">{employee.employeeNo}</Text>
              <Text variant="secondary">{employee.position}</Text>
              {employee.stateReason ? <Text variant="caption">{employee.stateReason}</Text> : null}
            </Stack>
          </Stack>
          <DetailList
            columns={2}
            dense
            items={[
              { label: t('common.employeeID'), value: employee.employeeNo, mono: true },
              { label: t('common.position'), value: employee.position },
              { label: t('common.status'), value: <Stack direction="row" spacing={1} wrap><StatusChip label={domainLabel(employee.state)} tone={employee.state.toLowerCase() === 'active' ? 'success' : employee.state.toLowerCase() === 'busy' ? 'warning' : 'info'} /><LocalizedFreshnessBadge freshness={employee.freshness} /></Stack> },
              { label: t('common.reason'), value: employee.stateReason ?? t('common.notAvailable') }
            ]}
          />
        </Card>
        <Grid columns={3}>
          <MetricCard label={t('collaborationPage.events')} value={format.number(entries.length)} hint={t('organizationPage.recentWork')} icon="activity" />
          <MetricCard label={t('common.evidence')} value={format.number(evidenceCount)} hint={t('collaborationPage.traceableProcess')} icon="audit" />
          <MetricCard label={t('common.nextAction')} value={format.number(nextActionEntries.length)} hint={t('organizationPage.workTimeline')} icon="approval" />
        </Grid>
        <Stack direction="row" wrap spacing={3} alignItems="flex-start">
          <Stack sx={{ flex: '2 1 620px', minWidth: 0 }}>
            <Card title={t('organizationPage.workTimeline')} headingLevel="h2" action={<StatusChip label={format.number(entries.length)} tone="info" />}>
              {!entries.length ? (
                <EmptyState title={t('collaborationPage.noTraceableActivity')} description={t('collaborationPage.newWorkMessagesAndAuditsWillAppearHere')} />
              ) : (
                <TimelineGroup label={t('organizationPage.recentWork')}>
                  <Timeline>
                    {entries.map(entry => (
                      <LocalizedTimelineEntry
                        key={entry.id}
                        timestampLabel={format.dateTime(entry.occurredAt)}
                        title={entry.title}
                        description={[
                          t('organizationPage.timelineDescription', {
                            amount: format.moneyMinor(toFiniteNumber(entry.costMinor), entry.currency), eventType: entry.eventType
                          }),
                          entry.taskId ? `${t('common.task')}: ${entry.taskId}` : '',
                          entry.evidenceRefs.length ? `${t('common.evidence')}: ${entry.evidenceRefs.join(', ')}` : ''
                        ].filter(Boolean).join(' · ')}
                        status={domainLabel(entry.status)}
                        nextAction={entry.nextAction ?? undefined}
                        tone={workEntryTone(entry.status)}
                      />
                    ))}
                  </Timeline>
                </TimelineGroup>
              )}
            </Card>
          </Stack>
          <Stack spacing={2} sx={{ flex: '1 1 300px', minWidth: 0 }}>
            <Card title={t('common.evidence')} headingLevel="h2" action={<StatusChip label={format.number(evidenceCount)} tone="success" />}>
              {!evidenceEntries.length ? (
                <EmptyState title={t('common.evidence')} description={t('common.notAvailable')} compact />
              ) : evidenceEntries.map(entry => (
                <Stack key={entry.id} spacing={0.5} sx={{ pb: 1.5, borderBottom: 1, borderColor: 'divider' }}>
                  <Text variant="bodyStrong">{entry.title}</Text>
                  <Text variant="mono" sx={{ overflowWrap: 'anywhere' }}>{entry.evidenceRefs.join(' · ')}</Text>
                  <Text variant="caption">{format.dateTime(entry.occurredAt)}</Text>
                </Stack>
              ))}
            </Card>
            <Card title={t('common.nextAction')} headingLevel="h2" action={<StatusChip label={format.number(nextActionEntries.length)} tone="warning" />}>
              {!nextActionEntries.length ? (
                <EmptyState title={t('common.nextAction')} description={t('common.notAvailable')} compact />
              ) : nextActionEntries.map(entry => (
                <Stack key={entry.id} spacing={0.5} sx={{ pb: 1.5, borderBottom: 1, borderColor: 'divider' }}>
                  <Text variant="bodyStrong">{entry.nextAction}</Text>
                  <Text variant="caption">{entry.title} · {format.dateTime(entry.occurredAt)}</Text>
                </Stack>
              ))}
            </Card>
          </Stack>
        </Stack>
      </Stack>
    </ContentContainer>
  );
}
