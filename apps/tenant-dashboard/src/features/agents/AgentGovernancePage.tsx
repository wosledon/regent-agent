import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, ContentContainer, DetailList, FormField, Grid, MetricCard, PageHeader, Select, Stack, StatusChip, Text, TextInput, createUuidV4, type ChipTone } from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useAuth } from '../../app/useAuth';
import { localizedNumberRangeError, localizedRequiredTextError } from '../../lib/localizedValidation';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { toFiniteNumber } from '../../lib/formatting';

interface DisclosureRequest { context: string }

function agentStatusTone(status?: string): ChipTone {
  if (status === 'Active' || status === 'Approved' || status === 'Published') return 'success';
  if (status === 'Open' || status === 'Draft') return 'warning';
  if (status === 'Rejected' || status === 'Blocked') return 'error';
  return 'info';
}

export function AgentGovernancePage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const queryClient = useQueryClient();
  const { session } = useAuth();
  const callerCapabilities = session?.capabilities ?? [];
  const callerAuthority = JSON.stringify({
    role: session?.role ?? 'unknown', authMethod: session?.authMethod ?? 'unknown',
    sessionKind: session?.sessionKind ?? 'unknown', capabilities: callerCapabilities
  });
  const restrictedActivated = session?.authMethod === 'Password' || session?.authMethod === 'Development'
    || session?.role === 'Owner' || session?.role === 'CoOwner'
    || callerCapabilities.includes('tools.restricted.activate');
  const governance = useQuery({ queryKey: ['task-governance'], queryFn: api.taskGovernance });
  const evolution = useQuery({ queryKey: ['agent-evolution'], queryFn: api.agentEvolution });
  const [taskType, setTaskType] = useState('engineering.implement');
  const [capabilities, setCapabilities] = useState('engineering.implement,test.execute');
  const [employeeId, setEmployeeId] = useState('');
  const [dimension, setDimension] = useState('engineering');
  const [modelScore, setModelScore] = useState('');
  const [historyScore, setHistoryScore] = useState('');
  const [toolFit, setToolFit] = useState('');
  const [contextFit, setContextFit] = useState('');
  const [confidence, setConfidence] = useState('');
  const [promptName, setPromptName] = useState('review');
  const [promptContent, setPromptContent] = useState(() => t('agentsPage.defaultPromptContent'));
  const [promptKind, setPromptKind] = useState('Operations');
  const [onboardingEmployeeId, setOnboardingEmployeeId] = useState('');
  const [onboardingPromptId, setOnboardingPromptId] = useState('');
  const [toolName, setToolName] = useState('research.search');
  const [level, setLevel] = useState('Primary');
  const [disclosureCapabilities, setDisclosureCapabilities] = useState('research.execute');

  const [disclosed, setDisclosed] = useState<{ request: DisclosureRequest; result: Awaited<ReturnType<typeof api.discloseTools>> } | null>(null);
  const [reviewSubject, setReviewSubject] = useState('');
  const [reviewAuthor, setReviewAuthor] = useState(session?.displayName ?? '');
  const [reviewApprover, setReviewApprover] = useState(session?.displayName ?? '');
  const [debateQuestion, setDebateQuestion] = useState('');
  const [debateDeadline, setDebateDeadline] = useState('');
  const [reassignTaskId, setReassignTaskId] = useState('');
  const [reassignEmployeeId, setReassignEmployeeId] = useState('');
  const [reassignApprovalId, setReassignApprovalId] = useState('');
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['task-governance'] }),
    queryClient.invalidateQueries({ queryKey: ['agent-evolution'] })
  ]);
  const registerType = useMutation({ mutationFn: () => api.registerTaskType({ taskType, requiredCapabilities: capabilities.split(',').map(x => x.trim()).filter(Boolean), toolProfile: 'Primary', doneTemplate: '{}', version: 1 }), onSuccess: invalidate });
  const assess = useMutation({
    mutationFn: () => api.assessCapability({
      employeeId, dimension, modelScore: Number(modelScore), historyScore: Number(historyScore),
      toolFit: Number(toolFit), contextFit: Number(contextFit), confidence: Number(confidence)
    }), onSuccess: invalidate
  });
  const createPrompt = useMutation({ mutationFn: () => api.createPrompt(promptName, promptContent, promptKind), onSuccess: async () => { setPromptContent(''); await invalidate(); } });
  const activatePrompt = useMutation({ mutationFn: api.activatePrompt, onSuccess: invalidate });
  const activateSkill = useMutation({ mutationFn: api.activateSkill, onSuccess: invalidate });
  const publishAsset = useMutation({ mutationFn: ({ type, id }: { type: 'Prompt' | 'Skill'; id: string }) => api.publishAgentAsset(type, id), onSuccess: invalidate });
  const onboarding = useMutation({ mutationFn: () => api.createOnboardingBaseline(onboardingEmployeeId, onboardingPromptId), onSuccess: invalidate });
  const setPolicy = useMutation({ mutationFn: () => api.setToolPolicy({ toolName, level, requiredCapabilities: disclosureCapabilities.split(',').map(x => x.trim()).filter(Boolean), summary: 'Local governed tool' }), onSuccess: invalidate });
  const disclosureContext = useMemo(() => JSON.stringify([
    toolName, level, disclosureCapabilities, evolution.data?.toolPolicies,
    callerAuthority, restrictedActivated
  ]), [toolName, level, disclosureCapabilities, evolution.data?.toolPolicies,
    callerAuthority, restrictedActivated]);
  const disclosureRequest = useMemo<DisclosureRequest>(() => ({ context: disclosureContext }), [disclosureContext]);
  const disclose = useMutation({
    mutationFn: async (request: DisclosureRequest) => ({ request, result: await api.discloseTools() }),
    onSuccess: setDisclosed
  });
  const createReview = useMutation({ mutationFn: () => api.createReviewTask({ id: createUuidV4(), subjectId: reviewSubject.trim(), author: reviewAuthor.trim(), requiredQuorum: 2, findings: [], approvers: [], status: 'Open' }), onSuccess: invalidate });
  const approveReview = useMutation({ mutationFn: ({ id, approver }: { id: string; approver: string }) => api.approveReviewTask(id, approver), onSuccess: invalidate });
  const createDebate = useMutation({
    mutationFn: () => api.createDebate({ id: createUuidV4(), question: debateQuestion.trim(), options: ['Approve', 'Reject'], deadline: new Date(debateDeadline).toISOString(), decision: null, status: 'Open' }),
    onSuccess: async () => { setDebateQuestion(''); setDebateDeadline(''); await invalidate(); }
  });
  const reassign = useMutation({ mutationFn: () => api.reassignTask(reassignTaskId, reassignEmployeeId, reassignApprovalId), onSuccess: invalidate });
  if (governance.isLoading || evolution.isLoading) return <LocalizedLoadingState />;
  const error = governance.error ?? evolution.error;
  if (error) return <LocalizedErrorState error={error} />;
  const state = governance.data;
  const agentState = evolution.data;
  const assessmentError = localizedRequiredTextError(employeeId, t('common.employeeID'), t)
    ?? localizedRequiredTextError(dimension, t('agentsPage.dimension'), t)
    ?? localizedNumberRangeError(modelScore, t('agentsPage.modelScore01'), 0, 1, t)
    ?? localizedNumberRangeError(historyScore, t('agentsPage.historyScore01'), 0, 1, t)
    ?? localizedNumberRangeError(toolFit, t('agentsPage.toolFit01'), 0, 1, t)
    ?? localizedNumberRangeError(contextFit, t('agentsPage.contextFit01'), 0, 1, t)
    ?? localizedNumberRangeError(confidence, t('agentsPage.confidence01'), 0, 1, t);
  const debateTimestamp = debateDeadline ? new Date(debateDeadline).getTime() : Number.NaN;
  const debateError = localizedRequiredTextError(debateQuestion, t('agentsPage.debateQuestion'), t)
    ?? (!Number.isFinite(debateTimestamp) ? t('agentsPage.debateDeadlineInvalid') : null);
  return <ContentContainer>
    <Stack spacing={3}>
      <PageHeader title={t('agentsPage.agentTaskGovernance')} description={t('agentsPage.capabilityScoringWorkLedgerReviewsDebatesPromptsSkillsAndToolDisclosure')} />
      <Grid columns={4}>
        <MetricCard label={t('agentsPage.workLedger')} value={state?.ledger?.length ?? 0} icon="agentGovernance" />
        <MetricCard label={t('agentsPage.reviewsAndDebates')} value={(state?.reviews?.length ?? 0) + (state?.debates?.length ?? 0)} icon="approval" tone={(state?.reviews?.length ?? 0) > 0 ? 'warning' : 'default'} />
        <MetricCard label={t('agentsPage.promptAndSkillEvolution')} value={(agentState?.prompts?.length ?? 0) + (agentState?.skills?.length ?? 0)} icon="knowledge" />
        <MetricCard label={t('agentsPage.toolDisclosure')} value={agentState?.toolPolicies?.length ?? 0} icon="governance" />
      </Grid>
      <Card padding="small" title={t('agentsPage.callerAuthorityReadOnly')} action={<StatusChip label={restrictedActivated ? t('agentsPage.active') : t('agentsPage.notActive')} tone={restrictedActivated ? 'success' : 'neutral'} />}>
        <DetailList dense columns={2} items={[
          { label: t('common.role'), value: t('agentsPage.roleAuthenticationSession', { value0: domainLabel(session?.role ?? 'unknown'), value1: domainLabel(session?.authMethod ?? 'unknown'), value2: domainLabel(session?.sessionKind ?? 'unknown') }) },
          { label: t('agentsPage.capabilities'), value: callerCapabilities.join(', ') || t('agentsPage.noExplicitCapabilities') },
          { label: t('agentsPage.restrictedActivation'), value: restrictedActivated ? t('agentsPage.active') : t('agentsPage.notActive') }
        ]} />
        <Text variant="caption">{t('agentsPage.thisIsSessionDerivedCallerAuthorityItDoesNotGrantCapabilitiesRequiredByA')}</Text>
      </Card>
      <Grid columns={2}>
      <Card padding="small" title={t('agentsPage.taskTypeAndCapability')}>
        <FormField label={t('agentsPage.taskType')}><TextInput value={taskType} onChange={setTaskType} /></FormField>
        <FormField label={t('agentsPage.requiredCapabilities')}><TextInput value={capabilities} onChange={setCapabilities} /></FormField>
        <Button onClick={() => registerType.mutate()} loading={registerType.isPending}>{t('agentsPage.registerTaskType')}</Button>
        <FormField label={t('common.employeeID')}><TextInput value={employeeId} onChange={setEmployeeId} /></FormField>
        <FormField label={t('agentsPage.dimension')}><TextInput value={dimension} onChange={setDimension} /></FormField>
        <FormField label={t('agentsPage.modelScore01')}><TextInput value={modelScore} onChange={setModelScore} inputProps={{ inputMode: 'decimal' }} /></FormField>
        <FormField label={t('agentsPage.historyScore01')}><TextInput value={historyScore} onChange={setHistoryScore} inputProps={{ inputMode: 'decimal' }} /></FormField>
        <FormField label={t('agentsPage.toolFit01')}><TextInput value={toolFit} onChange={setToolFit} inputProps={{ inputMode: 'decimal' }} /></FormField>
        <FormField label={t('agentsPage.contextFit01')}><TextInput value={contextFit} onChange={setContextFit} inputProps={{ inputMode: 'decimal' }} /></FormField>
        <FormField label={t('agentsPage.confidence01')}><TextInput value={confidence} onChange={setConfidence} inputProps={{ inputMode: 'decimal' }} /></FormField>
        <Button tone="secondary" onClick={() => assess.mutate()} disabled={!!assessmentError || assess.isPending} loading={assess.isPending}>{t('agentsPage.assessCapability')}</Button>
        {assessmentError ? <Text color="error">{assessmentError}</Text> : null}
        <LocalizedMutationFeedback mutation={registerType} successMessage={t('agentsPage.taskTypeRegistered')} />
        <LocalizedMutationFeedback mutation={assess} successMessage={t('agentsPage.capabilityAssessmentRecorded')} />
        <LocalizedDataTable density="compact" rows={state?.capabilities ?? []} getRowId={row => `${row.employeeId}:${row.dimension}`} columns={[
          { id: 'employee', header: t('agentsPage.employee'), render: row => row.employeeId ?? '' },
          { id: 'dimension', header: t('agentsPage.dimension'), render: row => row.dimension ?? '' },
          { id: 'score', header: t('agentsPage.effective'), render: row => format.number(toFiniteNumber(row.effectiveScore)) }
        ]} />
      </Card>
      <Card padding="small" title={t('agentsPage.promptAndSkillEvolution')}>
        <FormField label={t('agentsPage.promptName')}><TextInput value={promptName} onChange={setPromptName} /></FormField>
        <FormField label={t('agentsPage.promptContent')}><TextInput value={promptContent} onChange={setPromptContent} multiline minRows={2} /></FormField>
        <Select label={t('agentsPage.promptKind')} value={promptKind} onChange={setPromptKind} options={['Recruitment', 'Onboarding', 'Operations'].map(value => ({ value, label: domainLabel(value) }))} />
        <Button onClick={() => createPrompt.mutate()} disabled={!promptName.trim() || !promptContent.trim() || createPrompt.isPending} loading={createPrompt.isPending}>{t('agentsPage.createVersion')}</Button>
        <LocalizedMutationFeedback mutation={createPrompt} successMessage={t('agentsPage.promptVersionCreated')} />
        <LocalizedDataTable density="compact" rows={agentState?.prompts ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'name', header: t('agentsPage.prompt'), render: row => row.name ?? '' },
          { id: 'version', header: t('common.version'), render: row => format.number(toFiniteNumber(row.version)) },
          { id: 'kind', header: t('agentsPage.kind'), render: row => domainLabel(row.kind ?? 'Operations') },
          { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={agentStatusTone(row.status)} /> },
          { id: 'actions', header: t('common.actions'), render: row => <Stack direction="row" wrap>
            <Button tone="neutral" size="small" onClick={() => activatePrompt.mutate(row.id ?? '')} disabled={row.status === 'Active' || activatePrompt.isPending || publishAsset.isPending} loading={activatePrompt.isPending && activatePrompt.variables === row.id}>{t('common.activate')}</Button>
            <Button tone="secondary" size="small" onClick={() => publishAsset.mutate({ type: 'Prompt', id: row.id ?? '' })} disabled={row.status !== 'Active' || activatePrompt.isPending || publishAsset.isPending} loading={publishAsset.isPending && publishAsset.variables?.type === 'Prompt' && publishAsset.variables.id === row.id}>{t('common.publish')}</Button>
          </Stack> }
        ]} />
        <LocalizedDataTable density="compact" rows={agentState?.skills ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'name', header: t('agentsPage.skill'), render: row => row.name ?? '' },
          { id: 'version', header: t('common.version'), render: row => format.number(toFiniteNumber(row.version)) },
          { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={agentStatusTone(row.status)} /> },
          { id: 'actions', header: t('common.actions'), render: row => <Stack direction="row" wrap>
            <Button tone="neutral" size="small" onClick={() => activateSkill.mutate(row.id ?? '')} disabled={row.status === 'Active' || activateSkill.isPending || publishAsset.isPending} loading={activateSkill.isPending && activateSkill.variables === row.id}>{t('common.activate')}</Button>
            <Button tone="secondary" size="small" onClick={() => publishAsset.mutate({ type: 'Skill', id: row.id ?? '' })} disabled={row.status !== 'Active' || activateSkill.isPending || publishAsset.isPending} loading={publishAsset.isPending && publishAsset.variables?.type === 'Skill' && publishAsset.variables.id === row.id}>{t('common.publish')}</Button>
          </Stack> }
        ]} />
        <LocalizedMutationFeedback mutation={activatePrompt} successMessage={t('agentsPage.promptActivated')} />
        <LocalizedMutationFeedback mutation={activateSkill} successMessage={t('agentsPage.skillActivated')} />
        <LocalizedMutationFeedback mutation={publishAsset} successMessage={t('agentsPage.assetPublished')} />
      </Card>
    </Grid>
    <Grid columns={2}>
      <Card padding="small" title={t('agentsPage.toolDisclosure')}>
        <FormField label={t('agentsPage.toolName')}><TextInput value={toolName} onChange={setToolName} /></FormField>
        <Select label={t('agentsPage.level')} value={level} onChange={setLevel} options={['Primary', 'Auxiliary', 'Restricted', 'Forbidden'].map(value => ({ value, label: domainLabel(value) }))} />
        <FormField label={t('agentsPage.policyRequiredCapabilities')}><TextInput value={disclosureCapabilities} onChange={setDisclosureCapabilities} /></FormField>
        <Button onClick={() => setPolicy.mutate()} disabled={!toolName.trim() || setPolicy.isPending} loading={setPolicy.isPending}>{t('agentsPage.savePolicy')}</Button>
        <Text variant="caption">{t('agentsPage.policyRequiredCapabilitiesAreEvaluatedSeparatelyFromTheCallerAuthoritySh')}</Text>
        <Button tone="neutral" onClick={() => disclose.mutate(disclosureRequest)} disabled={!toolName.trim() || disclose.isPending} loading={disclose.isPending}>{t('agentsPage.disclose')}</Button>
        <LocalizedMutationFeedback mutation={setPolicy} successMessage={t('agentsPage.toolPolicySaved')} />
        {disclose.isSuccess && disclosed?.request === disclosureRequest ? <StatusChip label={t('agentsPage.toolDisclosureEvaluated')} tone="success" /> : null}
        {disclose.error && disclose.variables === disclosureRequest ? <LocalizedMutationFeedback mutation={disclose} showSuccess={false} /> : null}
        {disclosed?.request === disclosureRequest ? disclosed.result.tools.map(tool => <Text key={tool.name}>{t('agentsPage.toolLevelName', { level: domainLabel(tool.level), name: tool.name })}</Text>) : null}
      </Card>
      <Card padding="small" title={t('agentsPage.reviewsAndDebates')}>
        <FormField label={t('agentsPage.reviewSubjectID')}><TextInput value={reviewSubject} onChange={setReviewSubject} /></FormField>
        <FormField label={t('agentsPage.reviewAuthor')}><TextInput value={reviewAuthor} onChange={setReviewAuthor} /></FormField>
        <Button onClick={() => createReview.mutate()} disabled={!reviewSubject.trim() || !reviewAuthor.trim() || createReview.isPending} loading={createReview.isPending}>{t('agentsPage.createReview')}</Button>
        <LocalizedMutationFeedback mutation={createReview} successMessage={t('agentsPage.reviewTaskCreated')} />
        <FormField label={t('agentsPage.reviewApprover')}><TextInput value={reviewApprover} onChange={setReviewApprover} /></FormField>
        <LocalizedDataTable density="compact" rows={state?.reviews ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'subject', header: t('agentsPage.subject'), render: row => row.subjectId ?? '' },
          { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={agentStatusTone(row.status)} /> },
          { id: 'actions', header: t('common.actions'), render: row => <Button tone="neutral" size="small" onClick={() => approveReview.mutate({ id: row.id ?? '', approver: reviewApprover.trim() })} disabled={row.status === 'Approved' || !reviewApprover.trim() || approveReview.isPending} loading={approveReview.isPending && approveReview.variables?.id === row.id}>{t('common.approve')}</Button> }
        ]} />
        <LocalizedMutationFeedback mutation={approveReview} successMessage={t('agentsPage.reviewApprovalRecorded')} />
        <FormField label={t('agentsPage.debateQuestion')}><TextInput value={debateQuestion} onChange={setDebateQuestion} /></FormField>
        <FormField label={t('agentsPage.debateDeadline')}><TextInput value={debateDeadline} onChange={setDebateDeadline} type="datetime-local" /></FormField>
        <Button tone="secondary" onClick={() => createDebate.mutate()} disabled={!!debateError || createDebate.isPending} loading={createDebate.isPending}>{t('agentsPage.openDebate')}</Button>
        {debateError ? <Text color="error">{debateError}</Text> : null}
        <LocalizedMutationFeedback mutation={createDebate} successMessage={t('agentsPage.debateOpened')} />
        <LocalizedDataTable density="compact" rows={state?.debates ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'question', header: t('agentsPage.question'), render: row => row.question ?? '' },
          { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={agentStatusTone(row.status)} /> }
        ]} />
      </Card>
    </Grid>
    <Card padding="small" title={t('agentsPage.onboardingBaselineAndPublicAssets')}>
      <Stack spacing={2}>
        <FormField label={t('common.employeeID')}><TextInput value={onboardingEmployeeId} onChange={setOnboardingEmployeeId} /></FormField>
        <FormField label={t('agentsPage.activePromptBundleID')}><TextInput value={onboardingPromptId} onChange={setOnboardingPromptId} /></FormField>
        <Button tone="secondary" disabled={!onboardingEmployeeId.trim() || !onboardingPromptId.trim() || onboarding.isPending} loading={onboarding.isPending} onClick={() => onboarding.mutate()}>{t('agentsPage.createOnboardingBaseline')}</Button>
        <LocalizedMutationFeedback mutation={onboarding} successMessage={t('agentsPage.onboardingBaselineCreated')} />
        <LocalizedDataTable density="compact" rows={agentState?.onboardingBaselines ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'employee', header: t('agentsPage.employee'), render: row => row.employeeId ?? '' },
          { id: 'prompt', header: t('agentsPage.prompt'), render: row => row.promptBundleId ?? '' },
          { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={agentStatusTone(row.status)} /> }
        ]} />
        <LocalizedDataTable density="compact" rows={agentState?.publicAssets ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'asset', header: t('agentsPage.asset'), render: row => `${row.assetType}:${row.assetId}` },
          { id: 'version', header: t('common.version'), render: row => format.number(toFiniteNumber(row.version)) },
          { id: 'publisher', header: t('agentsPage.publisher'), render: row => row.publishedBy ?? '' }
        ]} />
      </Stack>
    </Card>
    <Card padding="small" title={t('agentsPage.approvedTaskReassignment')}>
      <Stack spacing={2}>
        <FormField label={t('agentsPage.taskID')}><TextInput value={reassignTaskId} onChange={setReassignTaskId} /></FormField>
        <FormField label={t('agentsPage.newEmployeeID')}><TextInput value={reassignEmployeeId} onChange={setReassignEmployeeId} /></FormField>
        <FormField label={t('agentsPage.approvalID')}><TextInput value={reassignApprovalId} onChange={setReassignApprovalId} /></FormField>
        <Button tone="secondary" disabled={!reassignTaskId.trim() || !reassignEmployeeId.trim() || !reassignApprovalId.trim() || reassign.isPending} loading={reassign.isPending} onClick={() => reassign.mutate()}>{t('agentsPage.reassignWithApproval')}</Button>
        <LocalizedMutationFeedback mutation={reassign} successMessage={t('agentsPage.taskReassignedWithApproval')} />
      </Stack>
    </Card>
    <Card padding="small" title={t('agentsPage.workLedger')}>
      <LocalizedDataTable density="compact" rows={state?.ledger ?? []} getRowId={row => row.id ?? ''} columns={[
        { id: 'employee', header: t('agentsPage.employee'), render: row => row.employeeId ?? '' },
        { id: 'task', header: t('common.task'), render: row => row.taskId ?? '' },
        { id: 'type', header: t('agentsPage.type'), render: row => row.taskType ?? '' },
        { id: 'score', header: t('agentsPage.weightedScore'), render: row => format.number(toFiniteNumber(row.weightedScore)) }
      ]} />
      </Card>
    </Stack>
  </ContentContainer>;
}
