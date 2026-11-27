import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ContentContainer, FormField, Grid, MetricCard,
  PageHeader, Select, Stack, StatusChip, Text, TextInput, createUuidV4
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';
import { localizedCount } from '../../lib/localizedCount';
import { governanceStatusTone } from './governancePresentation';

export function LifecyclePage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
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
  const [contributorAttestation, setContributorAttestation] = useState('');
  const [trademarkAttestation, setTrademarkAttestation] = useState('');
  const [purgeId, setPurgeId] = useState('');
  const [purgeConfirmation, setPurgeConfirmation] = useState('');
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['lifecycle'] });

  const softDelete = useMutation({ mutationFn: () => api.softDelete({ resourceType, resourceId, reason }), onSuccess: async () => { setResourceId(''); await invalidate(); } });
  const restore = useMutation({ mutationFn: (id: string) => api.restoreDeletion(id), onSuccess: invalidate });
  const purge = useMutation({ mutationFn: () => api.purgeDeletion(purgeId, purgeConfirmation), onSuccess: async () => { setPurgeConfirmation(''); await invalidate(); } });
  const createHold = useMutation({
    mutationFn: () => api.createLegalHold({
      id: createUuidV4(), resourceRef: holdRef, reason: holdReason,
      authorizedBy, startsAt: new Date().toISOString(), expiresAt: null, status: 'Active'
    }), onSuccess: async () => { setHoldRef(''); setHoldReason(''); await invalidate(); }
  });
  const releaseHold = useMutation({ mutationFn: (id: string) => api.releaseLegalHold(id), onSuccess: invalidate });
  const createRetention = useMutation({
    mutationFn: () => api.createRetentionPolicy({
      id: createUuidV4(), dataClass, hotDays: 30, archiveDays: 180,
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
      id: createUuidV4(), artifactRef: rightsRef, tenantOwnership: 'TenantOwned',
      platformLicense: 'None', thirdPartyRights, sourceRefs: thirdPartyRights === 'Unknown' ? [] : [`source:${rightsRef}`], publishStatus: 'Blocked'
    }), onSuccess: async () => { setRightsRef(''); await invalidate(); }
  });
  const recordLicense = useMutation({
    mutationFn: () => api.recordLicense({
      id: createUuidV4(), artifactRef: licenseRef, license,
      noticeRef: `notice:${licenseRef}`, sbomRef: `sbom:${licenseRef}`,
      contributorConfirmed: contributorAttestation === 'confirmed', trademarkReviewed: trademarkAttestation === 'reviewed', publishStatus: 'Blocked'
    }), onSuccess: async () => {
      setLicenseRef('');
      setContributorAttestation('');
      setTrademarkAttestation('');
      await invalidate();
    }
  });

  if (lifecycle.isLoading) return <LocalizedLoadingState />;
  if (lifecycle.error) return <LocalizedErrorState error={lifecycle.error} />;
  const state = lifecycle.data;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader title={t('governancePage.dataLifecycle')} description={t('governancePage.softDeletionLegalHoldRetentionArchivesBackupsAndPublishingRights')} />
        <Grid columns={4}>
          <MetricCard label={t('governancePage.deletionRequests')} value={state?.deletionRequests?.length ?? 0} icon="lifecycle" />
          <MetricCard label={t('governancePage.legalHolds')} value={state?.legalHolds?.length ?? 0} icon="governance" tone={(state?.legalHolds?.length ?? 0) > 0 ? 'warning' : 'default'} />
          <MetricCard label={t('governancePage.retentionAndArchives')} value={state?.archives?.length ?? 0} icon="database" />
          <MetricCard label={t('governancePage.backupAndRecovery')} value={state?.backups?.length ?? 0} icon="backup" />
        </Grid>
        <Grid columns={2}>
        <Card padding="small" title={t('governancePage.deletionRequests')}>
          <Select label={t('governancePage.resourceType')} value={resourceType} onChange={setResourceType} options={[{ value: 'Tenant', label: t('common.tenant') }, { value: 'Account', label: t('governancePage.account') }]} />
          <FormField label={t('governancePage.resourceID')}><TextInput value={resourceId} onChange={setResourceId} placeholder={t('governancePage.tenantGUID')} /></FormField>
          <FormField label={t('common.reason')}><TextInput value={reason} onChange={setReason} placeholder={t('governancePage.ownerRequestedDeletion')} /></FormField>
          <Button tone="danger" onClick={() => softDelete.mutate()} disabled={!resourceId || !reason} loading={softDelete.isPending}>{t('governancePage.requestSoftDeletion')}</Button>
          <LocalizedMutationFeedback mutation={softDelete} successMessage={t('governancePage.deletionRequested')} />
          <LocalizedDataTable density="compact" rows={state?.deletionRequests ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'resource', header: t('governancePage.resource'), render: row => t('governancePage.resourceLabel', { type: domainLabel(row.resourceType), id: row.resourceId ?? '' }) },
            { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={governanceStatusTone(row.status)} /> },
            { id: 'restore', header: t('governancePage.restoreUntil'), render: row => format.dateTime(row.restoreUntil) },
            { id: 'actions', header: t('common.actions'), render: row => <Button tone="neutral" size="small" onClick={() => restore.mutate(row.id ?? '')} disabled={row.status !== 'SoftDeleted'}>{t('governancePage.restore')}</Button> }
          ]} />
          <LocalizedMutationFeedback mutation={restore} successMessage={t('governancePage.deletionRestored')} />
        </Card>
        <Card padding="small" title={t('governancePage.physicalPurge')}>
          <FormField label={t('governancePage.deletionRequestID')}><TextInput value={purgeId} onChange={setPurgeId} /></FormField>
          <FormField label={t('governancePage.confirmation')} hint={t('governancePage.typePURGEAfterTheRestoreWindowExpires')}><TextInput value={purgeConfirmation} onChange={setPurgeConfirmation} /></FormField>
          <Button tone="danger" onClick={() => purge.mutate()} disabled={purgeConfirmation !== 'PURGE' || !purgeId} loading={purge.isPending}>{t('governancePage.purgeAndCreateProof')}</Button>
          <LocalizedMutationFeedback mutation={purge} successMessage={t('governancePage.deletionPurged')} />
          <Text variant="caption">{localizedCount(t, format, 'governancePage.deletionProofsRetained', state?.deletionProofs?.length ?? 0)}</Text>
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card padding="small" title={t('governancePage.legalHolds')}>
          <FormField label={t('governancePage.resourceReference')}><TextInput value={holdRef} onChange={setHoldRef} placeholder={t('governancePage.tenantGUIDOr')} /></FormField>
          <FormField label={t('common.reason')}><TextInput value={holdReason} onChange={setHoldReason} /></FormField>
          <FormField label={t('governancePage.authorizedBy')}><TextInput value={authorizedBy} onChange={setAuthorizedBy} /></FormField>
          <Button onClick={() => createHold.mutate()} disabled={!holdRef || !holdReason} loading={createHold.isPending}>{t('governancePage.createLegalHold')}</Button>
          <LocalizedMutationFeedback mutation={createHold} successMessage={t('governancePage.legalHoldCreated')} />
          <LocalizedDataTable density="compact" rows={state?.legalHolds ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'ref', header: t('common.scope'), render: row => row.resourceRef ?? '' },
            { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={governanceStatusTone(row.status)} /> },
            { id: 'actions', header: t('common.actions'), render: row => <Button tone="neutral" size="small" onClick={() => releaseHold.mutate(row.id ?? '')} disabled={row.status !== 'Active'}>{t('common.release')}</Button> }
          ]} />
          <LocalizedMutationFeedback mutation={releaseHold} successMessage={t('governancePage.legalHoldReleased')} />
        </Card>
        <Card padding="small" title={t('governancePage.retentionAndArchives')}>
          <FormField label={t('governancePage.dataClass')}><TextInput value={dataClass} onChange={setDataClass} /></FormField>
          <Button tone="secondary" onClick={() => createRetention.mutate()} loading={createRetention.isPending}>{t('governancePage.createRetentionPolicy')}</Button>
          <LocalizedMutationFeedback mutation={createRetention} successMessage={t('governancePage.retentionPolicyCreated')} />
          <FormField label={t('governancePage.archivePeriod')}><TextInput value={archivePeriod} onChange={setArchivePeriod} /></FormField>
          <Select label={t('governancePage.tier')} value={archiveTier} onChange={setArchiveTier} options={[{ value: 'Archive', label: t('common.archive') }, { value: 'Cold', label: t('governancePage.cold') }]} />
          <FormField label={t('governancePage.recordCount')}><TextInput value={recordCount} onChange={setRecordCount} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <Button onClick={() => archive.mutate()} loading={archive.isPending}>{t('governancePage.createArchiveManifest')}</Button>
          <LocalizedMutationFeedback mutation={archive} successMessage={t('governancePage.archiveCreated')} />
          <LocalizedDataTable density="compact" rows={state?.archives ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'period', header: t('governancePage.period'), render: row => row.period ?? '' },
            { id: 'tier', header: t('governancePage.tier'), render: row => domainLabel(row.tier) },
            { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={governanceStatusTone(row.status)} /> },
            { id: 'actions', header: t('common.actions'), render: row => <Button tone="neutral" size="small" onClick={() => restoreArchive.mutate(row.id ?? '')} disabled={row.status === 'Restored'}>{t('governancePage.verifyRestore')}</Button> }
          ]} />
          <LocalizedMutationFeedback mutation={restoreArchive} successMessage={t('governancePage.archiveRestoreRequested')} />
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card padding="small" title={t('governancePage.backupAndRecovery')}>
          <Button onClick={() => backup.mutate()} loading={backup.isPending}>{t('governancePage.createLocalBackupManifest')}</Button>
          <LocalizedMutationFeedback mutation={backup} successMessage={t('governancePage.backupManifestCreated')} />
          <LocalizedDataTable density="compact" rows={state?.backups ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'profile', header: t('governancePage.profile'), render: row => row.profile ?? '' },
            { id: 'rpo', header: t('governancePage.rPO'), render: row => t('governancePage.minutes', { value0: format.number(toFiniteNumber(row.rpoMinutes)) }) },
            { id: 'rto', header: t('governancePage.rTO'), render: row => t('governancePage.minutes', { value0: format.number(toFiniteNumber(row.rtoMinutes)) }) },
            { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={governanceStatusTone(row.status)} /> },
            { id: 'actions', header: t('common.actions'), render: row => <Button tone="neutral" size="small" onClick={() => verifyRestore.mutate(row.id ?? '')} disabled={row.restoreVerified}>{t('governancePage.verifyRestore')}</Button> }
          ]} />
          <LocalizedMutationFeedback mutation={verifyRestore} successMessage={t('governancePage.restoreVerified')} />
        </Card>
        <Card padding="small" title={t('governancePage.rightsAndLicenseGates')}>
          <FormField label={t('governancePage.artifactReference')}><TextInput value={rightsRef} onChange={setRightsRef} /></FormField>
          <Select label={t('governancePage.thirdPartyRights')} value={thirdPartyRights} onChange={setThirdPartyRights} options={[{ value: 'Unknown', label: t('common.unknown') }, { value: 'Cleared', label: t('governancePage.cleared') }]} />
          <Button tone="secondary" onClick={() => recordRights.mutate()} disabled={!rightsRef} loading={recordRights.isPending}>{t('governancePage.recordRights')}</Button>
          <LocalizedMutationFeedback mutation={recordRights} successMessage={t('governancePage.rightsRecorded')} />
          <FormField label={t('governancePage.licenseArtifact')}><TextInput value={licenseRef} onChange={setLicenseRef} /></FormField>
          <FormField label={t('governancePage.license')}><TextInput value={license} onChange={setLicense} /></FormField>
          <Select label={t('governancePage.contributorAttestation')} value={contributorAttestation} onChange={setContributorAttestation} options={[
            { value: '', label: t('governancePage.notAttested') },
            { value: 'confirmed', label: t('governancePage.iConfirmContributorRightsForThisArtifact') }
          ]} />
          <Select label={t('governancePage.trademarkAttestation')} value={trademarkAttestation} onChange={setTrademarkAttestation} options={[
            { value: '', label: t('governancePage.notReviewed') },
            { value: 'reviewed', label: t('governancePage.iCompletedTheTrademarkReview') }
          ]} />
          <Button onClick={() => recordLicense.mutate()} disabled={!licenseRef.trim() || !license.trim() || contributorAttestation !== 'confirmed' || trademarkAttestation !== 'reviewed' || recordLicense.isPending} loading={recordLicense.isPending}>{t('governancePage.recordLicense')}</Button>
          <LocalizedMutationFeedback mutation={recordLicense} successMessage={t('governancePage.licenseAndAttestationsRecorded')} />
          <LocalizedDataTable density="compact" rows={state?.rights ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'artifact', header: t('governancePage.artifact'), render: row => row.artifactRef ?? '' },
            { id: 'rights', header: t('governancePage.thirdParty'), render: row => domainLabel(row.thirdPartyRights) },
            { id: 'status', header: t('common.publish'), render: row => <StatusChip label={domainLabel(row.publishStatus)} tone={row.publishStatus === 'Eligible' ? 'success' : 'error'} /> }
          ]} />
        </Card>
        </Grid>
      </Stack>
    </ContentContainer>
  );
}
