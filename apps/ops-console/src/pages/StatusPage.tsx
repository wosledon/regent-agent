import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Card,
  ContentContainer,
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
import { api, type PlatformIncident } from '../lib/api';
import { useDomainLabels } from '../lib/domainLabels';
import { localizedOpsErrorMessage } from '../lib/errorMessages';
import { useOpsFormatter } from '../lib/formatting';
import { localizedCount } from '../lib/localizedCount';

export function StatusPage({ operational = false, canWrite = false }: { operational?: boolean; canWrite?: boolean }) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useOpsFormatter();
  const queryClient = useQueryClient();
  const status = useQuery({ queryKey: ['platform-status'], queryFn: api.status, refetchInterval: 30_000 });
  const [title, setTitle] = useState('');
  const [severity, setSeverity] = useState('SEV3');
  const [message, setMessage] = useState('');
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['platform-status'] });
  const refreshStatus = useMutation({
    mutationFn: api.refreshStatus,
    onSuccess: data => queryClient.setQueryData(['platform-status'], data)
  });
  const createIncident = useMutation({
    mutationFn: () => api.createIncident(title.trim(), severity, message.trim()),
    onSuccess: async () => { setTitle(''); setMessage(''); await refresh(); }
  });
  const resolve = useMutation({ mutationFn: api.resolveIncident, onSuccess: refresh });
  const wrap = (content: ReactNode) => operational ? content : <ContentContainer>{content}</ContentContainer>;

  if (status.isLoading) return wrap(<LocalizedLoadingState />);
  if (status.error && !status.data) {
    return wrap(<LocalizedErrorState message={localizedOpsErrorMessage(status.error, t)} onRetry={() => void status.refetch()} />);
  }

  const mutationError = refreshStatus.error ?? createIncident.error ?? resolve.error;
  const successMessage = resolve.isSuccess ? t('statusPage.incidentResolved')
    : createIncident.isSuccess ? t('statusPage.incidentPublished')
      : refreshStatus.isSuccess ? t('statusPage.healthRefreshed') : null;
  const data = status.data;
  const stale = Boolean(status.error && data);
  const overallTone = stale ? 'warning' : data?.overallStatus === 'Operational' ? 'success' : data ? 'warning' : 'default';

  return wrap(<Stack spacing={2.5}>
    <PageHeader title={t('statusPage.title')} description={t('statusPage.description')}
      meta={<Stack direction="row" spacing={1.5} wrap>
        <Text variant="caption">{t('statusPage.lastSuccessfulCheck', { time: data?.checkedAt ? format.dateTime(data.checkedAt) : t('common.never') })}</Text>
        {operational && !canWrite ? <Text variant="caption">{t('statusPage.readOnly')}</Text> : null}
      </Stack>}
      actions={operational && canWrite ? <Button onClick={() => refreshStatus.mutate()} loading={refreshStatus.isPending}>{t('statusPage.refreshHealth')}</Button> : undefined} />
    {status.error ? <LocalizedErrorState message={stale
      ? t('statusPage.staleSnapshot', { message: localizedOpsErrorMessage(status.error, t) })
      : localizedOpsErrorMessage(status.error, t)} onRetry={() => void status.refetch()} /> : null}
    {successMessage ? <StatusChip label={successMessage} tone="success" /> : null}
    {mutationError ? <LocalizedErrorState message={localizedOpsErrorMessage(mutationError, t)} /> : null}
    <Grid columns={3}>
      <MetricCard label={t('statusPage.overallStatus')} icon="platform" tone={overallTone}
        value={<StatusChip label={stale ? t('statusPage.staleUnknown') : data ? domainLabel(data.overallStatus) : domainLabel('unknown')}
          tone={!stale && data?.overallStatus === 'Operational' ? 'success' : 'warning'} />} />
      <MetricCard label={t('statusPage.componentsTitle')} icon="components"
        value={data ? localizedCount(t, format, 'statusPage.componentCount', data.components?.length ?? 0) : t('common.unknown')} />
      <MetricCard label={t('statusPage.incidentsTitle')} icon="notification" tone={data?.incidents?.some(incident => incident.status !== 'Resolved') ? 'warning' : 'default'}
        value={data ? localizedCount(t, format, 'statusPage.incidentCount', data.incidents?.length ?? 0) : t('common.unknown')} />
    </Grid>
    <Grid columns={2}>
      <Card title={t('statusPage.componentsTitle')}>
        {!data?.components?.length ? <EmptyState title={t('statusPage.noComponentsTitle')} description={t('statusPage.noComponentsDescription')} /> : null}
        {data?.components?.length ? <DataTable rows={data.components} getRowId={row => row.name} emptyTitle={t('common.noRecords')} density="compact" minWidth={520} columns={[
          { id: 'name', header: t('statusPage.component'), render: row => <Text variant="bodyStrong">{row.name}</Text> },
          { id: 'status', header: t('statusPage.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={stale ? 'warning' : row.status === 'Operational' ? 'success' : row.status === 'Down' ? 'error' : 'warning'} /> },
          { id: 'latency', header: t('statusPage.latency'), render: row => format.duration(row.latencyMs, 'millisecond') },
          { id: 'message', header: t('statusPage.message'), render: row => row.message }
        ]} /> : null}
      </Card>
      <Card title={t('statusPage.incidentsTitle')}>
        {!data?.incidents?.length ? <EmptyState title={t('statusPage.noIncidentsTitle')} description={t('statusPage.noIncidentsDescription')} /> : null}
        {data?.incidents?.length ? <DataTable rows={data.incidents} getRowId={row => row.id} emptyTitle={t('common.noRecords')} density="compact" minWidth={620} columns={[
          { id: 'title', header: t('statusPage.incident'), render: row => <Stack spacing={0.25}>
            <Text variant="bodyStrong">{row.title}</Text>
            <Text variant="caption">{format.dateTime(row.startedAt)}</Text>
          </Stack> },
          { id: 'severity', header: t('statusPage.severity'), render: row => <Stack direction="row" spacing={0.5} wrap>
            <StatusChip label={domainLabel(row.severity)} tone={row.severity === 'SEV1' ? 'error' : row.severity === 'SEV2' ? 'warning' : 'info'} />
            <StatusChip label={domainLabel(row.status)} tone={row.status === 'Resolved' ? 'success' : row.status === 'Open' ? 'warning' : 'neutral'} />
          </Stack> },
          { id: 'message', header: t('statusPage.message'), render: row => row.message },
          ...(operational && canWrite ? [{ id: 'actions', header: t('statusPage.actions'), render: (row: PlatformIncident) => row.status === 'Resolved' ? '—'
            : <Button size="small" tone="neutral" onClick={() => resolve.mutate(row.id)} disabled={resolve.isPending}
              loading={resolve.isPending && resolve.variables === row.id}>{t('statusPage.resolve')}</Button> }] : [])
        ]} /> : null}
      </Card>
    </Grid>
    {operational && canWrite ? <Card title={t('statusPage.publishTitle')}>
      <Grid columns={3}>
        <FormField label={t('statusPage.titleLabel')}>
          <TextInput value={title} disabled={createIncident.isPending} onChange={setTitle} inputProps={{ 'aria-label': t('statusPage.titleAria') }} />
        </FormField>
        <FormField label={t('statusPage.severityLabel')}>
          <Select label={t('statusPage.severityLabel')} value={severity} disabled={createIncident.isPending} onChange={setSeverity}
            options={['SEV1', 'SEV2', 'SEV3', 'SEV4'].map(value => ({ value, label: domainLabel(value) }))} />
        </FormField>
        <FormField label={t('statusPage.messageLabel')}>
          <TextInput value={message} disabled={createIncident.isPending} onChange={setMessage} multiline minRows={2} inputProps={{ 'aria-label': t('statusPage.messageAria') }} />
        </FormField>
      </Grid>
      <Button tone="danger" onClick={() => createIncident.mutate()} disabled={!title.trim() || !message.trim()} loading={createIncident.isPending}>
        {t('statusPage.publish')}
      </Button>
    </Card> : null}
  </Stack>);
}
