import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState,
  PageHeader, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function LifecyclePage() {
  const queryClient = useQueryClient();
  const lifecycle = useQuery({ queryKey: ['lifecycle'], queryFn: api.lifecycle });
  const [resourceType, setResourceType] = useState('Tenant');
  const [resourceId, setResourceId] = useState('');
  const [reason, setReason] = useState('');
  const [holdRef, setHoldRef] = useState('');
  const [holdReason, setHoldReason] = useState('');
  const [authorizedBy, setAuthorizedBy] = useState('Operator');
  const [dataClass, setDataClass] = useState('BusinessRecord');
  const [archivePeriod, setArchivePeriod] = useState('2026-11');
  const [archiveTier, setArchiveTier] = useState('Archive');
  const [recordCount, setRecordCount] = useState('100');
  const [rightsRef, setRightsRef] = useState('');
  const [thirdPartyRights, setThirdPartyRights] = useState('Unknown');
  const [licenseRef, setLicenseRef] = useState('');
  const [license, setLicense] = useState('Apache-2.0');
  const [purgeId, setPurgeId] = useState('');
  const [purgeConfirmation, setPurgeConfirmation] = useState('');
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['lifecycle'] });

  const softDelete = useMutation({ mutationFn: () => api.softDelete({ resourceType, resourceId, reason }), onSuccess: async () => { setResourceId(''); await invalidate(); } });
  const restore = useMutation({ mutationFn: (id: string) => api.restoreDeletion(id), onSuccess: invalidate });
  const purge = useMutation({ mutationFn: () => api.purgeDeletion(purgeId, purgeConfirmation), onSuccess: async () => { setPurgeConfirmation(''); await invalidate(); } });
  const createHold = useMutation({
    mutationFn: () => api.createLegalHold({
      id: crypto.randomUUID(), resourceRef: holdRef, reason: holdReason,
      authorizedBy, startsAt: new Date().toISOString(), expiresAt: null, status: 'Active'
    }), onSuccess: async () => { setHoldRef(''); setHoldReason(''); await invalidate(); }
  });
  const releaseHold = useMutation({ mutationFn: (id: string) => api.releaseLegalHold(id), onSuccess: invalidate });
  const createRetention = useMutation({
    mutationFn: () => api.createRetentionPolicy({
      id: crypto.randomUUID(), dataClass, hotDays: 30, archiveDays: 180,
      coldDays: 365, deleteAfterDays: 730, version: 1
    }), onSuccess: invalidate
  });
  const archive = useMutation({
    mutationFn: () => api.createArchive(archivePeriod, archiveTier, Number(recordCount)), onSuccess: invalidate
  });
  const restoreArchive = useMutation({ mutationFn: (id: string) => api.restoreArchive(id), onSuccess: invalidate });
  const backup = useMutation({ mutationFn: () => api.createBackup('local', 1440, 60), onSuccess: invalidate });
  const verifyRestore = useMutation({ mutationFn: (id: string) => api.verifyRestore(id), onSuccess: invalidate });
  const recordRights = useMutation({
    mutationFn: () => api.recordRights({
      id: crypto.randomUUID(), artifactRef: rightsRef, tenantOwnership: 'TenantOwned',
      platformLicense: 'None', thirdPartyRights, sourceRefs: thirdPartyRights === 'Unknown' ? [] : [`source:${rightsRef}`], publishStatus: 'Blocked'
    }), onSuccess: async () => { setRightsRef(''); await invalidate(); }
  });
  const recordLicense = useMutation({
    mutationFn: () => api.recordLicense({
      id: crypto.randomUUID(), artifactRef: licenseRef, license,
      noticeRef: `notice:${licenseRef}`, sbomRef: `sbom:${licenseRef}`,
      contributorConfirmed: true, trademarkReviewed: true, publishStatus: 'Blocked'
    }), onSuccess: async () => { setLicenseRef(''); await invalidate(); }
  });

  if (lifecycle.isLoading) return <LoadingState />;
  if (lifecycle.error) return <ErrorState message={lifecycle.error.message} />;
  const state = lifecycle.data;

  return (
    <Stack spacing={3}>
      <PageHeader title="Data lifecycle" description="Soft deletion, legal hold, retention, archives, backups and publishing rights." />
      <Grid columns={2}>
        <Card title="Deletion requests">
          <Select label="Resource type" value={resourceType} onChange={setResourceType} options={[{ value: 'Tenant', label: 'Tenant' }, { value: 'Account', label: 'Account' }]} />
          <FormField label="Resource ID"><TextInput value={resourceId} onChange={setResourceId} placeholder="Tenant GUID" /></FormField>
          <FormField label="Reason"><TextInput value={reason} onChange={setReason} placeholder="Owner requested deletion" /></FormField>
          <Button tone="danger" onClick={() => softDelete.mutate()} disabled={!resourceId || !reason} loading={softDelete.isPending}>Request soft deletion</Button>
          <DataTable rows={state?.deletionRequests ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'resource', header: 'Resource', render: row => `${row.resourceType ?? ''}: ${row.resourceId ?? ''}` },
            { id: 'status', header: 'Status', render: row => row.status ?? '' },
            { id: 'restore', header: 'Restore until', render: row => row.restoreUntil ? new Date(row.restoreUntil).toLocaleString() : '' },
            { id: 'actions', header: 'Actions', render: row => <Button tone="neutral" onClick={() => restore.mutate(row.id ?? '')} disabled={row.status !== 'SoftDeleted'}>Restore</Button> }
          ]} />
        </Card>
        <Card title="Physical purge">
          <FormField label="Deletion request ID"><TextInput value={purgeId} onChange={setPurgeId} /></FormField>
          <FormField label="Confirmation" hint="Type PURGE after the restore window expires."><TextInput value={purgeConfirmation} onChange={setPurgeConfirmation} /></FormField>
          <Button tone="danger" onClick={() => purge.mutate()} disabled={purgeConfirmation !== 'PURGE' || !purgeId} loading={purge.isPending}>Purge and create proof</Button>
          <Text variant="caption">{state?.deletionProofs?.length ?? 0} deletion proofs retained.</Text>
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card title="Legal holds">
          <FormField label="Resource reference"><TextInput value={holdRef} onChange={setHoldRef} placeholder="Tenant GUID or *" /></FormField>
          <FormField label="Reason"><TextInput value={holdReason} onChange={setHoldReason} /></FormField>
          <FormField label="Authorized by"><TextInput value={authorizedBy} onChange={setAuthorizedBy} /></FormField>
          <Button onClick={() => createHold.mutate()} disabled={!holdRef || !holdReason} loading={createHold.isPending}>Create legal hold</Button>
          <DataTable rows={state?.legalHolds ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'ref', header: 'Scope', render: row => row.resourceRef ?? '' },
            { id: 'status', header: 'Status', render: row => row.status ?? '' },
            { id: 'actions', header: 'Actions', render: row => <Button tone="neutral" onClick={() => releaseHold.mutate(row.id ?? '')} disabled={row.status !== 'Active'}>Release</Button> }
          ]} />
        </Card>
        <Card title="Retention and archives">
          <FormField label="Data class"><TextInput value={dataClass} onChange={setDataClass} /></FormField>
          <Button tone="secondary" onClick={() => createRetention.mutate()} loading={createRetention.isPending}>Create retention policy</Button>
          <FormField label="Archive period"><TextInput value={archivePeriod} onChange={setArchivePeriod} /></FormField>
          <Select label="Tier" value={archiveTier} onChange={setArchiveTier} options={[{ value: 'Archive', label: 'Archive' }, { value: 'Cold', label: 'Cold' }]} />
          <FormField label="Record count"><TextInput value={recordCount} onChange={setRecordCount} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <Button onClick={() => archive.mutate()} loading={archive.isPending}>Create archive manifest</Button>
          <DataTable rows={state?.archives ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'period', header: 'Period', render: row => row.period ?? '' },
            { id: 'tier', header: 'Tier', render: row => row.tier ?? '' },
            { id: 'status', header: 'Status', render: row => <StatusChip label={row.status ?? ''} tone="info" /> },
            { id: 'actions', header: 'Actions', render: row => <Button tone="neutral" onClick={() => restoreArchive.mutate(row.id ?? '')} disabled={row.status === 'Restored'}>Verify restore</Button> }
          ]} />
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card title="Backup and recovery">
          <Button onClick={() => backup.mutate()} loading={backup.isPending}>Create local backup manifest</Button>
          <DataTable rows={state?.backups ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'profile', header: 'Profile', render: row => row.profile ?? '' },
            { id: 'rpo', header: 'RPO', render: row => `${row.rpoMinutes ?? 0} min` },
            { id: 'rto', header: 'RTO', render: row => `${row.rtoMinutes ?? 0} min` },
            { id: 'status', header: 'Status', render: row => row.status ?? '' },
            { id: 'actions', header: 'Actions', render: row => <Button tone="neutral" onClick={() => verifyRestore.mutate(row.id ?? '')} disabled={row.restoreVerified}>Verify restore</Button> }
          ]} />
        </Card>
        <Card title="Rights and license gates">
          <FormField label="Artifact reference"><TextInput value={rightsRef} onChange={setRightsRef} /></FormField>
          <Select label="Third-party rights" value={thirdPartyRights} onChange={setThirdPartyRights} options={[{ value: 'Unknown', label: 'Unknown' }, { value: 'Cleared', label: 'Cleared' }]} />
          <Button tone="secondary" onClick={() => recordRights.mutate()} disabled={!rightsRef} loading={recordRights.isPending}>Record rights</Button>
          <FormField label="License artifact"><TextInput value={licenseRef} onChange={setLicenseRef} /></FormField>
          <FormField label="License"><TextInput value={license} onChange={setLicense} /></FormField>
          <Button onClick={() => recordLicense.mutate()} disabled={!licenseRef || !license} loading={recordLicense.isPending}>Record license</Button>
          <DataTable rows={state?.rights ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'artifact', header: 'Artifact', render: row => row.artifactRef ?? '' },
            { id: 'rights', header: 'Third-party', render: row => row.thirdPartyRights ?? '' },
            { id: 'status', header: 'Publish', render: row => <StatusChip label={row.publishStatus ?? ''} tone={row.publishStatus === 'Eligible' ? 'success' : 'error'} /> }
          ]} />
        </Card>
      </Grid>
    </Stack>
  );
}
