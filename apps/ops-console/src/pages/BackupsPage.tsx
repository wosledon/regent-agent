import { useMemo, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Card,
  ConfirmDialog,
  DataTable,
  EmptyState,
  FormField,
  Grid,
  MetricCard,
  PageHeader,
  Select,
  Stack,
  StatusChip,
  Text
} from '@regent/ui-shared';
import { LocalizedErrorState, LocalizedLoadingState } from '../components/LocalizedStates';
import { api, type BackupJob, type BackupProfile, type RestoreJob, type RestoreScope } from '../lib/api';
import { useDomainLabels } from '../lib/domainLabels';
import { localizedOpsErrorMessage } from '../lib/errorMessages';
import { useOpsFormatter } from '../lib/formatting';
import { localizedCount } from '../lib/localizedCount';

interface CompletedBackupSet {
  id: string;
  jobs: BackupJob[];
}

function completedBackupSets(jobs: readonly BackupJob[]): CompletedBackupSet[] {
  const groups = new Map<string, BackupJob[]>();
  for (const job of jobs) {
    if (job.status !== 'Completed' || !job.backupSetId) continue;
    groups.set(job.backupSetId, [...(groups.get(job.backupSetId) ?? []), job]);
  }
  return [...groups.entries()]
    .map(([id, setJobs]) => ({ id, jobs: setJobs }))
    .sort((left, right) => right.jobs[0].startedAt.localeCompare(left.jobs[0].startedAt));
}

export function BackupsPage({ canWrite = false }: { canWrite?: boolean }) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useOpsFormatter();
  const queryClient = useQueryClient();
  const profiles = useQuery({ queryKey: ['ops-backups'], queryFn: api.backups });
  const profile = profiles.data?.[0];
  const coverage = useQuery({
    queryKey: ['backup-coverage', profile?.id ?? 'default'],
    queryFn: () => api.backupCoverage(profile?.id ?? 'default'),
    enabled: !!profile
  });
  const jobs = useQuery({ queryKey: ['backup-jobs'], queryFn: api.backupJobs });
  const restores = useQuery({ queryKey: ['restore-jobs'], queryFn: api.restoreJobs });
  const [selectedSetId, setSelectedSetId] = useState('');
  const [scope, setScope] = useState<RestoreScope>('Full');
  const [approval, setApproval] = useState<RestoreJob | null>(null);
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['ops-backups'] }),
    queryClient.invalidateQueries({ queryKey: ['backup-coverage'] }),
    queryClient.invalidateQueries({ queryKey: ['backup-jobs'] }),
    queryClient.invalidateQueries({ queryKey: ['restore-jobs'] })
  ]);
  const save = useMutation({
    mutationFn: (loaded: BackupProfile) => api.saveBackupProfile({ ...loaded, enabled: !loaded.enabled }),
    onSuccess: invalidate
  });
  const run = useMutation({ mutationFn: (profileId: string) => api.runBackup(profileId), onSuccess: invalidate });
  const request = useMutation({
    mutationFn: ({ backupIds, scope: requestedScope }: { backupIds: string[]; scope: RestoreScope }) =>
      api.requestRestore(backupIds, requestedScope),
    onSuccess: invalidate
  });
  const approve = useMutation({ mutationFn: (id: string) => api.approveRestore(id), onSuccess: invalidate });
  const completedSets = useMemo(() => completedBackupSets(jobs.data ?? []), [jobs.data]);
  const selectedSet = completedSets.find(set => set.id === selectedSetId);
  const requiredComponents = coverage.data?.enabledComponents ?? [];
  const selectedComponents = new Set(selectedSet?.jobs.map(job => job.component) ?? []);
  const missingComponents = requiredComponents.filter(component => !selectedComponents.has(component));
  const canRequest = canWrite && !!profile?.enabled && !!selectedSet
    && (scope === 'Tenant' || (requiredComponents.length > 0 && missingComponents.length === 0));
  const coverageComponents = requiredComponents.length ? format.list(requiredComponents) : t('common.none');
  const componentDrillEntries = coverage.data?.componentRtoMinutes ? Object.entries(coverage.data.componentRtoMinutes) : [];
  const componentDrills = componentDrillEntries.length
    ? format.list(componentDrillEntries.map(([component, minutes]) => t('backupsPage.componentDrill', {
      component, duration: minutes == null ? domainLabel('unverified') : format.duration(minutes, 'minute')
    })))
    : t('common.none');
  const pendingApprovals = restores.data?.filter(job => job.status === 'PendingApproval').length ?? 0;
  const mutationError = save.error ?? run.error ?? request.error ?? approve.error;
  const successMessages = [
    save.isSuccess ? t('backupsPage.profileUpdated') : null,
    run.isSuccess ? t('backupsPage.runFinished') : null,
    request.isSuccess ? t('backupsPage.requestSuccess') : null,
    approve.isSuccess ? t('backupsPage.approveSuccess') : null
  ].filter((message): message is string => Boolean(message));
  const approvalTenant = approval
    ? profiles.data?.find(item => item.id === approval.profileId)?.deploymentId ?? approval.profileId
    : '';
  const approvalBackupSets = approval
    ? format.list([...new Set(approval.backupIds.map(id => jobs.data?.find(job => job.id === id)?.backupSetId ?? id.slice(0, 8)))])
    : '';
  const approvalDescription = approval
    ? t('backupsPage.confirmApproveRestore', { scope: domainLabel(approval.scope), tenant: approvalTenant, backupSets: approvalBackupSets })
    : '';

  let jobsContent: ReactNode;
  if (jobs.isLoading) {
    jobsContent = <LocalizedLoadingState />;
  } else if (jobs.error) {
    jobsContent = <LocalizedErrorState message={localizedOpsErrorMessage(jobs.error, t)} onRetry={() => void jobs.refetch()} />;
  } else if (!jobs.data?.length) {
    jobsContent = <EmptyState title={t('backupsPage.noJobsTitle')} description={t('backupsPage.noJobsDescription')} />;
  } else {
    jobsContent = <DataTable rows={jobs.data} getRowId={row => row.id} emptyTitle={t('common.noRecords')} density="compact" columns={[
      { id: 'set', header: t('backupsPage.backupSet'), render: row => row.backupSetId?.slice(0, 8) ?? row.id.slice(0, 8) },
      { id: 'component', header: t('backupsPage.component'), render: row => row.component },
      { id: 'status', header: t('backupsPage.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Completed' ? 'success' : row.status === 'Blocked' ? 'error' : 'warning'} /> },
      { id: 'started', header: t('backupsPage.started'), render: row => format.dateTime(row.startedAt) },
      { id: 'hash', header: t('backupsPage.hash'), render: row => <Text variant="mono">{row.sha256 || '—'}</Text> },
      { id: 'error', header: t('backupsPage.error'), render: row => row.error ? t('common.detailsUnavailable') : '—' }
    ]} />;
  }

  let restoresContent: ReactNode;
  if (restores.isLoading) {
    restoresContent = <LocalizedLoadingState />;
  } else if (restores.error) {
    restoresContent = <LocalizedErrorState message={localizedOpsErrorMessage(restores.error, t)} onRetry={() => void restores.refetch()} />;
  } else if (!restores.data?.length) {
    restoresContent = <EmptyState title={t('backupsPage.noRestoresTitle')} description={t('backupsPage.noRestoresDescription')} />;
  } else {
    restoresContent = <DataTable rows={restores.data} getRowId={row => row.id} emptyTitle={t('common.noRecords')} density="compact" columns={[
      { id: 'status', header: t('backupsPage.status'), render: (row: RestoreJob) => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Verified' ? 'success' : row.status === 'PendingApproval' ? 'warning' : 'error'} /> },
      { id: 'scope', header: t('backupsPage.scope'), render: row => domainLabel(row.scope) },
      { id: 'requester', header: t('backupsPage.requester'), render: row => row.requester },
      { id: 'approver', header: t('backupsPage.approver'), render: row => row.approver ?? '—' },
      { id: 'rpo', header: t('backupsPage.rpo'), render: row => row.rpoActualMinutes == null ? domainLabel('unverified') : format.duration(row.rpoActualMinutes, 'minute') },
      { id: 'rto', header: t('backupsPage.rto'), render: row => row.rtoActualMinutes == null ? domainLabel('unverified') : format.duration(row.rtoActualMinutes, 'minute') },
      ...(canWrite ? [{ id: 'actions', header: t('backupsPage.actions'), render: (row: RestoreJob) => row.status === 'PendingApproval'
        ? <Button size="small" tone="secondary" onClick={() => setApproval(row)} disabled={approve.isPending || request.isPending}
            loading={approve.isPending && approve.variables === row.id}>{t('backupsPage.approveRestore')}</Button>
        : '—' }] : [])
    ]} />;
  }

  if (profiles.isLoading) return <LocalizedLoadingState />;
  if (profiles.error && !profiles.data) {
    return <LocalizedErrorState message={localizedOpsErrorMessage(profiles.error, t)} onRetry={() => void profiles.refetch()} />;
  }
  if (!profile) return <EmptyState title={t('backupsPage.noProfileTitle')} description={t('backupsPage.noProfileDescription')} />;

  const coverageValue = coverage.isLoading
    ? <Text variant="secondary">{t('common.loading')}</Text>
    : coverage.error
      ? <Text variant="secondary">{t('common.unknown')}</Text>
      : coverage.data
        ? <StatusChip label={domainLabel(coverage.data.status)} tone={coverage.data.status === 'covered' ? 'success' : coverage.data.enabled ? 'warning' : 'neutral'} />
        : <Text variant="secondary">{t('common.unknown')}</Text>;

  return <Stack spacing={2.5}>
    <PageHeader title={t('backupsPage.title')} description={t('backupsPage.description')}
      meta={!canWrite ? <Text variant="caption">{t('backupsPage.readOnly')}</Text> : undefined}
      actions={canWrite ? <Stack direction="row" spacing={1} wrap>
        <Button tone="secondary" onClick={() => save.mutate(profile)} loading={save.isPending}>{profile.enabled ? t('backupsPage.disable') : t('backupsPage.enable')}</Button>
        <Button onClick={() => run.mutate(profile.id)} disabled={!profile.enabled} loading={run.isPending}>{t('backupsPage.runJobs')}</Button>
      </Stack> : undefined} />
    {profiles.error ? <LocalizedErrorState message={localizedOpsErrorMessage(profiles.error, t)} onRetry={() => void profiles.refetch()} /> : null}
    {successMessages.length || mutationError ? <Stack direction="row" spacing={1} wrap sx={{ alignItems: 'center' }}>
      {successMessages.map(message => <StatusChip key={message} label={message} tone="success" />)}
      {mutationError ? <LocalizedErrorState message={localizedOpsErrorMessage(mutationError, t)} /> : null}
    </Stack> : null}
    <Grid columns={4}>
      <MetricCard label={t('backupsPage.coverageTitle')} value={coverageValue} icon="backup"
        tone={coverage.data?.status === 'covered' ? 'success' : coverage.data?.enabled ? 'warning' : 'default'} />
      <MetricCard label={t('backupsPage.rpo')} icon="clock"
        value={coverage.data ? coverage.data.effectiveRpoMinutes == null ? domainLabel('unverified') : format.duration(coverage.data.effectiveRpoMinutes, 'minute') : coverage.isLoading ? t('common.loading') : t('common.unknown')} />
      <MetricCard label={t('backupsPage.rto')} icon="activity"
        value={coverage.data ? coverage.data.effectiveRtoMinutes == null ? domainLabel('unverified') : format.duration(coverage.data.effectiveRtoMinutes, 'minute') : coverage.isLoading ? t('common.loading') : t('common.unknown')} />
      <MetricCard label={t('backupsPage.approveRestore')} value={restores.data ? format.number(pendingApprovals) : restores.isLoading ? t('common.loading') : t('common.unknown')}
        hint={t('backupsPage.restoreGuidance')} icon="approval" tone={pendingApprovals ? 'warning' : 'default'} />
    </Grid>
    <Card title={t('backupsPage.coverageTitle')}>
      {coverage.isLoading ? <LocalizedLoadingState /> : coverage.error ? <LocalizedErrorState message={localizedOpsErrorMessage(coverage.error, t)} onRetry={() => void coverage.refetch()} /> : coverage.data ? <Stack spacing={1}>
        <Text>{t('backupsPage.coverageStatus', { status: domainLabel(coverage.data.status) })}</Text>
        <Text>{t('backupsPage.coverageComponents', { components: coverageComponents })}</Text>
        <Text variant="caption">{localizedCount(t, format, 'backupsPage.componentCount', requiredComponents.length)}</Text>
        <Text>{t('backupsPage.effectiveRpo', { duration: coverage.data.effectiveRpoMinutes == null ? domainLabel('unverified') : format.duration(coverage.data.effectiveRpoMinutes, 'minute') })}</Text>
        <Text>{t('backupsPage.effectiveRto', { duration: coverage.data.effectiveRtoMinutes == null ? domainLabel('unverified') : format.duration(coverage.data.effectiveRtoMinutes, 'minute') })}</Text>
        <Text>{t('backupsPage.componentDrills', { drills: componentDrills })}</Text>
      </Stack> : null}
    </Card>
    <Grid columns={2}>
      <Card title={t('backupsPage.requestRestore')}>
        {!canWrite ? <Text variant="secondary">{t('backupsPage.restoreGuidance')}</Text> : <Stack spacing={2}>
          <Text variant="caption">{t('backupsPage.restoreGuidance')}</Text>
          {jobs.isLoading ? <LocalizedLoadingState /> : null}
          {jobs.error ? <LocalizedErrorState message={localizedOpsErrorMessage(jobs.error, t)} onRetry={() => void jobs.refetch()} /> : null}
          {jobs.isSuccess && !completedSets.length ? <EmptyState title={t('backupsPage.noCompletedSetsTitle')} description={t('backupsPage.noCompletedSetsDescription')} /> : null}
          {completedSets.length ? <Stack spacing={2}>
            <FormField label={t('backupsPage.completedSetLabel')}>
              <Select label={t('backupsPage.completedSetLabel')} value={selectedSet?.id ?? ''} onChange={setSelectedSetId} disabled={request.isPending} options={[
                { value: '', label: t('backupsPage.selectCompletedSet') },
                ...completedSets.map(set => ({
                  value: set.id,
                  label: t('backupsPage.backupSetOption', {
                    id: set.id.slice(0, 8), components: format.list(set.jobs.map(job => job.component)), started: format.dateTime(set.jobs[0].startedAt)
                  })
                }))
              ]} />
            </FormField>
            <FormField label={t('backupsPage.restoreScopeLabel')}>
              <Select label={t('backupsPage.restoreScopeLabel')} value={scope} onChange={setScope} disabled={request.isPending} options={[
                { value: 'Full', label: domainLabel('Full') }, { value: 'Tenant', label: domainLabel('Tenant') }
              ]} />
            </FormField>
            {selectedSet && scope === 'Full' && !requiredComponents.length ? <Text color="error">{t('backupsPage.fullCoverageRequired')}</Text> : null}
            {selectedSet && scope === 'Full' && missingComponents.length ? <Text color="error">{t('backupsPage.fullScopeMissing', { components: format.list(missingComponents) })}</Text> : null}
            <Button onClick={() => selectedSet && request.mutate({ backupIds: selectedSet.jobs.map(job => job.id), scope })}
              disabled={!canRequest} loading={request.isPending}>{t('backupsPage.requestRestore')}</Button>
          </Stack> : null}
        </Stack>}
      </Card>
      <Card title={t('backupsPage.restoresTitle')}>
        {restoresContent}
      </Card>
    </Grid>
    <Card title={t('backupsPage.jobsTitle')}>{jobsContent}</Card>
    <ConfirmDialog open={Boolean(approval)} title={t('backupsPage.approveRestore')}
      description={<Text sx={{ whiteSpace: 'pre-line' }}>{approvalDescription}</Text>}
      confirmLabel={t('backupsPage.approveRestore')} tone="danger" loading={approve.isPending}
      onClose={() => { if (!approve.isPending) setApproval(null); }}
      onConfirm={() => {
        if (!approval) return;
        approve.mutate(approval.id, { onSettled: () => setApproval(null) });
      }} />
  </Stack>;
}
