import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Card,
  ConfirmDialog,
  ContentContainer,
  FormField,
  Grid,
  MetricCard,
  PageHeader,
  SectionHeader,
  Select,
  SemanticLink,
  Stack,
  StatusChip,
  Text,
  TextInput,
  type ChipTone
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';

function actionTone(actionType: string): ChipTone {
  return actionType === 'Terminate' ? 'error' : actionType === 'Reinstate' ? 'success' : 'info';
}

function appealTone(status: string): ChipTone {
  if (status === 'Open') return 'warning';
  if (status === 'Upheld') return 'error';
  if (status === 'Reversed') return 'success';
  return 'neutral';
}

function improvementTone(status: string): ChipTone {
  if (status === 'Approved') return 'success';
  if (status === 'Rejected') return 'error';
  return 'warning';
}

export function PerformanceActionsPage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const queryClient = useQueryClient();
  const actions = useQuery({ queryKey: ['performance-actions'], queryFn: api.performanceActions });
  const improvements = useQuery({ queryKey: ['improvements'], queryFn: api.improvements });
  const [employeeId, setEmployeeId] = useState('');
  const [actionType, setActionType] = useState('Coaching');
  const [reason, setReason] = useState('');
  const [approvalId, setApprovalId] = useState('');
  const [category, setCategory] = useState('process');
  const [summary, setSummary] = useState('');
  const [benefit, setBenefit] = useState('');
  const [terminationConfirmOpen, setTerminationConfirmOpen] = useState(false);
  const [appealToReverse, setAppealToReverse] = useState<{ id: string; employeeId: string } | null>(null);
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['performance-actions'] }),
    queryClient.invalidateQueries({ queryKey: ['improvements'] })
  ]);
  const perform = useMutation({
    mutationFn: () => api.performAction(employeeId, actionType, reason, approvalId || undefined),
    onSuccess: async () => { setReason(''); setApprovalId(''); await invalidate(); }
  });
  const propose = useMutation({
    mutationFn: () => api.proposeImprovement(employeeId, category, summary, benefit),
    onSuccess: async () => { setSummary(''); setBenefit(''); await invalidate(); }
  });
  const appeal = useMutation({
    mutationFn: ({ id, reason: appealReason }: { id: string; reason: string }) => api.appealPerformanceAction(id, appealReason),
    onSuccess: invalidate
  });
  const resolveAppeal = useMutation({
    mutationFn: ({ id, upheld }: { id: string; upheld: boolean }) => api.resolvePerformanceAppeal(id, upheld),
    onSuccess: invalidate
  });
  const reviewImprovement = useMutation({
    mutationFn: ({ id, approved }: { id: string; approved: boolean }) => api.reviewImprovement(id, approved),
    onSuccess: invalidate
  });

  if (actions.isLoading || improvements.isLoading) return <LocalizedLoadingState />;
  const error = actions.error ?? improvements.error;
  if (error) return <LocalizedErrorState error={error} />;

  const actionRows = actions.data ?? [];
  const improvementRows = improvements.data ?? [];
  const openAppeals = actionRows.filter(row => row.appealStatus === 'Open').length;
  const pendingImprovements = improvementRows.filter(row => row.status === 'Proposed').length;
  const executeDisabled = !employeeId.trim() || !reason.trim() || (actionType === 'Terminate' && !approvalId.trim()) || perform.isPending;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('performancePage.performanceActions')}
          description={t('performancePage.aIAssistedNonTerminationActionsHumanApprovedTerminationAndAppeals')}
          meta={<>
            <StatusChip label={t('common.status')} tone="info" />
            <StatusChip label={t('common.evidence')} tone="neutral" />
          </>}
        />
        <Grid columns={3}>
          <MetricCard label={t('performancePage.performanceActions')} value={format.number(actionRows.length)} hint={t('performancePage.performAction')} icon="performance" />
          <MetricCard label={t('common.appeal')} value={format.number(openAppeals)} hint={t('performancePage.performanceAppealOpened')} icon="approval" tone={openAppeals > 0 ? 'warning' : 'default'} />
          <MetricCard label={t('performancePage.employeeImprovementProposal')} value={format.number(pendingImprovements)} hint={t('performancePage.submitProposal')} icon="workflow" tone={pendingImprovements > 0 ? 'info' : 'default'} />
        </Grid>

        <Grid columns={2}>
          <Card
            title={t('performancePage.performAction')}
            description={t('performancePage.aIAssistedNonTerminationActionsHumanApprovedTerminationAndAppeals')}
            headingLevel="h2"
            padding="small"
            action={<StatusChip label={domainLabel(actionType)} tone={actionTone(actionType)} />}
          >
            <Grid columns={2}>
              <FormField label={t('common.employeeID')} required>
                <TextInput value={employeeId} onChange={setEmployeeId} disabled={perform.isPending} />
              </FormField>
              <Select label={t('common.action')} value={actionType} onChange={setActionType} disabled={perform.isPending} options={['Coaching', 'Transfer', 'Demotion', 'Freeze', 'Terminate', 'Reinstate'].map(value => ({ value, label: domainLabel(value) }))} />
            </Grid>
            <FormField label={t('common.reason')} required>
              <TextInput value={reason} onChange={setReason} multiline minRows={3} disabled={perform.isPending} />
            </FormField>
            {actionType === 'Terminate' ? <FormField label={t('performancePage.bossApprovalID')} required>
              <TextInput value={approvalId} onChange={setApprovalId} disabled={perform.isPending} />
            </FormField> : null}
            <Stack direction="row" spacing={1.5} alignItems="center" wrap>
              <Button
                tone={actionType === 'Terminate' ? 'danger' : 'primary'}
                onClick={() => actionType === 'Terminate' ? setTerminationConfirmOpen(true) : perform.mutate()}
                disabled={executeDisabled}
                loading={perform.isPending}
              >
                {t('performancePage.executeAction')}
              </Button>
              <LocalizedMutationFeedback mutation={perform} successMessage={t('performancePage.performanceActionRecorded')} />
            </Stack>
          </Card>

          <Card title={t('performancePage.employeeImprovementProposal')} headingLevel="h2" padding="small" action={<StatusChip label={domainLabel(category)} tone="info" />}>
            <Grid columns={2}>
              <FormField label={t('common.employeeID')} required>
                <TextInput value={employeeId} onChange={setEmployeeId} disabled={propose.isPending} />
              </FormField>
              <Select label={t('common.category')} value={category} onChange={setCategory} disabled={propose.isPending} options={['process', 'skill', 'tool', 'model', 'organization'].map(value => ({ value, label: domainLabel(value) }))} />
            </Grid>
            <FormField label={t('common.summary')} required>
              <TextInput value={summary} onChange={setSummary} multiline minRows={3} disabled={propose.isPending} />
            </FormField>
            <FormField label={t('performancePage.expectedBenefit')} required>
              <TextInput value={benefit} onChange={setBenefit} disabled={propose.isPending} />
            </FormField>
            <Stack direction="row" spacing={1.5} alignItems="center" wrap>
              <Button tone="secondary" onClick={() => propose.mutate()} disabled={!employeeId.trim() || !summary.trim() || !benefit.trim() || propose.isPending} loading={propose.isPending}>{t('performancePage.submitProposal')}</Button>
              <LocalizedMutationFeedback mutation={propose} successMessage={t('performancePage.improvementProposed')} />
            </Stack>
          </Card>
        </Grid>

        <SectionHeader title={t('performancePage.performanceActions')} action={<StatusChip label={format.number(actionRows.length)} tone="neutral" />} />
        <Card padding="none">
          <LocalizedDataTable
            rows={actionRows}
            getRowId={row => row.id}
            density="compact"
            minWidth={1120}
            ariaLabel={t('performancePage.performanceActions')}
            columns={[
              { id: 'employee', header: t('performancePage.employee'), render: row => <SemanticLink href={`/organization/employees/${row.employeeId}`}><Text variant="bodyStrong">{row.employeeId}</Text></SemanticLink> },
              { id: 'action', header: t('common.action'), render: row => <StatusChip label={domainLabel(row.actionType)} tone={actionTone(row.actionType)} /> },
              { id: 'reason', header: t('common.reason'), render: row => <Text sx={{ minWidth: 220, whiteSpace: 'pre-wrap' }}>{row.reason}</Text> },
              { id: 'approval', header: t('performancePage.bossApprovalID'), render: row => row.approvalId ? <Text variant="mono">{row.approvalId}</Text> : t('common.notAvailable') },
              { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Completed' ? 'success' : 'info'} /> },
              { id: 'appeal', header: t('common.appeal'), render: row => <StatusChip label={domainLabel(row.appealStatus)} tone={appealTone(row.appealStatus)} /> },
              { id: 'actions', header: t('common.actions'), render: row => <Stack direction="row" spacing={1} wrap>
                <Button size="small" tone="neutral" onClick={() => appeal.mutate({ id: row.id, reason: 'Employee requests independent review' })} disabled={row.appealStatus !== 'None' && row.appealStatus !== 'Rejected'}>{t('common.appeal')}</Button>
                {row.appealStatus === 'Open' ? <Button size="small" tone="secondary" onClick={() => resolveAppeal.mutate({ id: row.id, upheld: true })} disabled={resolveAppeal.isPending}>{t('performancePage.uphold')}</Button> : null}
                {row.appealStatus === 'Open' ? <Button size="small" tone="danger" onClick={() => setAppealToReverse({ id: row.id, employeeId: row.employeeId })} disabled={resolveAppeal.isPending}>{t('performancePage.reverse')}</Button> : null}
              </Stack> }
            ]}
          />
        </Card>
        <LocalizedMutationFeedback mutation={appeal} successMessage={t('performancePage.performanceAppealOpened')} />
        <LocalizedMutationFeedback mutation={resolveAppeal} successMessage={t('performancePage.performanceAppealResolved')} />

        <SectionHeader title={t('performancePage.employeeImprovementProposal')} action={<StatusChip label={format.number(pendingImprovements)} tone="warning" />} />
        <Card padding="none">
          <LocalizedDataTable
            rows={improvementRows}
            getRowId={row => row.id}
            density="compact"
            minWidth={1040}
            ariaLabel={t('performancePage.employeeImprovementProposal')}
            columns={[
              { id: 'employee', header: t('performancePage.employee'), render: row => <SemanticLink href={`/organization/employees/${row.employeeId}`}><Text variant="bodyStrong">{row.employeeId}</Text></SemanticLink> },
              { id: 'category', header: t('common.category'), render: row => domainLabel(row.category) },
              { id: 'summary', header: t('common.summary'), render: row => <Text sx={{ minWidth: 240, whiteSpace: 'pre-wrap' }}>{row.summary}</Text> },
              { id: 'benefit', header: t('performancePage.expectedBenefit'), render: row => <Text sx={{ minWidth: 200, whiteSpace: 'pre-wrap' }}>{row.expectedBenefit}</Text> },
              { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={improvementTone(row.status)} /> },
              { id: 'actions', header: t('common.actions'), render: row => row.status === 'Proposed' ? <Stack direction="row" spacing={1}>
                <Button size="small" tone="secondary" onClick={() => reviewImprovement.mutate({ id: row.id, approved: true })} disabled={reviewImprovement.isPending}>{t('common.approve')}</Button>
                <Button size="small" tone="danger" onClick={() => reviewImprovement.mutate({ id: row.id, approved: false })} disabled={reviewImprovement.isPending}>{t('common.reject')}</Button>
              </Stack> : t('common.notAvailable') }
            ]}
          />
        </Card>
        <LocalizedMutationFeedback mutation={reviewImprovement} successMessage={t('performancePage.improvementReviewed')} />

        <ConfirmDialog
          open={terminationConfirmOpen}
          title={t('performancePage.executeAction')}
          description={<Text component="span" sx={{ whiteSpace: 'pre-line' }}>{t('performancePage.confirmTerminate', { employeeId: employeeId.trim(), approvalId: approvalId.trim(), reason: reason.trim() })}</Text>}
          confirmLabel={t('performancePage.executeAction')}
          cancelLabel={t('common.no')}
          tone="danger"
          loading={perform.isPending}
          requireText={employeeId.trim()}
          onConfirm={() => { setTerminationConfirmOpen(false); perform.mutate(); }}
          onClose={() => { if (!perform.isPending) setTerminationConfirmOpen(false); }}
        />
        <ConfirmDialog
          open={appealToReverse !== null}
          title={t('performancePage.reverse')}
          description={appealToReverse ? `${t('common.appeal')} · ${appealToReverse.employeeId}` : undefined}
          confirmLabel={t('performancePage.reverse')}
          cancelLabel={t('common.no')}
          tone="danger"
          loading={resolveAppeal.isPending}
          onConfirm={() => { if (appealToReverse) resolveAppeal.mutate({ id: appealToReverse.id, upheld: false }); setAppealToReverse(null); }}
          onClose={() => { if (!resolveAppeal.isPending) setAppealToReverse(null); }}
        />
      </Stack>
    </ContentContainer>
  );
}
