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
import { stableOccurrenceKeys } from '../../lib/stableKeys';
import { governanceRiskTone, governanceStatusTone } from './governancePresentation';

export function GovernancePage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const governance = useQuery({ queryKey: ['governance'], queryFn: api.governance });
  const [toolName, setToolName] = useState('');
  const [toolVersion, setToolVersion] = useState('1.0.0');
  const [toolCapabilities, setToolCapabilities] = useState('');
  const [scanContent, setScanContent] = useState('');
  const [scanResult, setScanResult] = useState<Awaited<ReturnType<typeof api.scanInjection>> | null>(null);
  const [dlpContent, setDlpContent] = useState('');
  const [dlpResult, setDlpResult] = useState<Awaited<ReturnType<typeof api.redactDlp>> | null>(null);
  const [incidentTitle, setIncidentTitle] = useState('');
  const [severity, setSeverity] = useState('SEV3');
  const [commander, setCommander] = useState('Operator');
  const [primary, setPrimary] = useState('Owner');
  const [delegate, setDelegate] = useState('Controller');
  const [scope, setScope] = useState('release.execute');
  const [expiresAt, setExpiresAt] = useState('');
  const [contentRef, setContentRef] = useState('');
  const [contentText, setContentText] = useState('');
  const [appealReason, setAppealReason] = useState('');
  const [incidentId, setIncidentId] = useState('');
  const [actionIncidentId, setActionIncidentId] = useState('');
  const [containmentAction, setContainmentAction] = useState('');
  const [recoverySummary, setRecoverySummary] = useState('');
  const [retrospective, setRetrospective] = useState('');
  const [evidenceText, setEvidenceText] = useState('');
  const [verification, setVerification] = useState<Awaited<ReturnType<typeof api.verifyIncidentEvidence>> | null>(null);
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['governance'] });

  const registerTool = useMutation({
    mutationFn: () => api.registerTool({
      id: createUuidV4(), name: toolName, version: toolVersion,
      capabilities: toolCapabilities.split(',').map(value => value.trim()).filter(Boolean),
      sbomJson: JSON.stringify({ vulnerabilities: [] }), digest: '', signature: '', status: 'Draft', createdAt: new Date().toISOString()
    }), onSuccess: async () => { setToolName(''); await invalidate(); }
  });
  const admitTool = useMutation({ mutationFn: (id: string) => api.admitTool(id, 'Security Reviewer'), onSuccess: invalidate });
  const scan = useMutation({ mutationFn: () => api.scanInjection(scanContent), onSuccess: setScanResult });
  const redact = useMutation({ mutationFn: () => api.redactDlp(dlpContent), onSuccess: setDlpResult });
  const createIncident = useMutation({
    mutationFn: () => api.createIncident({
      id: createUuidV4(), severity, title: incidentTitle, commander,
      status: 'Open', containmentActions: [], evidenceRefs: [], notifications: [],
      recoverySummary: null, retrospective: null, createdAt: new Date().toISOString()
    }), onSuccess: async () => { setIncidentTitle(''); await invalidate(); }
  });
  const contain = useMutation({
    mutationFn: ({ id, action }: { id: string; action: string }) => api.containIncident(id, action.trim()),
    onSuccess: async () => { setContainmentAction(''); await invalidate(); }
  });
  const recover = useMutation({
    mutationFn: ({ id, summary }: { id: string; summary: string }) => api.recoverIncident(id, summary.trim()),
    onSuccess: async () => { setRecoverySummary(''); await invalidate(); }
  });
  const close = useMutation({
    mutationFn: ({ id, retrospective: summary }: { id: string; retrospective: string }) => api.closeIncident(id, summary.trim()),
    onSuccess: async () => { setRetrospective(''); await invalidate(); }
  });
  const review = useMutation({ mutationFn: () => api.reviewContent(contentRef, contentText), onSuccess: async () => { setContentText(''); await invalidate(); } });
  const appeal = useMutation({ mutationFn: (id: string) => api.appealContent(id, appealReason), onSuccess: async () => { setAppealReason(''); await invalidate(); } });
  const resolveAppeal = useMutation({ mutationFn: ({ id, upheld }: { id: string; upheld: boolean }) => api.resolveContentAppeal(id, upheld), onSuccess: invalidate });
  const sealEvidence = useMutation({ mutationFn: () => api.sealIncidentEvidence(incidentId, evidenceText), onSuccess: async () => { setEvidenceText(''); await invalidate(); } });
  const verifyEvidence = useMutation({ mutationFn: () => api.verifyIncidentEvidence(incidentId), onSuccess: setVerification });
  const createDelegation = useMutation({
    mutationFn: () => api.createApprovalDelegation({
      id: createUuidV4(), primaryApprover: primary, delegateApprover: delegate,
      actionScope: scope, expiresAt: new Date(expiresAt).toISOString(), status: 'Active', reason: 'Planned absence'
    }), onSuccess: async () => { setExpiresAt(''); await invalidate(); }
  });

  if (governance.isLoading) return <LocalizedLoadingState />;
  if (governance.error) return <LocalizedErrorState error={governance.error} />;
  const state = governance.data;
  const actionIncident = state?.incidents?.find(incident => incident.id === actionIncidentId);
  const responsePending = contain.isPending || recover.isPending || close.isPending;
  const findings = scanResult?.findings ?? [];
  const findingKeys = stableOccurrenceKeys(findings);

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader title={t('governancePage.securityGovernance')} description={t('governancePage.executionBoundaryAuthorizationSupplyChainDLPContentSafetyAndIncidentResp')} />
        <Grid columns={4}>
          <MetricCard label={t('governancePage.toolSupplyChain')} value={state?.tools?.length ?? 0} icon="governance" />
          <MetricCard label={t('governancePage.securityIncident')} value={state?.incidents?.length ?? 0} icon="security" tone={(state?.incidents?.length ?? 0) > 0 ? 'error' : 'default'} />
          <MetricCard label={t('governancePage.approvalAbsenceDelegation')} value={state?.approvalDelegations?.length ?? 0} icon="approval" />
          <MetricCard label={t('governancePage.contentSafety')} value={state?.contentFindings?.length ?? 0} icon="audit" tone={(state?.contentFindings?.length ?? 0) > 0 ? 'warning' : 'default'} />
        </Grid>
        <Grid columns={2}>
        <Card padding="small" title={t('governancePage.toolSupplyChain')}>
          <FormField label={t('governancePage.toolName')}><TextInput value={toolName} onChange={setToolName} placeholder={t('governancePage.researchSearch')} /></FormField>
          <FormField label={t('common.version')}><TextInput value={toolVersion} onChange={setToolVersion} /></FormField>
          <FormField label={t('governancePage.capabilities')}><TextInput value={toolCapabilities} onChange={setToolCapabilities} placeholder={t('governancePage.researchExecute')} /></FormField>
          <Button onClick={() => registerTool.mutate()} disabled={!toolName || !toolCapabilities} loading={registerTool.isPending}>{t('governancePage.registerImmutableTool')}</Button>
          <LocalizedMutationFeedback mutation={registerTool} successMessage={t('governancePage.immutableToolRegistered')} />
          <LocalizedDataTable density="compact" rows={state?.tools ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'name', header: t('governancePage.tool'), render: row => `${row.name ?? ''}@${row.version ?? ''}` },
            { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Admitted' ? 'success' : 'warning'} /> },
            { id: 'action', header: t('common.action'), render: row => <Button tone="secondary" size="small" onClick={() => admitTool.mutate(row.id ?? '')} disabled={row.status !== 'Draft'}>{t('governancePage.admit')}</Button> }
          ]} />
          <LocalizedMutationFeedback mutation={admitTool} successMessage={t('governancePage.toolAdmitted')} />
        </Card>
        <Card padding="small" title={t('governancePage.promptAndDataBoundaries')}>
          <FormField label={t('governancePage.untrustedContent')}><TextInput value={scanContent} onChange={setScanContent} multiline minRows={3} placeholder={t('governancePage.ignorePreviousInstructions')} /></FormField>
          <Button onClick={() => scan.mutate()} disabled={!scanContent} loading={scan.isPending}>{t('governancePage.scanInjection')}</Button>
          {scanResult ? <StatusChip label={scanResult.safe ? t('governancePage.noInjectionPattern') : t('governancePage.quarantine')} tone={scanResult.safe ? 'success' : 'error'} /> : null}
          {findings.map((finding, index) => <Text key={findingKeys[index]} variant="caption">{finding}</Text>)}
          <LocalizedMutationFeedback mutation={scan} showSuccess={false} />
          <FormField label={t('governancePage.sensitiveContent')}><TextInput value={dlpContent} onChange={setDlpContent} multiline minRows={2} placeholder={t('governancePage.contactOwnerExampleTest')} /></FormField>
          <Button tone="secondary" onClick={() => redact.mutate()} disabled={!dlpContent} loading={redact.isPending}>{t('governancePage.redactAndTokenize')}</Button>
          {dlpResult ? <Text variant="caption">{dlpResult.redactedText || t('common.notAvailable')}</Text> : null}
          <LocalizedMutationFeedback mutation={redact} showSuccess={false} />
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card padding="small" title={t('governancePage.securityIncident')}>
          <FormField label={t('common.title')}><TextInput value={incidentTitle} onChange={setIncidentTitle} placeholder={t('governancePage.credentialExposure')} /></FormField>
          <Select label={t('common.severity')} value={severity} onChange={setSeverity} options={['SEV1', 'SEV2', 'SEV3', 'SEV4'].map(value => ({ value, label: domainLabel(value) }))} />
          <FormField label={t('governancePage.incidentCommander')}><TextInput value={commander} onChange={setCommander} /></FormField>
          <Button tone="danger" onClick={() => createIncident.mutate()} disabled={!incidentTitle.trim() || createIncident.isPending} loading={createIncident.isPending}>{t('governancePage.declareIncident')}</Button>
          <LocalizedMutationFeedback mutation={createIncident} successMessage={t('governancePage.securityIncidentDeclared')} />
          <Select label={t('governancePage.incidentResponse')} value={actionIncidentId} onChange={setActionIncidentId} options={[{ value: '', label: t('governancePage.selectIncident') }, ...(state?.incidents ?? []).map(item => ({ value: item.id ?? '', label: t('governancePage.incidentOption', { title: item.title ?? '', status: domainLabel(item.status) }) }))]} />
          <FormField label={t('governancePage.containmentAction')}><TextInput value={containmentAction} onChange={setContainmentAction} placeholder={t('governancePage.describeTheActualContainmentPerformed')} /></FormField>
          <Button tone="neutral" disabled={!actionIncident || actionIncident.status !== 'Open' || !containmentAction.trim() || responsePending} loading={contain.isPending} onClick={() => actionIncident?.id && contain.mutate({ id: actionIncident.id, action: containmentAction })}>{t('governancePage.recordContainment')}</Button>
          <FormField label={t('governancePage.recoverySummary')}><TextInput value={recoverySummary} onChange={setRecoverySummary} multiline minRows={2} placeholder={t('governancePage.describeRestoredServiceAndVerificationEvidence')} /></FormField>
          <Button tone="neutral" disabled={!actionIncident || actionIncident.status !== 'Contained' || !recoverySummary.trim() || responsePending} loading={recover.isPending} onClick={() => actionIncident?.id && recover.mutate({ id: actionIncident.id, summary: recoverySummary })}>{t('governancePage.recordRecovery')}</Button>
          <FormField label={t('governancePage.retrospective')}><TextInput value={retrospective} onChange={setRetrospective} multiline minRows={2} placeholder={t('governancePage.documentCausesLessonsAndFollowUpActions')} /></FormField>
          <Button tone="secondary" disabled={!actionIncident || actionIncident.status !== 'Recovered' || !retrospective.trim() || responsePending} loading={close.isPending} onClick={() => actionIncident?.id && close.mutate({ id: actionIncident.id, retrospective })}>{t('governancePage.closeWithRetrospective')}</Button>
          <LocalizedMutationFeedback mutation={contain} successMessage={t('governancePage.containmentRecorded')} />
          <LocalizedMutationFeedback mutation={recover} successMessage={t('governancePage.recoveryRecorded')} />
          <LocalizedMutationFeedback mutation={close} successMessage={t('governancePage.incidentClosedWithRetrospective')} />
          <LocalizedDataTable density="compact" rows={state?.incidents ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'title', header: t('governancePage.incident'), render: row => row.title ?? '' },
            { id: 'severity', header: t('common.severity'), render: row => <StatusChip label={domainLabel(row.severity)} tone={governanceRiskTone(row.severity)} /> },
            { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={governanceStatusTone(row.status)} /> },
            { id: 'actions', header: t('common.actions'), render: row => <Button tone="neutral" size="small" onClick={() => setActionIncidentId(row.id ?? '')} disabled={!row.id}>{t('governancePage.selectForResponse')}</Button> }
          ]} />
        </Card>
        <Card padding="small" title={t('governancePage.approvalAbsenceDelegation')}>
          <FormField label={t('governancePage.primaryApprover')}><TextInput value={primary} onChange={setPrimary} /></FormField>
          <FormField label={t('governancePage.delegate')}><TextInput value={delegate} onChange={setDelegate} /></FormField>
          <FormField label={t('governancePage.actionScope')}><TextInput value={scope} onChange={setScope} /></FormField>
          <FormField label={t('governancePage.expiresAt')}><TextInput value={expiresAt} onChange={setExpiresAt} type="datetime-local" /></FormField>
          <Button onClick={() => createDelegation.mutate()} disabled={!expiresAt || primary === delegate} loading={createDelegation.isPending}>{t('governancePage.createDelegation')}</Button>
          <LocalizedMutationFeedback mutation={createDelegation} successMessage={t('governancePage.approvalDelegationCreated')} />
          <LocalizedDataTable density="compact" rows={state?.approvalDelegations ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'primary', header: t('governancePage.primary'), render: row => row.primaryApprover ?? '' },
            { id: 'delegate', header: t('governancePage.delegate'), render: row => row.delegateApprover ?? '' },
            { id: 'scope', header: t('common.scope'), render: row => row.actionScope ?? '' },
            { id: 'expires', header: t('common.expires'), render: row => format.dateTime(row.expiresAt) }
          ]} />
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card padding="small" title={t('governancePage.contentSafety')}>
          <FormField label={t('governancePage.contentReference')}><TextInput value={contentRef} onChange={setContentRef} /></FormField>
          <FormField label={t('common.content')}><TextInput value={contentText} onChange={setContentText} multiline minRows={2} /></FormField>
          <Button onClick={() => review.mutate()} disabled={!contentRef || !contentText} loading={review.isPending}>{t('governancePage.reviewContent')}</Button>
          <LocalizedDataTable density="compact" rows={state?.contentFindings ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'ref', header: t('common.content'), render: row => row.contentRef ?? '' },
            { id: 'category', header: t('common.category'), render: row => row.category ?? '' },
            { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.disposition === 'Block' ? 'error' : 'warning'} /> },
            { id: 'actions', header: t('common.actions'), render: row => <Stack direction="row" wrap>
              <Button tone="neutral" size="small" onClick={() => appeal.mutate(row.id ?? '')} disabled={row.status !== 'Open'}>{t('common.appeal')}</Button>
              <Button tone="secondary" size="small" onClick={() => resolveAppeal.mutate({ id: row.id ?? '', upheld: true })} disabled={row.status !== 'UnderReview'}>{t('governancePage.uphold')}</Button>
              <Button tone="secondary" size="small" onClick={() => resolveAppeal.mutate({ id: row.id ?? '', upheld: false })} disabled={row.status !== 'UnderReview'}>{t('governancePage.overturn')}</Button>
            </Stack> }
          ]} />
          <FormField label={t('governancePage.appealReason')}><TextInput value={appealReason} onChange={setAppealReason} /></FormField>
          <LocalizedMutationFeedback mutation={review} successMessage={t('governancePage.contentReviewRecorded')} />
          <LocalizedMutationFeedback mutation={appeal} successMessage={t('governancePage.contentAppealOpened')} />
          <LocalizedMutationFeedback mutation={resolveAppeal} successMessage={t('governancePage.contentAppealResolved')} />
        </Card>
        <Card padding="small" title={t('governancePage.incidentEvidence')}>
          <Select label={t('governancePage.incident')} value={incidentId} onChange={setIncidentId} options={[{ value: '', label: t('governancePage.selectIncident') }, ...(state?.incidents ?? []).map(item => ({ value: item.id ?? '', label: item.title ?? '' }))]} />
          <FormField label={t('common.evidence')}><TextInput value={evidenceText} onChange={setEvidenceText} multiline minRows={2} /></FormField>
          <Stack direction="row">
            <Button onClick={() => sealEvidence.mutate()} disabled={!incidentId || !evidenceText} loading={sealEvidence.isPending}>{t('governancePage.signEvidence')}</Button>
            <Button tone="secondary" onClick={() => verifyEvidence.mutate()} disabled={!incidentId} loading={verifyEvidence.isPending}>{t('governancePage.verifyEvidence')}</Button>
          </Stack>
          {verification ? <StatusChip label={verification.valid ? t('governancePage.evidenceValid') : t('governancePage.evidenceInvalid')} tone={verification.valid ? 'success' : 'error'} /> : null}
          <LocalizedMutationFeedback mutation={sealEvidence} successMessage={t('governancePage.incidentEvidenceSealed')} />
          <LocalizedMutationFeedback mutation={verifyEvidence} showSuccess={false} />
        </Card>
        </Grid>
      </Stack>
    </ContentContainer>
  );
}
