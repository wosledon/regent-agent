import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Card,
  DataTable,
  EmptyState,
  FormField,
  Grid,
  MetricCard,
  PageHeader,
  Select,
  Stack,
  StatusChip,
  Text,
  TextInput
} from '@regent/ui-shared';
import { LocalizedErrorState, LocalizedLoadingState } from '../components/LocalizedStates';
import { api, type AlertRecord, type ObservabilityProfile, type SloDefinition } from '../lib/api';
import { useDomainLabels } from '../lib/domainLabels';
import { localizedOpsErrorMessage } from '../lib/errorMessages';
import { useOpsFormatter } from '../lib/formatting';
import { localizedCount } from '../lib/localizedCount';

export function ObservabilityPage({ canWrite = false }: { canWrite?: boolean }) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useOpsFormatter();
  const queryClient = useQueryClient();
  const profile = useQuery({ queryKey: ['observability'], queryFn: api.observability });
  const elasticsearch = useQuery({ queryKey: ['elasticsearch-status'], queryFn: api.elasticsearch });
  const evaluations = useQuery({ queryKey: ['slo-evaluations'], queryFn: api.evaluateSlos });
  const alerts = useQuery({ queryKey: ['observability-alerts'], queryFn: api.alerts });
  const [draft, setDraft] = useState<ObservabilityProfile | null>(null);
  const [sloName, setSloName] = useState('api_availability');
  const [metric, setMetric] = useState('api.success');
  const [target, setTarget] = useState('0.99');
  const [fingerprint, setFingerprint] = useState('');
  const [silenceReason, setSilenceReason] = useState('');
  const invalidateAlerts = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['observability-alerts'] }),
    queryClient.invalidateQueries({ queryKey: ['slo-evaluations'] })
  ]);
  const acknowledge = useMutation({ mutationFn: api.acknowledgeAlert, onSuccess: invalidateAlerts });
  const resolve = useMutation({ mutationFn: (id: string) => api.resolveAlert(id, 'Recovered and verified by SRE'), onSuccess: invalidateAlerts });
  const silence = useMutation({
    mutationFn: ({ fingerprint: value, reason }: { fingerprint: string; reason: string }) => api.silenceAlert(value, reason, 60),
    onSuccess: async () => { setFingerprint(''); setSilenceReason(''); await invalidateAlerts(); }
  });
  const save = useMutation({
    mutationFn: (value: ObservabilityProfile) => api.saveObservability(value),
    onSuccess: data => { setDraft(null); queryClient.setQueryData(['observability'], data); }
  });
  const saveSlo = useMutation({
    mutationFn: (slo: SloDefinition) => api.saveSlo(slo),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['slo-evaluations'] })
  });
  const value = draft ?? profile.data;
  const alertMutationError = silence.error ?? acknowledge.error ?? resolve.error;
  const mutationError = alertMutationError ?? save.error ?? saveSlo.error;
  const activeAlerts = (alerts.data ?? []).filter(alert => alert.status !== 'Resolved');
  const numericTarget = Number(target);
  const validSlo = !!sloName.trim() && !!metric.trim() && Number.isFinite(numericTarget) && numericTarget > 0 && numericTarget <= 1;
  const successMessages = [
    silence.isSuccess ? t('observabilityPage.silenceSuccess', { duration: format.duration(60, 'minute') }) : null,
    acknowledge.isSuccess ? t('observabilityPage.acknowledgeSuccess') : null,
    resolve.isSuccess ? t('observabilityPage.resolveSuccess') : null,
    save.isSuccess ? t('observabilityPage.profileSaved') : null,
    saveSlo.isSuccess ? t('observabilityPage.sloSaved') : null
  ].filter((message): message is string => Boolean(message));
  const alertCount = alerts.data
    ? localizedCount(t, format, 'observabilityPage.alertCount', activeAlerts.length)
    : alerts.isLoading ? t('common.loading') : t('common.unknown');
  const elasticValue = elasticsearch.isLoading
    ? <Text variant="secondary">{t('common.loading')}</Text>
    : elasticsearch.error
      ? <Text variant="secondary">{t('common.unknown')}</Text>
      : elasticsearch.data
        ? format.number(elasticsearch.data.indices?.length ?? 0)
        : <Text variant="secondary">{t('common.unknown')}</Text>;

  let evaluationsContent: ReactNode;
  if (evaluations.isLoading) {
    evaluationsContent = <LocalizedLoadingState />;
  } else if (evaluations.error) {
    evaluationsContent = <LocalizedErrorState message={localizedOpsErrorMessage(evaluations.error, t)} onRetry={() => void evaluations.refetch()} />;
  } else if (!evaluations.data?.length) {
    evaluationsContent = <EmptyState title={t('observabilityPage.noEvaluationsTitle')} description={t('observabilityPage.noEvaluationsDescription')} />;
  } else {
    evaluationsContent = <DataTable rows={evaluations.data} getRowId={row => row.name} emptyTitle={t('common.noRecords')} density="compact" columns={[
      { id: 'name', header: t('observabilityPage.slo'), render: row => <Text variant="bodyStrong">{row.name}</Text> },
      { id: 'actual', header: t('observabilityPage.actual'), render: row => format.percent(row.actual, { maximumFractionDigits: 2 }) },
      { id: 'target', header: t('observabilityPage.target'), render: row => format.percent(row.target, { maximumFractionDigits: 2 }) },
      { id: 'budget', header: t('observabilityPage.errorBudget'), render: row => format.percent(row.errorBudgetRemaining, { maximumFractionDigits: 1 }) },
      { id: 'status', header: t('observabilityPage.status'), render: row => <StatusChip label={domainLabel(row.passed ? 'Passed' : 'Failed')} tone={row.passed ? 'success' : 'error'} /> }
    ]} />;
  }

  return <Stack spacing={2.5}>
    <PageHeader title={t('observabilityPage.title')} description={t('observabilityPage.description')}
      meta={!canWrite ? <Text variant="caption">{t('observabilityPage.readOnly')}</Text> : undefined} />
    {successMessages.length || mutationError ? <Stack direction="row" spacing={1} wrap sx={{ alignItems: 'center' }}>
      {successMessages.map(message => <StatusChip key={message} label={message} tone="success" />)}
      {mutationError ? <LocalizedErrorState message={localizedOpsErrorMessage(mutationError, t)} /> : null}
    </Stack> : null}
    <Grid columns={4}>
      <MetricCard label={t('observabilityPage.alertsTitle')} value={alertCount} icon="notification"
        tone={activeAlerts.some(alert => alert.level === 'L3') ? 'error' : activeAlerts.length ? 'warning' : 'default'} />
      <MetricCard label={t('observabilityPage.sloTitle')} icon="evaluation"
        value={evaluations.data ? format.number(evaluations.data.length) : evaluations.isLoading ? t('common.loading') : t('common.unknown')} />
      <MetricCard label={t('observabilityPage.elasticTitle')} value={elasticValue} hint={t('observabilityPage.index')} icon="database"
        tone={elasticsearch.data ? elasticsearch.data.reachable ? 'success' : 'error' : 'default'} />
      <MetricCard label={t('observabilityPage.telemetryTitle')} value={value ? domainLabel(value.mode) : profile.isLoading ? t('common.loading') : t('common.unknown')} icon="observability" />
    </Grid>
    <Card title={t('observabilityPage.alertsTitle')}>
      {alerts.isLoading ? <LocalizedLoadingState /> : null}
      {alerts.error ? <LocalizedErrorState message={localizedOpsErrorMessage(alerts.error, t)} onRetry={() => void alerts.refetch()} /> : null}
      {alerts.isSuccess && !activeAlerts.length ? <EmptyState title={t('observabilityPage.noActiveAlertsTitle')} description={t('observabilityPage.noActiveAlertsDescription')} /> : null}
      {alerts.isSuccess && activeAlerts.length ? <DataTable rows={activeAlerts} getRowId={row => row.id} emptyTitle={t('common.noRecords')} density="compact" minWidth={980} columns={[
        { id: 'level', header: t('observabilityPage.level'), render: row => <StatusChip label={domainLabel(row.level)} tone={row.level === 'L3' ? 'error' : row.level === 'L2' ? 'warning' : 'info'} /> },
        { id: 'slo', header: t('observabilityPage.slo'), render: row => <Text variant="bodyStrong">{row.sloName}</Text> },
        { id: 'status', header: t('observabilityPage.status'), render: row => <Stack spacing={0.25}>
          <StatusChip label={domainLabel(row.status)} tone={row.status === 'Firing' ? 'error' : row.status === 'Acknowledged' ? 'warning' : 'neutral'} />
          <Text variant="caption">{format.dateTime(row.updatedAt)}</Text>
        </Stack> },
        { id: 'owner', header: t('observabilityPage.owner'), render: row => row.owner },
        { id: 'message', header: t('observabilityPage.impact'), render: row => row.message },
        ...(canWrite ? [{ id: 'actions', header: t('observabilityPage.actions'), render: (row: AlertRecord) => <Stack direction="row" spacing={0.5} wrap>
          <Button size="small" onClick={() => acknowledge.mutate(row.id)} disabled={row.status === 'Acknowledged' || row.status === 'Silenced' || acknowledge.isPending}
            loading={acknowledge.isPending && acknowledge.variables === row.id}>{t('observabilityPage.acknowledge')}</Button>
          <Button size="small" tone="secondary" onClick={() => resolve.mutate(row.id)} disabled={resolve.isPending}
            loading={resolve.isPending && resolve.variables === row.id}>{t('observabilityPage.resolve')}</Button>
          <Button size="small" tone="secondary" onClick={() => { setFingerprint(row.fingerprint); setSilenceReason(row.message); }}>{t('observabilityPage.silence')}</Button>
        </Stack> }] : [])
      ]} /> : null}
      {canWrite ? <Stack spacing={2}>
        <Grid columns={2}>
          <FormField label={t('observabilityPage.silenceFingerprintLabel')}>
            <TextInput value={fingerprint} onChange={setFingerprint} disabled={silence.isPending} inputProps={{ 'aria-label': t('observabilityPage.silenceFingerprintAria') }} />
          </FormField>
          <FormField label={t('observabilityPage.silenceReasonLabel')}>
            <TextInput value={silenceReason} onChange={setSilenceReason} disabled={silence.isPending} inputProps={{ 'aria-label': t('observabilityPage.silenceReasonAria') }} />
          </FormField>
        </Grid>
        <Button onClick={() => silence.mutate({ fingerprint: fingerprint.trim(), reason: silenceReason.trim() })}
          disabled={!fingerprint.trim() || !silenceReason.trim()} loading={silence.isPending}>
          {t('observabilityPage.approveSilence', { duration: format.duration(60, 'minute') })}
        </Button>
      </Stack> : null}
    </Card>
    <Card title={t('observabilityPage.sloTitle')}>
      {canWrite ? <Stack spacing={2}>
        <Grid columns={3}>
          <FormField label={t('observabilityPage.nameLabel')}>
            <TextInput value={sloName} onChange={setSloName} disabled={saveSlo.isPending} inputProps={{ 'aria-label': t('observabilityPage.nameAria') }} />
          </FormField>
          <FormField label={t('observabilityPage.metricLabel')}>
            <TextInput value={metric} onChange={setMetric} disabled={saveSlo.isPending} inputProps={{ 'aria-label': t('observabilityPage.metricAria') }} />
          </FormField>
          <FormField label={t('observabilityPage.targetLabel')} hint={t('observabilityPage.targetHint')}>
            <TextInput value={target} onChange={setTarget} disabled={saveSlo.isPending} inputProps={{ inputMode: 'decimal', 'aria-label': t('observabilityPage.targetAria') }} />
          </FormField>
        </Grid>
        {!validSlo ? <Text color="error">{t('observabilityPage.targetValidation')}</Text> : null}
        <Button tone="secondary" disabled={!validSlo} loading={saveSlo.isPending}
          onClick={() => saveSlo.mutate({ name: sloName.trim(), metric: metric.trim(), target: numericTarget, windowDays: 30 })}>{t('observabilityPage.saveSlo')}</Button>
      </Stack> : null}
      {evaluationsContent}
    </Card>
    <Grid columns={2}>
      <Card title={t('observabilityPage.telemetryTitle')}>
        {profile.isLoading ? <LocalizedLoadingState /> : null}
        {profile.error ? <LocalizedErrorState message={localizedOpsErrorMessage(profile.error, t)} onRetry={() => void profile.refetch()} /> : null}
        {value && canWrite ? <Stack spacing={2}>
          <Grid columns={2}>
            <FormField label={t('observabilityPage.modeLabel')}>
              <Select label={t('observabilityPage.modeLabel')} value={value.mode} disabled={save.isPending} onChange={mode => setDraft({ ...value, mode })}
                options={['off', 'local', 'otlp', 'elastic'].map(mode => ({ value: mode, label: domainLabel(mode) }))} />
            </FormField>
            <FormField label={t('observabilityPage.serviceNameLabel')}>
              <TextInput value={value.serviceName} disabled={save.isPending} onChange={serviceName => setDraft({ ...value, serviceName })}
                inputProps={{ 'aria-label': t('observabilityPage.serviceNameAria') }} />
            </FormField>
          </Grid>
          <Grid columns={2}>
            <FormField label={t('observabilityPage.metricIntervalLabel')}>
              <TextInput value={String(value.metricIntervalSeconds)} disabled={save.isPending} onChange={seconds => setDraft({ ...value, metricIntervalSeconds: Number(seconds) })}
                inputProps={{ inputMode: 'numeric', 'aria-label': t('observabilityPage.metricIntervalAria') }} />
            </FormField>
            <Stack sx={{ justifyContent: 'flex-end' }}>
              <Button onClick={() => save.mutate(value)} loading={save.isPending}>{t('observabilityPage.saveProfile')}</Button>
            </Stack>
          </Grid>
        </Stack> : null}
        {value && !canWrite ? <Stack spacing={1}>
          <Text>{t('observabilityPage.modeValue', { mode: domainLabel(value.mode) })}</Text>
          <Text>{t('observabilityPage.serviceValue', { service: value.serviceName })}</Text>
          <Text>{t('observabilityPage.metricIntervalValue', { duration: format.duration(value.metricIntervalSeconds, 'second') })}</Text>
        </Stack> : null}
      </Card>
      <Card title={t('observabilityPage.elasticTitle')}>
        {elasticsearch.isLoading ? <LocalizedLoadingState /> : null}
        {elasticsearch.error ? <LocalizedErrorState message={localizedOpsErrorMessage(elasticsearch.error, t)} onRetry={() => void elasticsearch.refetch()} /> : null}
        {elasticsearch.data ? <Stack spacing={2}>
          <Stack direction="row" spacing={1} wrap>
            <StatusChip label={domainLabel(elasticsearch.data.reachable ? 'Reachable' : 'Unreachable')} tone={elasticsearch.data.reachable ? 'success' : 'error'} />
            {elasticsearch.data.status ? <StatusChip label={domainLabel(elasticsearch.data.status)} tone={elasticsearch.data.status === 'green' ? 'success' : elasticsearch.data.status === 'yellow' ? 'warning' : 'error'} /> : null}
          </Stack>
          {elasticsearch.data.error ? <Text color="error">{t('common.detailsUnavailable')}</Text> : null}
          {!elasticsearch.data.indices?.length ? <EmptyState title={t('observabilityPage.noIndicesTitle')} description={t('observabilityPage.noIndicesDescription')} /> : null}
          {elasticsearch.data.indices?.length ? <DataTable rows={elasticsearch.data.indices} getRowId={row => row.name} emptyTitle={t('common.noRecords')} density="compact" minWidth={520} columns={[
            { id: 'name', header: t('observabilityPage.index'), render: row => row.name },
            { id: 'documents', header: t('observabilityPage.documents'), render: row => row.documents == null ? '—' : format.number(row.documents) },
            { id: 'size', header: t('observabilityPage.size'), render: row => row.storeSize ?? '—' }
          ]} /> : null}
        </Stack> : null}
      </Card>
    </Grid>
  </Stack>;
}
