import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, EmptyState, ErrorState, LoadingState, PageHeader, Stack, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../lib/api';

export function BackupsPage() {
  const queryClient = useQueryClient();
  const profiles = useQuery({ queryKey: ['ops-backups'], queryFn: api.backups });
  const coverage = useQuery({ queryKey: ['backup-coverage'], queryFn: api.backupCoverage });
  const jobs = useQuery({ queryKey: ['backup-jobs'], queryFn: api.backupJobs });
  const restores = useQuery({ queryKey: ['restore-jobs'], queryFn: api.restoreJobs });
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['ops-backups'] }),
    queryClient.invalidateQueries({ queryKey: ['backup-coverage'] }),
    queryClient.invalidateQueries({ queryKey: ['backup-jobs'] }),
    queryClient.invalidateQueries({ queryKey: ['restore-jobs'] })
  ]);
  const save = useMutation({
    mutationFn: (enabled: boolean) => {
      const profile = profiles.data?.[0];
      return api.saveBackupProfile({
        id: 'default', deploymentId: 'local', enabled, components: enabled ? 'rustfs' : 'none',
        retentionDays: enabled ? 35 : 0, rustFs: enabled, version: profile?.version
      });
    }, onSuccess: invalidate
  });
  const run = useMutation({ mutationFn: api.runBackup, onSuccess: invalidate });
  const drill = useMutation({
    mutationFn: async () => {
      const completed = (jobs.data ?? []).filter(job => job.status === 'Completed');
      if (!completed.length) throw new Error('Run a completed backup before starting a restore drill.');
      const restore = await api.requestRestore([completed[0].id]);
      return api.approveRestore(restore.id);
    }, onSuccess: invalidate
  });
  if (profiles.isLoading || coverage.isLoading || jobs.isLoading || restores.isLoading) return <LoadingState />;
  const error = profiles.error ?? coverage.error ?? jobs.error ?? restores.error ?? save.error ?? run.error ?? drill.error;
  if (error) return <ErrorState message={error.message} />;
  const profile = profiles.data?.[0];
  if (!profile) return <EmptyState title="No backup profile" />;
  return <Stack spacing={3}>
    <PageHeader title="Backups" description="Component jobs, effective RPO/RTO and two-person restore drills." />
    <Card title="Backup coverage">
      <Text>Status: {coverage.data?.status ?? 'unknown'}</Text>
      <Text>Components: {coverage.data?.enabledComponents.join(', ') || 'none'}</Text>
      <Text>Effective RPO: {coverage.data?.effectiveRpoMinutes == null ? 'unverified' : `${coverage.data.effectiveRpoMinutes} minutes`}</Text>
      <Text>Effective RTO: {coverage.data?.effectiveRtoMinutes == null ? 'unverified' : `${coverage.data.effectiveRtoMinutes} minutes`}</Text>
      <Stack direction="row" wrap spacing={1}>
        <Button onClick={() => save.mutate(!profile.enabled)} loading={save.isPending}>{profile.enabled ? 'Disable' : 'Enable local RustFS evidence backup'}</Button>
        <Button tone="secondary" onClick={() => run.mutate()} disabled={!profile.enabled} loading={run.isPending}>Run component jobs</Button>
        <Button tone="secondary" onClick={() => drill.mutate()} disabled={!profile.enabled} loading={drill.isPending}>Run two-person restore drill</Button>
      </Stack>
    </Card>
    <Card title="Backup jobs">
      <DataTable rows={jobs.data ?? []} getRowId={row => row.id} columns={[
        { id: 'component', header: 'Component', render: row => row.component },
        { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone={row.status === 'Completed' ? 'success' : 'error'} /> },
        { id: 'started', header: 'Started', render: row => new Date(row.startedAt).toLocaleString() },
        { id: 'hash', header: 'SHA-256', render: row => row.sha256 || '—' },
        { id: 'error', header: 'Error', render: row => row.error ?? '—' }
      ]} />
    </Card>
    <Card title="Restore approvals and drills">
      <DataTable rows={restores.data ?? []} getRowId={row => row.id} columns={[
        { id: 'status', header: 'Status', render: row => row.status },
        { id: 'scope', header: 'Scope', render: row => row.scope },
        { id: 'requester', header: 'Requester', render: row => row.requester },
        { id: 'approver', header: 'Approver', render: row => row.approver ?? '—' },
        { id: 'rpo', header: 'RPO', render: row => row.rpoActualMinutes == null ? 'unverified' : `${row.rpoActualMinutes}m` },
        { id: 'rto', header: 'RTO', render: row => row.rtoActualMinutes == null ? 'unverified' : `${row.rtoActualMinutes}m` }
      ]} />
    </Card>
  </Stack>;
}
