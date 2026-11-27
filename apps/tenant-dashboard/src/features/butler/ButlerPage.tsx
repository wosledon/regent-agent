import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ContentContainer, DetailList, FormField, FreshnessBadge, Grid,
  MetricCard, PageHeader, SectionHeader, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';

export function ButlerPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const reports = useQuery({ queryKey: ['butler-reports'], queryFn: api.butlerReports });
  const schedules = useQuery({ queryKey: ['butler-schedules'], queryFn: api.butlerSchedules });
  const [question, setQuestion] = useState(() => t('butlerPage.defaultQuestion'));
  const [answer, setAnswer] = useState<Awaited<ReturnType<typeof api.butlerAsk>> | null>(null);
  const [kind, setKind] = useState('Daily');
  const [frequency, setFrequency] = useState('Daily');
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['butler-reports'] }),
    queryClient.invalidateQueries({ queryKey: ['butler-schedules'] })
  ]);
  const ask = useMutation({ mutationFn: () => api.butlerAsk(question), onSuccess: setAnswer });
  const generate = useMutation({ mutationFn: () => api.generateButlerReport(kind), onSuccess: invalidate });
  const retry = useMutation({ mutationFn: (id: string) => api.retryButlerReport(id), onSuccess: invalidate });
  const saveSchedule = useMutation({
    mutationFn: (enabled: boolean) => api.saveButlerSchedule(kind, frequency, enabled), onSuccess: invalidate
  });
  if (reports.isLoading || schedules.isLoading) return <LocalizedLoadingState />;
  if (reports.error || schedules.error) return <LocalizedErrorState error={(reports.error ?? schedules.error)} />;

  const reportRows = reports.data ?? [];
  const scheduleRows = schedules.data ?? [];
  const completedReports = reportRows.filter(report => report.status === 'Completed').length;
  const retryableReports = reportRows.filter(report => report.status !== 'Completed').length;
  const enabledSchedules = scheduleRows.filter(schedule => schedule.enabled).length;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('butlerPage.butler')}
          description={t('butlerPage.askAboutTheCompanyAndManageDeterministicScheduledReports')}
          meta={<FreshnessBadge freshness="unknown" label={t('common.freshness.unknown')} />}
        />
        <Grid columns={4}>
          <MetricCard label={t('butlerPage.reports')} value={format.number(reportRows.length)} icon="journal" />
          <MetricCard label={t('common.domain.completed')} value={format.number(completedReports)} tone="success" icon="success" />
          <MetricCard label={t('common.domain.backlog')} value={format.number(retryableReports)} tone={retryableReports > 0 ? 'warning' : 'default'} icon="refresh" />
          <MetricCard label={t('butlerPage.reportSchedule')} value={format.number(enabledSchedules)} tone={enabledSchedules > 0 ? 'info' : 'default'} icon="schedule" />
        </Grid>
        <Grid columns={2}>
          <Card title={t('butlerPage.askTheButler')} headingLevel="h2" padding="small">
            <Stack spacing={2}>
              <FormField label={t('butlerPage.question')}>
                <TextInput value={question} onChange={setQuestion} multiline minRows={3} placeholder={t('butlerPage.question')} />
              </FormField>
              <Stack direction="row" spacing={1.5} wrap alignItems="center">
                <Button size="small" onClick={() => ask.mutate()} disabled={!question} loading={ask.isPending}>{t('butlerPage.ask')}</Button>
                <LocalizedMutationFeedback mutation={ask} showSuccess={false} />
              </Stack>
              {answer ? (
                <Card tone="info" padding="small">
                  <Stack spacing={1.5}>
                    <StatusChip label={t('butlerPage.intent', { value0: domainLabel(answer.intent?.trim() || 'summary') })} tone="info" />
                    <Text variant="bodyStrong">{answer.answer || t('common.notAvailable')}</Text>
                    <DetailList dense columns={1} items={[
                      { label: t('common.evidence'), value: (answer.evidenceRefs ?? []).join(' · ') || t('common.notAvailable'), mono: true }
                    ]} />
                  </Stack>
                </Card>
              ) : null}
            </Stack>
          </Card>
          <Card title={t('butlerPage.reportSchedule')} headingLevel="h2" padding="small">
            <Stack spacing={2}>
              <Grid columns={2}>
                <Select label={t('butlerPage.reportKind')} value={kind} onChange={setKind} options={['Daily', 'Weekly', 'Budget', 'Incident', 'Custom'].map(value => ({ value, label: domainLabel(value) }))} />
                <Select label={t('common.frequency')} value={frequency} onChange={setFrequency} options={['Daily', 'Weekly', 'Manual'].map(value => ({ value, label: domainLabel(value) }))} />
              </Grid>
              <Stack direction="row" wrap spacing={1}>
                <Button size="small" onClick={() => saveSchedule.mutate(true)} loading={saveSchedule.isPending}>{t('butlerPage.enable')}</Button>
                <Button tone="neutral" size="small" onClick={() => saveSchedule.mutate(false)} loading={saveSchedule.isPending}>{t('butlerPage.disable')}</Button>
                <Button tone="secondary" size="small" onClick={() => generate.mutate()} loading={generate.isPending}>{t('butlerPage.generateNow')}</Button>
              </Stack>
              <LocalizedMutationFeedback mutation={saveSchedule} successMessage={t('butlerPage.reportScheduleSaved')} />
              <LocalizedMutationFeedback mutation={generate} successMessage={t('butlerPage.reportGenerated')} />
              <LocalizedDataTable rows={scheduleRows} getRowId={row => row.id ?? ''} density="compact" minWidth={520} columns={[
                { id: 'kind', header: t('butlerPage.kind'), render: row => domainLabel(row.kind ?? 'unknown') },
                { id: 'frequency', header: t('common.frequency'), render: row => domainLabel(row.frequency ?? 'unknown') },
                { id: 'enabled', header: t('common.status'), render: row => <StatusChip label={row.enabled ? t('common.enabled') : t('common.disabled')} tone={row.enabled ? 'success' : 'neutral'} /> },
                { id: 'next', header: t('butlerPage.nextRun'), render: row => row.nextRunAt ? format.dateTime(row.nextRunAt) : domainLabel('Manual') }
              ]} />
            </Stack>
          </Card>
        </Grid>
        <Stack spacing={2}>
          <SectionHeader title={t('butlerPage.reports')} action={<StatusChip label={format.number(reportRows.length)} tone={retryableReports > 0 ? 'warning' : 'neutral'} />} />
          <Card padding="small">
            <LocalizedDataTable rows={reportRows} getRowId={row => row.id ?? ''} density="compact" minWidth={980} emptyTitle={t('butlerPage.noReportsYet')} emptyDescription={t('butlerPage.generateAReportOrWaitForTheScheduler')} columns={[
              { id: 'title', header: t('butlerPage.report'), render: row => <Stack spacing={0.25}><Text variant="bodyStrong">{row.title ?? t('common.notAvailable')}</Text><Text variant="caption">{domainLabel(row.kind ?? 'unknown')}</Text></Stack> },
              { id: 'summary', header: t('common.summary'), render: row => row.summary ?? t('common.notAvailable') },
              { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status ?? 'unknown')} tone={row.status === 'Completed' ? 'success' : row.status === 'Failed' ? 'error' : 'warning'} /> },
              { id: 'evidence', header: t('common.evidence'), render: row => <Text variant="mono">{(row.evidenceRefs ?? []).join(' · ') || t('common.notAvailable')}</Text> },
              { id: 'created', header: t('common.created'), render: row => format.dateTime(row.createdAt) },
              { id: 'actions', header: t('common.actions'), render: row => <Stack spacing={0.5}>
                <Text variant="caption">{t('common.nextAction', { action: row.status === 'Completed' ? t('common.domain.completed') : t('common.retry') })}</Text>
                <Button tone="neutral" size="small" onClick={() => retry.mutate(row.id ?? '')} disabled={row.status === 'Completed' || retry.isPending} loading={retry.isPending && retry.variables === row.id}>{t('common.retry')}</Button>
              </Stack> }
            ]} />
            <LocalizedMutationFeedback mutation={retry} successMessage={t('butlerPage.reportRetried')} />
          </Card>
        </Stack>
      </Stack>
    </ContentContainer>
  );
}
