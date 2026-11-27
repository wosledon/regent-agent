import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Button, Card, FormField, Grid, MetricCard, SectionHeader, Select, Stack, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { useAuth } from '../../app/useAuth';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';

type Finding = Awaited<ReturnType<typeof api.memoryFindings>>[number];
type Correction = Awaited<ReturnType<typeof api.memoryCorrections>>[number];
type SafetyCommand =
  | { kind: 'quarantine'; finding: Finding }
  | { kind: 'appeal'; finding: Finding }
  | { kind: 'review'; finding: Finding; release: boolean }
  | { kind: 'createCorrection' }
  | { kind: 'approveCorrection'; correction: Correction };

interface MemorySafetyPanelProps {
  onChanged: () => void | Promise<void>;
}

export function MemorySafetyPanel({ onChanged }: MemorySafetyPanelProps) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const { session } = useAuth();
  const [reviewer, setReviewer] = useState(session?.displayName ?? '');
  const [findingId, setFindingId] = useState('');
  const [claimId, setClaimId] = useState('');
  const [correctionContent, setCorrectionContent] = useState('');
  const [correctionReason, setCorrectionReason] = useState('');
  const [appealReason, setAppealReason] = useState('');
  const [quarantineReason, setQuarantineReason] = useState('');
  const [quarantineScope, setQuarantineScope] = useState('organization');
  const [reviewResolution, setReviewResolution] = useState('');
  const findings = useQuery({ queryKey: ['memory-findings'], queryFn: api.memoryFindings });
  const corrections = useQuery({ queryKey: ['memory-corrections'], queryFn: api.memoryCorrections });
  const safety = useMutation({
    mutationFn: async (command: SafetyCommand) => {
      switch (command.kind) {
        case 'quarantine':
          return api.quarantineMemoryFinding(command.finding.id, reviewer.trim(), quarantineReason.trim(), quarantineScope);
        case 'appeal':
          return api.appealMemoryFinding(command.finding.id, appealReason.trim(), session?.displayName ?? reviewer.trim());
        case 'review':
          return api.reviewMemoryAppeal(command.finding.id, reviewer.trim(), reviewResolution.trim(), command.release);
        case 'createCorrection':
          return api.createMemoryCorrection({ claimId: claimId.trim(), content: correctionContent.trim(), reason: correctionReason.trim(), reviewerId: reviewer.trim(), findingId: findingId.trim() || undefined });
        case 'approveCorrection':
          return api.approveMemoryCorrection(command.correction.id, command.correction.reviewerId ?? reviewer.trim());
      }
    },
    onSuccess: async (_result, command) => {
      if (command.kind === 'createCorrection') setCorrectionContent('');
      if (command.kind === 'appeal') setAppealReason('');
      if (command.kind === 'review') setReviewResolution('');
      await onChanged();
    }
  });

  if (findings.isLoading || corrections.isLoading) return <LocalizedLoadingState />;
  if (findings.error || corrections.error) return <LocalizedErrorState error={findings.error ?? corrections.error} />;

  const findingRows = findings.data ?? [];
  const correctionRows = corrections.data ?? [];
  const prepareCorrection = (finding: Finding) => {
    setFindingId(finding.id);
    setClaimId(finding.claimId);
  };
  const pendingKind = safety.isPending ? safety.variables.kind : '';
  const reviewerReady = !!reviewer.trim();
  const correctionReady = reviewerReady && !!claimId.trim() && !!correctionContent.trim() && !!correctionReason.trim();
  const openFindings = findingRows.filter(finding => finding.status === 'Open').length;
  const quarantinedFindings = findingRows.filter(finding => finding.status === 'Quarantined').length;
  const appealedFindings = findingRows.filter(finding => finding.status === 'Appealed').length;
  const pendingCorrections = correctionRows.filter(correction => correction.status === 'PendingReview').length;

  return (
    <Stack spacing={3}>
      <Grid columns={4}>
        <MetricCard label={t('memoryPage.poisoningFindings')} value={format.number(openFindings)} tone={openFindings > 0 ? 'error' : 'default'} icon="security" />
        <MetricCard label={t('common.domain.quarantined')} value={format.number(quarantinedFindings)} tone={quarantinedFindings > 0 ? 'warning' : 'default'} icon="governance" />
        <MetricCard label={t('common.domain.appealed')} value={format.number(appealedFindings)} tone={appealedFindings > 0 ? 'warning' : 'default'} icon="approval" />
        <MetricCard label={t('memoryPage.correctionTasks')} value={format.number(pendingCorrections)} tone={pendingCorrections > 0 ? 'warning' : 'default'} icon="edit" />
      </Grid>
      <Stack spacing={2}>
        <SectionHeader title={t('memoryPage.poisoningFindings')} action={<StatusChip label={format.number(findingRows.length)} tone={findingRows.length > 0 ? 'warning' : 'neutral'} />} />
        <Card padding="small">
          <LocalizedDataTable
            rows={findingRows}
            getRowId={row => row.id}
            density="compact"
            minWidth={980}
            emptyTitle={t('memoryPage.noMemoryFindings')}
            columns={[
              { id: 'rule', header: t('memoryPage.rule'), render: row => <Stack spacing={0.25}><Text variant="mono">{row.ruleId}</Text><Text variant="caption">{row.claimId}</Text></Stack> },
              { id: 'risk', header: t('memoryPage.risk'), render: row => <StatusChip label={domainLabel(row.riskLevel)} tone={row.riskLevel === 'High' ? 'error' : 'warning'} /> },
              { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Released' || row.status === 'Corrected' ? 'success' : 'warning'} /> },
              { id: 'evidence', header: t('common.evidence'), render: row => <Stack spacing={0.25}><Text variant="secondary">{row.reason || t('common.notAvailable')}</Text><Text variant="mono">{(row.evidenceRefs ?? []).join(' · ') || t('common.notAvailable')}</Text></Stack> },
              { id: 'review', header: t('common.review'), render: row => <Stack spacing={0.25}><Text variant="caption">{row.reviewedBy ?? t('common.unknown')}</Text><Text variant="caption">{row.resolution ?? t('common.notAvailable')}</Text><Text variant="caption">{t('common.expires')}: {row.reviewDueAt ? format.dateTime(row.reviewDueAt) : t('common.notAvailable')}</Text></Stack> },
              { id: 'actions', header: t('common.actions'), render: row => {
                const nextAction = row.status === 'Open' ? t('memoryPage.quarantine') : row.status === 'Quarantined' ? t('common.appeal') : row.status === 'Appealed' ? t('common.review') : t('common.domain.completed');
                return <Stack spacing={0.75}>
                  <Text variant="caption">{nextAction}</Text>
                  <Stack direction="row" wrap spacing={0.5}>
                    {row.status === 'Open' ? <Button tone="danger" size="small" disabled={!reviewerReady || !quarantineReason.trim() || safety.isPending} loading={safety.isPending && safety.variables?.kind === 'quarantine' && safety.variables.finding.id === row.id} onClick={() => safety.mutate({ kind: 'quarantine', finding: row })}>{t('memoryPage.quarantine')}</Button> : null}
                    {row.status === 'Quarantined' ? <Button tone="neutral" size="small" disabled={!appealReason.trim() || safety.isPending} loading={safety.isPending && safety.variables?.kind === 'appeal' && safety.variables.finding.id === row.id} onClick={() => safety.mutate({ kind: 'appeal', finding: row })}>{t('common.appeal')}</Button> : null}
                    {row.status === 'Appealed' ? <Button tone="secondary" size="small" disabled={!reviewerReady || !reviewResolution.trim() || safety.isPending} loading={safety.isPending && safety.variables?.kind === 'review' && safety.variables.finding.id === row.id && safety.variables.release} onClick={() => safety.mutate({ kind: 'review', finding: row, release: true })}>{t('common.release')}</Button> : null}
                    {row.status === 'Appealed' ? <Button tone="danger" size="small" disabled={!reviewerReady || !reviewResolution.trim() || safety.isPending} loading={safety.isPending && safety.variables?.kind === 'review' && safety.variables.finding.id === row.id && !safety.variables.release} onClick={() => safety.mutate({ kind: 'review', finding: row, release: false })}>{t('memoryPage.keepQuarantined')}</Button> : null}
                    {row.status !== 'Corrected' ? <Button tone="neutral" size="small" disabled={safety.isPending} onClick={() => prepareCorrection(row)}>{t('memoryPage.prepareCorrection')}</Button> : null}
                  </Stack>
                </Stack>;
              } }
            ]}
          />
        </Card>
      </Stack>
      <Grid columns={2}>
        <Card title={t('memoryPage.independentReviewer')} headingLevel="h2" padding="small">
          <Stack spacing={2}>
            <FormField label={t('memoryPage.independentReviewer')}>
              <TextInput value={reviewer} onChange={setReviewer} placeholder={t('memoryPage.reviewerIdentity')} />
            </FormField>
            <Text variant="label">{t('memoryPage.quarantine')}</Text>
            <FormField label={t('memoryPage.quarantineReason')}>
              <TextInput value={quarantineReason} onChange={setQuarantineReason} placeholder={t('memoryPage.evidenceBasedSafetyReason')} />
            </FormField>
            <Select label={t('memoryPage.quarantineScope')} value={quarantineScope} onChange={setQuarantineScope} options={[
              { value: 'personal', label: domainLabel('personal') }, { value: 'organization', label: domainLabel('organization') }, { value: 'knowledge_base', label: domainLabel('knowledge_base') }
            ]} />
            <Text variant="label">{t('common.appeal')}</Text>
            <FormField label={t('memoryPage.appealReason')}>
              <TextInput value={appealReason} onChange={setAppealReason} placeholder={t('memoryPage.evidenceForAppeal')} />
            </FormField>
            <Text variant="label">{t('common.review')}</Text>
            <FormField label={t('memoryPage.appealReviewResolution')}>
              <TextInput value={reviewResolution} onChange={setReviewResolution} placeholder={t('memoryPage.actualReviewFindingsAndResolution')} />
            </FormField>
          </Stack>
        </Card>
        <Card title={t('memoryPage.independentCorrection')} headingLevel="h2" padding="small">
          <Stack spacing={2}>
            <FormField label={t('memoryPage.claimID')}><TextInput value={claimId} onChange={setClaimId} placeholder={t('memoryPage.selectAFindingOrEnterAClaimId')} /></FormField>
            <FormField label={t('memoryPage.findingID')}><TextInput value={findingId} onChange={setFindingId} placeholder={t('memoryPage.optionalFindingId')} /></FormField>
            <FormField label={t('memoryPage.proposedContent')}><TextInput multiline minRows={2} value={correctionContent} onChange={setCorrectionContent} placeholder={t('memoryPage.correctedClaim')} /></FormField>
            <FormField label={t('common.reason')}><TextInput value={correctionReason} onChange={setCorrectionReason} placeholder={t('memoryPage.reasonForIndependentCorrection')} /></FormField>
            <Button size="small" disabled={!correctionReady || safety.isPending} loading={pendingKind === 'createCorrection'} onClick={() => safety.mutate({ kind: 'createCorrection' })}>{t('memoryPage.createCorrection')}</Button>
          </Stack>
        </Card>
      </Grid>
      <Stack spacing={2}>
        <SectionHeader title={t('memoryPage.correctionTasks')} action={<StatusChip label={format.number(correctionRows.length)} tone={pendingCorrections > 0 ? 'warning' : 'neutral'} />} />
        <Card padding="small">
          <LocalizedDataTable rows={correctionRows} getRowId={row => row.id} density="compact" minWidth={860} emptyTitle={t('memoryPage.noCorrectionTasks')} columns={[
            { id: 'claim', header: t('memoryPage.claim'), render: row => <Stack spacing={0.25}><Text variant="mono">{row.claimId}</Text><Text variant="caption">{row.findingId ?? t('common.notAvailable')}</Text></Stack> },
            { id: 'reviewer', header: t('memoryPage.reviewer'), render: row => row.reviewerId ?? domainLabel('Unassigned') },
            { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Completed' ? 'success' : 'warning'} /> },
            { id: 'proposal', header: t('memoryPage.proposal'), render: row => <Stack spacing={0.25}><Text variant="secondary">{row.proposedContent ?? t('memoryPage.proposalRequired')}</Text><Text variant="caption">{row.reason}</Text></Stack> },
            { id: 'created', header: t('common.created'), render: row => format.dateTime(row.createdAt) },
            { id: 'action', header: t('common.action'), render: row => row.status === 'PendingReview' && row.proposedContent
              ? <Button tone="secondary" size="small" disabled={!reviewerReady || safety.isPending} loading={safety.isPending && safety.variables?.kind === 'approveCorrection' && safety.variables.correction.id === row.id} onClick={() => safety.mutate({ kind: 'approveCorrection', correction: row })}>{t('memoryPage.approveCorrection')}</Button> : <StatusChip label={domainLabel(row.status)} tone={row.status === 'Completed' ? 'success' : 'neutral'} /> }
          ]} />
        </Card>
      </Stack>
      <LocalizedMutationFeedback mutation={safety} successMessage={t('memoryPage.memorySafetyActionCompleted')} />
    </Stack>
  );
}
