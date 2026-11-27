import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ContentContainer, DetailList, FormField, FreshnessBadge, Grid,
  Icon, MetricCard, PageHeader, SectionHeader, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import type { WorkflowRun } from '../../lib/domain-types';
import { toFiniteNumber } from '../../lib/formatting';

export function WorkflowsPage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const queryClient = useQueryClient();
  const [run, setRun] = useState<WorkflowRun | null>(null);
  const [name, setName] = useState('');
  const [steps, setSteps] = useState('');
  const [scopePositionId, setScopePositionId] = useState('');
  const [decision, setDecision] = useState('Adopt');
  const [reviewer, setReviewer] = useState('Leader');
  const [note, setNote] = useState(() => t('workflowsPage.defaultReviewNote'));
  const { data, isLoading, error } = useQuery({ queryKey: ['workflows'], queryFn: api.workflows });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['workflows'] });
  const capture = useMutation({
    mutationFn: () => api.createWorkflow(name, steps.split('\n').map(value => value.trim()).filter(Boolean), scopePositionId),
    onSuccess: async () => { setName(''); setSteps(''); setScopePositionId(''); await invalidate(); }
  });
  const nominate = useMutation({ mutationFn: api.nominateWorkflow, onSuccess: invalidate });
  const review = useMutation({
    mutationFn: (id: string) => api.reviewWorkflow(id, decision, reviewer, note), onSuccess: invalidate
  });
  const start = useMutation({ mutationFn: api.startWorkflow, onSuccess: setRun });
  const advance = useMutation({ mutationFn: api.advanceWorkflow, onSuccess: setRun });

  if (isLoading) return <LocalizedLoadingState />;
  if (error) return <LocalizedErrorState error={error} />;

  const workflows = data ?? [];
  const personalCount = workflows.filter(workflow => workflow.status === 'Personal').length;
  const nominatedCount = workflows.filter(workflow => workflow.status === 'Nominated').length;
  const runnableCount = workflows.filter(workflow => ['Active', 'Adopted', 'Pilot'].includes(workflow.status)).length;
  const runWorkflow = run ? workflows.find(workflow => workflow.id === run.workflowId) : undefined;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('workflowsPage.workflows')}
          description={t('workflowsPage.capturePersonalPracticeNominateItUpwardReviewImmutablyAndRunApprovedVers')}
          meta={<FreshnessBadge freshness="unknown" label={t('common.freshness.unknown')} />}
        />
        <Grid columns={4}>
          <MetricCard label={t('common.domain.personal')} value={format.number(personalCount)} icon="workflow" />
          <MetricCard label={t('common.domain.nominated')} value={format.number(nominatedCount)} tone={nominatedCount > 0 ? 'warning' : 'default'} icon="approval" />
          <MetricCard label={t('common.domain.active')} value={format.number(runnableCount)} tone="success" icon="workflow" />
          <MetricCard label={t('workflowsPage.currentRun')} value={run ? domainLabel(run.status) : t('common.notAvailable')} tone={run ? 'info' : 'default'} icon="activity" />
        </Grid>
        <Grid columns={2}>
          <Card title={t('workflowsPage.capturePersonalWorkflow')} headingLevel="h2" padding="small">
            <Stack spacing={2}>
              <FormField label={t('common.name')}><TextInput value={name} onChange={setName} placeholder={t('workflowsPage.releaseChecklist')} /></FormField>
              <FormField label={t('workflowsPage.stepsOnePerLine')}><TextInput multiline minRows={4} value={steps} onChange={setSteps} placeholder={t('workflowsPage.stepsPlaceholder')} /></FormField>
              <FormField label={t('workflowsPage.scopePositionID')}><TextInput value={scopePositionId} onChange={setScopePositionId} /></FormField>
              <Stack direction="row" spacing={1.5} wrap alignItems="center">
                <Button size="small" disabled={!name || !steps || !scopePositionId} loading={capture.isPending} onClick={() => capture.mutate()}>{t('workflowsPage.captureWorkflow')}</Button>
                <LocalizedMutationFeedback mutation={capture} successMessage={t('workflowsPage.workflowCaptured')} />
              </Stack>
            </Stack>
          </Card>
          <Card title={t('workflowsPage.reviewNomination')} headingLevel="h2" padding="small">
            <Stack spacing={2}>
              <Select label={t('common.decision')} value={decision} onChange={setDecision} options={['Adopt', 'Pilot', 'Reject'].map(value => ({ value, label: domainLabel(value) }))} />
              <FormField label={t('workflowsPage.reviewer')}><TextInput value={reviewer} onChange={setReviewer} /></FormField>
              <FormField label={t('workflowsPage.reviewNote')}><TextInput value={note} onChange={setNote} /></FormField>
              <Text variant="caption">{t('workflowsPage.useTheReviewActionOnANominatedVersionAdoptedAndPilotVersionsAreImmutable')}</Text>
            </Stack>
          </Card>
        </Grid>
        <LocalizedMutationFeedback mutation={nominate} successMessage={t('workflowsPage.workflowNominated')} />
        <LocalizedMutationFeedback mutation={review} successMessage={t('workflowsPage.workflowReviewRecorded')} />
        <LocalizedMutationFeedback mutation={start} showSuccess={false} />
        <Stack spacing={2}>
          <SectionHeader title={t('workflowsPage.workflows')} action={<StatusChip label={format.number(workflows.length)} tone={nominatedCount > 0 ? 'warning' : 'neutral'} />} />
          <Card padding="small">
            <LocalizedDataTable rows={workflows} getRowId={workflow => workflow.id} density="compact" minWidth={980} emptyTitle={t('workflowsPage.noWorkflows')} columns={[
              { id: 'workflow', header: t('common.name'), render: workflow => <Stack spacing={0.4}>
                <Text variant="bodyStrong">{t('workflowsPage.workflowTitle', { name: workflow.name, version: format.number(toFiniteNumber(workflow.version)) })}</Text>
                <StatusChip label={domainLabel(workflow.status)} tone={workflow.status === 'Rejected' ? 'error' : workflow.status === 'Personal' ? 'info' : 'success'} />
              </Stack> },
              { id: 'steps', header: t('workflowsPage.stepsOnePerLine'), render: workflow => workflow.steps.join(' → ') },
              { id: 'provenance', header: t('common.evidence'), render: workflow => <Stack spacing={0.25}>
                <Text variant="caption">{t('workflowsPage.authorScope', { value0: workflow.author ?? t('workflowsPage.system'), value1: workflow.scopePositionId ?? t('workflowsPage.company') })}</Text>
                <Text variant="caption">{t('common.created')}: {workflow.createdAt ? format.dateTime(workflow.createdAt) : t('common.notAvailable')}</Text>
              </Stack> },
              { id: 'review', header: t('common.review'), render: workflow => <Stack spacing={0.25}>
                <Text variant="caption">{workflow.reviewedBy ? t('workflowsPage.reviewedBy', { value0: workflow.reviewedBy }) : t('common.notAvailable')}</Text>
                <Text variant="secondary">{workflow.reviewNote ?? t('common.notAvailable')}</Text>
                <Text variant="caption">{workflow.reviewedAt ? format.dateTime(workflow.reviewedAt) : t('common.notAvailable')}</Text>
              </Stack> },
              { id: 'actions', header: t('common.actions'), render: workflow => {
                const canRun = ['Active', 'Adopted', 'Pilot'].includes(workflow.status);
                const nextAction = workflow.status === 'Personal' ? t('workflowsPage.nominate') : workflow.status === 'Nominated' ? t('common.review') : canRun ? t('workflowsPage.startRun') : domainLabel(workflow.status);
                return <Stack spacing={0.5}>
                  <Text variant="caption">{t('common.nextAction', { action: nextAction })}</Text>
                  <Stack direction="row" wrap spacing={0.5}>
                    {workflow.status === 'Personal' ? <Button tone="secondary" size="small" onClick={() => nominate.mutate(workflow.id)} loading={nominate.isPending && nominate.variables === workflow.id}>{t('workflowsPage.nominate')}</Button> : null}
                    {workflow.status === 'Nominated' ? <Button size="small" onClick={() => review.mutate(workflow.id)} loading={review.isPending && review.variables === workflow.id}>{t('common.review')}</Button> : null}
                    {canRun ? <Button tone="neutral" size="small" onClick={() => start.mutate(workflow.id)} loading={start.isPending && start.variables === workflow.id}>{t('workflowsPage.startRun')}</Button> : null}
                  </Stack>
                </Stack>;
              } }
            ]} />
          </Card>
        </Stack>
        {run ? (
          <Stack spacing={2}>
            <SectionHeader title={t('workflowsPage.currentRun')} action={<StatusChip label={domainLabel(run.status)} tone="info" />} />
            <Card padding="small">
              <Stack spacing={2}>
                <DetailList dense columns={2} items={[
                  { label: t('common.status'), value: domainLabel(run.status) },
                  { label: t('common.next'), value: format.number(run.currentStep + 1) },
                  { label: t('common.name'), value: runWorkflow?.name ?? t('common.notAvailable') },
                  { label: t('common.version'), value: runWorkflow ? format.number(toFiniteNumber(runWorkflow.version)) : t('common.notAvailable') }
                ]} />
                {runWorkflow ? <Stack spacing={1.25}>
                  {runWorkflow.steps.map((step, index) => {
                    const stepStatus = index < run.currentStep ? 'Completed' : index === run.currentStep ? 'Running' : 'Pending';
                    return <Stack key={`${step}-${index}`} direction="row" spacing={1.5} alignItems="flex-start">
                      <Icon name={stepStatus === 'Completed' ? 'check' : stepStatus === 'Running' ? 'clock' : 'workflow'} sx={{ mt: 0.25 }} />
                      <Stack sx={{ flex: 1, minWidth: 0 }}>
                        <Text variant="caption">{format.number(index + 1)}</Text>
                        <Text variant="bodyStrong">{step}</Text>
                      </Stack>
                      <StatusChip label={domainLabel(stepStatus)} tone={stepStatus === 'Completed' ? 'success' : stepStatus === 'Running' ? 'info' : 'neutral'} />
                    </Stack>;
                  })}
                </Stack> : null}
                <Stack direction="row" spacing={1.5} wrap alignItems="center">
                  <Button size="small" onClick={() => advance.mutate(run.id)} loading={advance.isPending}>{t('workflowsPage.advance')}</Button>
                  <LocalizedMutationFeedback mutation={advance} showSuccess={false} />
                </Stack>
              </Stack>
            </Card>
          </Stack>
        ) : null}
      </Stack>
    </ContentContainer>
  );
}
