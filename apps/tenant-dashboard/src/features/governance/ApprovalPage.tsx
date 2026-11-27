import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ConfirmDialog, ContentContainer, DetailList, EmptyState, FormField,
  PageHeader, SectionHeader, Stack, StatusChip, Text, TextInput, createUuidV4
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import type { components } from '../../lib/api-client/schema';
import { useAuth } from '../../app/useAuth';
import { useDomainLabels } from '../../lib/domainLabels';
import { localizedGenericReasonDefaults, localizedSpecificTextError } from '../../lib/localizedValidation';
import { governanceStatusTone } from './governancePresentation';

type Approval = components['schemas']['ApprovalDto'];
type ApprovalDecisionValue = 'approve' | 'reject';
interface ApprovalDecisionProps { approvalId: string; data: Approval }
interface ApprovalDecisionVariables { approvalId: string; decision: string; reason: string }

export function ApprovalPage() {
  const { t } = useTranslation();
  const { approvalId = '' } = useParams();
  const { data, isLoading, error, refetch } = useQuery({ queryKey: ['approvals', approvalId], queryFn: () => api.approval(approvalId) });

  if (isLoading) return <LocalizedLoadingState label={t('governancePage.loadingApproval')} />;
  if (error) return <LocalizedErrorState error={error} onRetry={() => void refetch()} />;
  if (!data) return <EmptyState title={t('governancePage.approvalNotFound')} description={t('governancePage.theApprovalMayHaveBeenRemovedOrIsNoLongerAvailable')} />;
  return <ApprovalDecision key={approvalId} approvalId={approvalId} data={data} />;
}

function ApprovalDecision({ approvalId, data }: ApprovalDecisionProps) {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const { session } = useAuth();
  const queryClient = useQueryClient();
  const [reason, setReason] = useState('');
  const [pendingDecision, setPendingDecision] = useState<ApprovalDecisionValue | null>(null);
  const canDecide = session?.role === 'Owner' || session?.role === 'CoOwner' || session?.capabilities?.includes('tenant.write') === true;
  const normalizedReason = reason.trim();
  const reasonError = localizedSpecificTextError(reason, t('governancePage.decisionReason'), localizedGenericReasonDefaults(t), t);
  const reasonValid = reasonError === null;
  const mutation = useMutation({
    mutationFn: ({ approvalId: targetId, decision, reason: decisionReason }: ApprovalDecisionVariables) =>
      api.decideApproval(targetId, { decision, reason: decisionReason }, createUuidV4()),
    onSuccess: async () => {
      setReason('');
      await queryClient.invalidateQueries({ queryKey: ['approvals'] });
      await queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    }
  });

  const requestDecision = (decision: ApprovalDecisionValue) => {
    if (!canDecide || data.state !== 'pending' || !reasonValid || mutation.isPending || mutation.isSuccess) return;
    setPendingDecision(decision);
  };

  const confirmDecision = () => {
    if (!pendingDecision || !canDecide || data.state !== 'pending' || !reasonValid || mutation.isPending || mutation.isSuccess) return;
    mutation.mutate({ approvalId, decision: pendingDecision, reason: normalizedReason });
    setPendingDecision(null);
  };

  const decisionDisabled = data.state !== 'pending' || !reasonValid || mutation.isPending || mutation.isSuccess || pendingDecision !== null;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={data.title}
          description={data.actionType}
          actions={<StatusChip label={domainLabel(data.state)} tone={governanceStatusTone(data.state)} />}
          meta={<Text variant="caption">{t('governancePage.requesterExpires', { value0: data.requester, value1: format.dateTime(data.expiresAt) })}</Text>}
        />
        <Stack direction="row" spacing={2} sx={{ flexWrap: 'wrap', alignItems: 'flex-start' }}>
          <Stack spacing={2} sx={{ flex: '1 1 620px', minWidth: 0 }}>
            <SectionHeader title={t('common.summary')} />
            <Card padding="small"><Text>{data.impact}</Text></Card>
            <SectionHeader title={t('common.evidence')} />
            <Card padding="small">
              {data.evidenceRefs.length ? <Stack spacing={1}>
                {data.evidenceRefs.map((reference, index) => <Text key={`${reference}-${index}`} variant="mono">{reference}</Text>)}
              </Stack> : <Text variant="secondary">{t('common.notAvailable')}</Text>}
            </Card>
          </Stack>
          <Stack sx={{ flex: '0 1 380px', minWidth: 320 }}>
            <Card
              title={t('common.decision')}
              padding="small"
              sx={{ position: { lg: 'sticky' }, top: 80 }}
              footer={canDecide ? <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                <Button tone="secondary" disabled={decisionDisabled} loading={mutation.isPending && mutation.variables?.decision === 'approve'} onClick={() => requestDecision('approve')}>{t('common.approve')}</Button>
                <Button tone="danger" disabled={decisionDisabled} loading={mutation.isPending && mutation.variables?.decision === 'reject'} onClick={() => requestDecision('reject')}>{t('common.reject')}</Button>
              </Stack> : undefined}
            >
              <DetailList dense items={[
                { label: t('common.status'), value: <StatusChip label={domainLabel(data.state)} tone={governanceStatusTone(data.state)} /> },
                { label: t('common.expires'), value: format.dateTime(data.expiresAt) },
                { label: t('common.created'), value: format.dateTime(data.createdAt) }
              ]} />
              {canDecide ? <Stack spacing={1}>
                <FormField label={t('governancePage.decisionReason')} hint={t('governancePage.enterASpecificNonDefaultReason')} error={reason.trim() ? reasonError ?? undefined : undefined}>
                  <TextInput value={reason} onChange={setReason} placeholder={t('governancePage.decisionReason')} disabled={mutation.isPending || data.state !== 'pending' || pendingDecision !== null} />
                </FormField>
                <LocalizedMutationFeedback mutation={mutation} successMessage={t('governancePage.approvalDecisionRecorded')} />
              </Stack> : <Text color="error">{t('governancePage.permissionDeniedOwnerCoOwnerOrTenantWriteAuthorityIsRequiredToDecideAppr')}</Text>}
            </Card>
          </Stack>
        </Stack>
      </Stack>
      <ConfirmDialog
        open={pendingDecision !== null}
        title={t('common.decision')}
        description={pendingDecision ? t('governancePage.confirmApproval', {
          decision: pendingDecision === 'approve' ? t('common.approve') : t('common.reject'), title: data.title, reason: normalizedReason
        }) : undefined}
        confirmLabel={pendingDecision === 'reject' ? t('common.reject') : t('common.approve')}
        cancelLabel={t('common.no')}
        tone={pendingDecision === 'reject' ? 'danger' : 'primary'}
        loading={mutation.isPending}
        onConfirm={confirmDecision}
        onClose={() => { if (!mutation.isPending) setPendingDecision(null); }}
      />
    </ContentContainer>
  );
}
