import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Card,
  ContentContainer,
  Grid,
  MetricCard,
  PageHeader,
  Stack,
  StatusChip,
  Text,
  type ChipTone
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { toFiniteNumber } from '../../lib/formatting';

function performanceStatusTone(status: string): ChipTone {
  const normalized = status.trim().toLowerCase();
  if (['on track', 'ontrack', 'healthy', 'achieved', 'met', 'success', 'active'].includes(normalized)) return 'success';
  if (['at risk', 'warning', 'pending', 'watch', 'stale'].includes(normalized)) return 'warning';
  if (['off track', 'offtrack', 'critical', 'failed', 'breach', 'blocked'].includes(normalized)) return 'error';
  return 'info';
}

export function PerformancePage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const { data, isLoading, error } = useQuery({ queryKey: ['kpis'], queryFn: api.kpis });
  const snapshots = data ?? [];
  const onTarget = snapshots.filter(row => {
    const value = toFiniteNumber(row.value);
    const target = toFiniteNumber(row.target);
    return value !== null && target !== null && value >= target;
  }).length;
  const attention = snapshots.filter(row => ['warning', 'error'].includes(performanceStatusTone(row.status))).length;

  if (isLoading) return <LocalizedLoadingState />;
  if (error) return <LocalizedErrorState error={error} />;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('performancePage.performance')}
          meta={<StatusChip label={t('performancePage.metric')} tone="neutral" />}
        />
        <Grid columns={3}>
          <MetricCard
            label={t('performancePage.metric')}
            value={format.number(snapshots.length)}
            hint={t('performancePage.performance')}
            icon="performance"
          />
          <MetricCard
            label={t('performancePage.target')}
            value={format.number(onTarget)}
            hint={t('performancePage.value')}
            icon="success"
            tone={onTarget > 0 ? 'success' : 'default'}
          />
          <MetricCard
            label={t('common.status')}
            value={format.number(attention)}
            hint={t('performancePage.subject')}
            icon="activity"
            tone={attention > 0 ? 'warning' : 'default'}
          />
        </Grid>
        <Card padding="none">
          <LocalizedDataTable
            rows={snapshots}
            getRowId={row => row.id}
            density="compact"
            minWidth={880}
            ariaLabel={t('performancePage.performance')}
            emptyTitle={t('performancePage.noKPISnapshots')}
            columns={[
              { id: 'metric', header: t('performancePage.metric'), render: row => <Text variant="bodyStrong">{row.metricKey}</Text> },
              { id: 'subject', header: t('performancePage.subject'), render: row => t('performancePage.subjectValue', { type: domainLabel(row.subjectType), id: row.subjectId }) },
              { id: 'value', header: t('performancePage.value'), numeric: true, render: row => format.number(toFiniteNumber(row.value)) },
              { id: 'target', header: t('performancePage.target'), numeric: true, render: row => {
                const target = toFiniteNumber(row.target);
                return target === null ? row.target || t('common.notAvailable') : format.number(target);
              } },
              { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={performanceStatusTone(row.status)} /> }
            ]}
          />
        </Card>
      </Stack>
    </ContentContainer>
  );
}
