import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import {
  Button,
  Card,
  ConfirmDialog,
  ContentContainer,
  DetailList,
  EmptyState,
  FormField,
  PageHeader,
  SectionHeader,
  Select,
  Stack,
  StatusChip,
  TextInput,
  createUuidV4,
  type ChipTone
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { toFiniteNumber } from '../../lib/formatting';
import { localizedCount } from '../../lib/localizedCount';

const deliveryStatusTone: Record<string, ChipTone> = {
  accepted: 'success',
  completed: 'success',
  done: 'success',
  live: 'success',
  resolved: 'success',
  ready: 'info',
  inreview: 'info',
  verifying: 'info',
  draft: 'neutral',
  backlog: 'neutral',
  new: 'warning',
  readyforretest: 'warning',
  reopened: 'error',
  failed: 'error',
  blocked: 'error',
  cancelled: 'error'
};

const severityTone: Record<string, ChipTone> = {
  P0: 'error',
  P1: 'warning',
  P2: 'info',
  P3: 'neutral'
};

function statusTone(status?: string): ChipTone {
  return deliveryStatusTone[status?.toLowerCase() ?? ''] ?? 'info';
}

export function DeliveryPage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const { projectId = '' } = useParams();
  const queryClient = useQueryClient();
  const delivery = useQuery({
    queryKey: ['delivery', projectId],
    queryFn: () => api.delivery(projectId)
  });
  const [claim, setClaim] = useState('');
  const [sourceA, setSourceA] = useState('');
  const [sourceB, setSourceB] = useState('');
  const [keyClaim, setKeyClaim] = useState('false');
  const [title, setTitle] = useState('');
  const [problem, setProblem] = useState('');
  const [scope, setScope] = useState('');
  const [criteria, setCriteria] = useState('');
  const [priority, setPriority] = useState('P1');
  const [briefTitle, setBriefTitle] = useState('');
  const [briefProblem, setBriefProblem] = useState('');
  const [mvpScope, setMvpScope] = useState('');
  const [successMetrics, setSuccessMetrics] = useState('success_rate>=95%');
  const [findingIds, setFindingIds] = useState('');
  const [briefId, setBriefId] = useState('');
  const [decision, setDecision] = useState('Go');
  const [rationale, setRationale] = useState('');
  const [evidenceIds, setEvidenceIds] = useState('');
  const [reround, setReround] = useState('0');
  const [defectTitle, setDefectTitle] = useState('');
  const [defectSteps, setDefectSteps] = useState('');
  const [defectSeverity, setDefectSeverity] = useState('P2');
  const [version, setVersion] = useState('v1.0.0');
  const [artifactRef, setArtifactRef] = useState('artifact:release');
  const [acceptanceRequirement, setAcceptanceRequirement] = useState('');
  const [releaseConfirmOpen, setReleaseConfirmOpen] = useState(false);
  const [terminateConfirmOpen, setTerminateConfirmOpen] = useState(false);

  const [functionalScore, setFunctionalScore] = useState('90');
  const [usabilityScore, setUsabilityScore] = useState('88');
  const [designScore, setDesignScore] = useState('85');
  const [accessibilityScore, setAccessibilityScore] = useState('80');
  const [observedReleaseId, setObservedReleaseId] = useState('');
  const [releaseHealthy, setReleaseHealthy] = useState('true');
  const [metricsJson, setMetricsJson] = useState('{"error_rate":0.01}');
  const [terminationOutcome, setTerminationOutcome] = useState('');
  const [whatWorked, setWhatWorked] = useState('');
  const [whatFailed, setWhatFailed] = useState('');
  const [lessons, setLessons] = useState('');
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['delivery', projectId] });

  const addFinding = useMutation({
    mutationFn: () =>
      api.addFinding(projectId, {
        id: createUuidV4(),
        claim,
        keyClaim: keyClaim === 'true',
        counterEvidence: null,
        sources: [
          { id: sourceA, kind: 'research', location: sourceA },
          ...(sourceB ? [{ id: sourceB, kind: 'research', location: sourceB }] : [])
        ]
      }),
    onSuccess: async () => {
      setClaim('');
      setSourceA('');
      setSourceB('');
      await invalidate();
    }
  });
  const addRequirement = useMutation({
    mutationFn: () =>
      api.createRequirement(projectId, {
        id: createUuidV4(),
        title,
        problem,
        scope,
        priority,
        status: 'Draft',
        acceptanceCriteria: criteria
          .split('\n')
          .map((value) => value.trim())
          .filter(Boolean),
        version: 1,
        totalWeight: 0
      }),
    onSuccess: async () => {
      setTitle('');
      setProblem('');
      setScope('');
      setCriteria('');
      await invalidate();
    }
  });
  const createBrief = useMutation({
    mutationFn: () =>
      api.createBrief(projectId, {
        id: createUuidV4(),
        title: briefTitle,
        problem: briefProblem,
        mvpScope: mvpScope
          .split(',')
          .map((value) => value.trim())
          .filter(Boolean),
        successMetrics: Object.fromEntries(
          successMetrics
            .split('\n')
            .map((line) => line.split('='))
            .filter((parts) => parts.length === 2)
            .map((parts) => [parts[0].trim(), parts[1].trim()])
        ),
        findingIds: findingIds
          .split(',')
          .map((value) => value.trim())
          .filter(Boolean),
        version: 1
      }),
    onSuccess: async () => {
      setBriefTitle('');
      setBriefProblem('');
      setMvpScope('');
      await invalidate();
    }
  });
  const decide = useMutation({
    mutationFn: () =>
      api.decideBrief(
        projectId,
        briefId,
        decision,
        rationale,
        evidenceIds
          .split(',')
          .map((value) => value.trim())
          .filter(Boolean),
        Number(reround)
      ),
    onSuccess: invalidate
  });
  const makeReady = useMutation({
    mutationFn: (requirementId: string) => api.readyRequirement(projectId, requirementId, 6),
    onSuccess: invalidate
  });
  const createPlan = useMutation({
    mutationFn: async (requirementId: string) => {
      const requirement = delivery.data?.requirements?.find((item) => item.id === requirementId);
      if (!requirement) throw new Error(t('deliveryPage.requirementNotFound'));
      const acceptanceCriteria = requirement.acceptanceCriteria ?? [];
      const tasks = Array.from({ length: 3 }, (_, index) => ({
        id: createUuidV4(),
        projectId,
        requirementId,
        title: t('deliveryPage.generatedTaskTitle', {
          requirement: requirement.title ?? t('deliveryPage.requirement'),
          step: format.number(index + 1)
        }),
        taskType: 'engineering.implement',
        requiredCapabilitiesJson: JSON.stringify(['engineering.implement']),
        dependsOn: [],
        weight: 2,
        status: 'Backlog',
        author: '',
        assignee: null,
        validator: null,
        acceptanceCriteria
      }));
      const plan = await api.proposePlan(projectId, {
        id: createUuidV4(),
        requirementId,
        requirementVersion: requirement.version ?? 1,
        tasks,
        validationErrors: [],
        status: 'Draft',
        riskLevel: 'Low',
        approvalId: null,
        approvedBy: null
      });
      return api.approvePlan(projectId, plan.id ?? '');
    },
    onSuccess: invalidate
  });
  const reportDefect = useMutation({
    mutationFn: () =>
      api.reportDefect(projectId, {
        id: createUuidV4(),
        projectId,
        requirementId: delivery.data?.requirements?.[0]?.id || '',
        taskId: null,
        title: defectTitle,
        severity: defectSeverity,
        status: 'New',
        reproductionSteps: defectSteps,
        reportedBy: '',
        fixer: null,
        retester: null,
        evidence: []
      }),
    onSuccess: async () => {
      setDefectTitle('');
      setDefectSteps('');
      await invalidate();
    }
  });
  const release = useMutation({
    mutationFn: () => api.releaseProject(projectId, version, artifactRef),
    onSuccess: invalidate
  });
  const fixDefect = useMutation({
    mutationFn: (id: string) => api.fixDefect(projectId, id),
    onSuccess: invalidate
  });
  const retest = useMutation({
    mutationFn: (id: string) => api.retestDefect(projectId, id, true),
    onSuccess: invalidate
  });
  const acceptance = useMutation({
    mutationFn: () =>
      api.addAcceptanceReport(projectId, {
        id: createUuidV4(),
        requirementId: acceptanceRequirement,
        reviewer: '',
        functionalScore: Number(functionalScore),
        usabilityScore: Number(usabilityScore),
        designScore: Number(designScore),
        accessibilityScore: Number(accessibilityScore),
        evidence: [{ id: 'acceptance-evidence', kind: 'report', location: 'local' }]
      }),
    onSuccess: invalidate
  });
  const observe = useMutation({
    mutationFn: () => api.observeRelease(projectId, observedReleaseId, releaseHealthy === 'true', metricsJson),
    onSuccess: invalidate
  });
  const terminate = useMutation({
    mutationFn: () =>
      api.terminateProject(projectId, {
        id: createUuidV4(),
        outcome: terminationOutcome,
        whatWorked,
        whatFailed,
        lessons: lessons
          .split('\n')
          .map((value) => value.trim())
          .filter(Boolean)
      }),
    onSuccess: invalidate
  });

  if (delivery.isLoading) return <LocalizedLoadingState />;
  if (delivery.error) return <LocalizedErrorState error={delivery.error} />;
  const state = delivery.data;
  if (!state) return <EmptyState title={t('deliveryPage.noDeliveryData')} description={t('common.notAvailable')} />;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('deliveryPage.deliveryLifecycle')}
          description={t('deliveryPage.evidenceGatedResearchRequirementsReviewsAcceptanceAndRelease')}
        />
        <Card title={t('deliveryPage.evidenceSummary')} headingLevel="h2">
          <DetailList
            columns={2}
            dense
            items={[
              {
                label: t('deliveryPage.researchFindings'),
                value: t('deliveryPage.findingsReviews', {
                  findings: localizedCount(t, format, 'deliveryPage.findingCount', state.findings?.length ?? 0),
                  reviews: localizedCount(t, format, 'deliveryPage.reviewCount', state.reviews?.length ?? 0)
                })
              },
              {
                label: t('deliveryPage.independentAcceptance'),
                value: t('deliveryPage.acceptanceReportsDoneEvidenceRecords', {
                  reports: localizedCount(t, format, 'deliveryPage.acceptanceReportCount', state.acceptanceReports?.length ?? 0),
                  records: localizedCount(t, format, 'deliveryPage.doneEvidenceRecordCount', state.evidence?.length ?? 0)
                })
              },
              {
                label: t('deliveryPage.requirements'),
                value: format.number(state.requirements?.length ?? 0)
              },
              {
                label: t('deliveryPage.defect'),
                value: format.number(state.defects?.length ?? 0)
              },
              {
                label: t('common.release'),
                value: format.number(state.releases?.length ?? 0)
              }
            ]}
          />
        </Card>
        <SectionHeader title={t('deliveryPage.researchFinding')} />
        <Stack direction="row" spacing={2} wrap sx={{ alignItems: 'stretch' }}>
          <Stack sx={{ flex: '1 1 420px' }}>
            <Card>
              <FormField label={t('deliveryPage.claim')}>
                <TextInput value={claim} onChange={setClaim} placeholder={t('deliveryPage.usersNeedDeterministicApproval')} />
              </FormField>
              <FormField label={t('deliveryPage.sourceA')}>
                <TextInput value={sourceA} onChange={setSourceA} placeholder={t('deliveryPage.interviewOwner1')} />
              </FormField>
              <FormField label={t('deliveryPage.sourceB')}>
                <TextInput value={sourceB} onChange={setSourceB} placeholder={t('deliveryPage.surveyApproval1')} />
              </FormField>
              <Select
                label={t('deliveryPage.keyClaim')}
                value={keyClaim}
                onChange={setKeyClaim}
                options={[
                  { value: 'false', label: t('common.no') },
                  {
                    value: 'true',
                    label: t('deliveryPage.yesTwoSourcesRequired')
                  }
                ]}
              />
              <Button
                onClick={() => addFinding.mutate()}
                disabled={!claim || !sourceA || (keyClaim === 'true' && !sourceB)}
                loading={addFinding.isPending}
              >
                {t('deliveryPage.addFinding')}
              </Button>
              <LocalizedMutationFeedback mutation={addFinding} successMessage={t('deliveryPage.researchFindingAdded')} />
            </Card>
          </Stack>
          <Stack sx={{ flex: '1 1 420px' }}>
            <Card title={t('deliveryPage.requirement')}>
              <FormField label={t('common.title')}>
                <TextInput value={title} onChange={setTitle} placeholder={t('deliveryPage.approvalDecisionFlow')} />
              </FormField>
              <FormField label={t('deliveryPage.problem')}>
                <TextInput value={problem} onChange={setProblem} placeholder={t('deliveryPage.ownersNeedExplicitControl')} />
              </FormField>
              <FormField label={t('common.scope')}>
                <TextInput value={scope} onChange={setScope} placeholder={t('deliveryPage.tenantDashboard')} />
              </FormField>
              <FormField label={t('deliveryPage.acceptanceCriteriaOnePerLine')}>
                <TextInput
                  value={criteria}
                  onChange={setCriteria}
                  multiline
                  minRows={3}
                  placeholder={t('deliveryPage.acceptanceCriteriaPlaceholder')}
                />
              </FormField>
              <Select
                label={t('common.priority')}
                value={priority}
                onChange={setPriority}
                options={['P0', 'P1', 'P2'].map((value) => ({
                  value,
                  label: domainLabel(value)
                }))}
              />
              <Button
                onClick={() => addRequirement.mutate()}
                disabled={!title || !problem || !scope || !criteria}
                loading={addRequirement.isPending}
              >
                {t('deliveryPage.createRequirement')}
              </Button>
              <LocalizedMutationFeedback mutation={addRequirement} successMessage={t('deliveryPage.requirementCreated')} />
            </Card>
          </Stack>
        </Stack>
        <SectionHeader title={t('deliveryPage.productBrief')} />
        <Stack direction="row" spacing={2} wrap sx={{ alignItems: 'stretch' }}>
          <Stack sx={{ flex: '1 1 420px' }}>
            <Card>
              <FormField label={t('deliveryPage.briefTitle')}>
                <TextInput value={briefTitle} onChange={setBriefTitle} />
              </FormField>
              <FormField label={t('deliveryPage.problem')}>
                <TextInput value={briefProblem} onChange={setBriefProblem} />
              </FormField>
              <FormField label={t('deliveryPage.mVPScopeCommaSeparated')}>
                <TextInput value={mvpScope} onChange={setMvpScope} />
              </FormField>
              <FormField label={t('deliveryPage.successMetricsKeyValuePerLine')}>
                <TextInput value={successMetrics} onChange={setSuccessMetrics} multiline minRows={2} />
              </FormField>
              <FormField label={t('deliveryPage.findingIDs')}>
                <TextInput value={findingIds} onChange={setFindingIds} />
              </FormField>
              <Button
                onClick={() => createBrief.mutate()}
                disabled={!briefTitle || !briefProblem || !mvpScope || !successMetrics}
                loading={createBrief.isPending}
              >
                {t('deliveryPage.createBrief')}
              </Button>
              <LocalizedMutationFeedback mutation={createBrief} successMessage={t('deliveryPage.productBriefCreated')} />
            </Card>
          </Stack>
          <Stack sx={{ flex: '1 1 420px' }}>
            <Card title={t('deliveryPage.goNoGoDecision')}>
              <Select
                label={t('deliveryPage.brief')}
                value={briefId}
                onChange={setBriefId}
                options={[
                  { value: '', label: t('deliveryPage.selectBrief') },
                  ...(state.briefs ?? []).map((brief) => ({
                    value: brief.id ?? '',
                    label: brief.title ?? ''
                  }))
                ]}
              />
              <Select
                label={t('common.decision')}
                value={decision}
                onChange={setDecision}
                options={[
                  { value: 'Go', label: t('deliveryPage.go') },
                  { value: 'NoGo', label: t('deliveryPage.noGo') }
                ]}
              />
              <FormField label={t('deliveryPage.rationale')}>
                <TextInput value={rationale} onChange={setRationale} multiline minRows={2} />
              </FormField>
              <FormField label={t('deliveryPage.evidenceIDs')}>
                <TextInput value={evidenceIds} onChange={setEvidenceIds} />
              </FormField>
              <FormField label={t('deliveryPage.reround')}>
                <TextInput value={reround} onChange={setReround} />
              </FormField>
              <Button
                tone="secondary"
                onClick={() => decide.mutate()}
                disabled={!briefId || !rationale || !evidenceIds}
                loading={decide.isPending}
              >
                {t('deliveryPage.recordDecision')}
              </Button>
              <LocalizedMutationFeedback mutation={decide} successMessage={t('deliveryPage.briefDecisionRecorded')} />
            </Card>
          </Stack>
        </Stack>
        <SectionHeader title={t('deliveryPage.researchFindings')} />
        <Card>
          <LocalizedDataTable
            rows={state.findings ?? []}
            getRowId={(row) => row.id ?? ''}
            density="compact"
            columns={[
              {
                id: 'claim',
                header: t('deliveryPage.claim'),
                render: (row) => row.claim ?? ''
              },
              {
                id: 'sources',
                header: t('deliveryPage.sources'),
                render: (row) => (row.sources ?? []).map((source) => source.id).join(', ')
              },
              {
                id: 'key',
                header: t('deliveryPage.keyClaim'),
                render: (row) => (row.keyClaim ? t('common.yes') : t('common.no'))
              }
            ]}
          />
        </Card>
        <SectionHeader title={t('deliveryPage.requirements')} />
        <Card>
          <LocalizedDataTable
            rows={state.requirements ?? []}
            getRowId={(row) => row.id ?? ''}
            density="compact"
            columns={[
              {
                id: 'title',
                header: t('deliveryPage.requirement'),
                render: (row) => row.title ?? ''
              },
              {
                id: 'priority',
                header: t('common.priority'),
                render: (row) => domainLabel(row.priority)
              },
              {
                id: 'status',
                header: t('common.status'),
                render: (row) => <StatusChip label={domainLabel(row.status)} tone={statusTone(row.status)} />
              },
              {
                id: 'actions',
                header: t('common.actions'),
                render: (row) => (
                  <Stack direction="row" wrap>
                    <Button tone="neutral" onClick={() => makeReady.mutate(row.id ?? '')} disabled={row.status !== 'Draft'}>
                      {t('deliveryPage.ready')}
                    </Button>
                    <Button tone="secondary" onClick={() => createPlan.mutate(row.id ?? '')} disabled={row.status !== 'Ready'}>
                      {t('deliveryPage.plan')}
                    </Button>
                  </Stack>
                )
              }
            ]}
          />
          <LocalizedMutationFeedback mutation={makeReady} successMessage={t('deliveryPage.requirementReady')} />
          <LocalizedMutationFeedback mutation={createPlan} successMessage={t('deliveryPage.planCreatedAndApproved')} />
        </Card>
        <SectionHeader title={t('deliveryPage.tasksAndDefects')} />
        <Card>
          <LocalizedDataTable
            rows={state.tasks ?? []}
            getRowId={(row) => row.id ?? ''}
            density="compact"
            columns={[
              {
                id: 'title',
                header: t('common.task'),
                render: (row) => row.title ?? ''
              },
              {
                id: 'type',
                header: t('deliveryPage.type'),
                render: (row) => row.taskType ?? ''
              },
              {
                id: 'weight',
                header: t('deliveryPage.weight'),
                render: (row) => format.number(toFiniteNumber(row.weight))
              },
              {
                id: 'status',
                header: t('common.status'),
                render: (row) => <StatusChip label={domainLabel(row.status)} tone={statusTone(row.status)} />
              }
            ]}
          />
        </Card>
        <Card title={t('deliveryPage.defect')}>
          <FormField label={t('deliveryPage.defectTitle')}>
            <TextInput value={defectTitle} onChange={setDefectTitle} placeholder={t('deliveryPage.approvalCanBeRepeated')} />
          </FormField>
          <FormField label={t('deliveryPage.reproductionSteps')}>
            <TextInput value={defectSteps} onChange={setDefectSteps} multiline minRows={2} />
          </FormField>
          <Select
            label={t('common.severity')}
            value={defectSeverity}
            onChange={setDefectSeverity}
            options={['P0', 'P1', 'P2', 'P3'].map((value) => ({
              value,
              label: domainLabel(value)
            }))}
          />
          <Button
            tone="danger"
            onClick={() => reportDefect.mutate()}
            disabled={!defectTitle || !defectSteps}
            loading={reportDefect.isPending}
          >
            {t('deliveryPage.reportDefect')}
          </Button>
          <LocalizedMutationFeedback mutation={reportDefect} successMessage={t('deliveryPage.defectReported')} />
          <LocalizedDataTable
            rows={state.defects ?? []}
            getRowId={(row) => row.id ?? ''}
            density="compact"
            columns={[
              {
                id: 'title',
                header: t('deliveryPage.defect'),
                render: (row) => row.title ?? ''
              },
              {
                id: 'severity',
                header: t('common.severity'),
                render: (row) => <StatusChip label={domainLabel(row.severity)} tone={severityTone[row.severity ?? ''] ?? 'neutral'} />
              },
              {
                id: 'status',
                header: t('common.status'),
                render: (row) => <StatusChip label={domainLabel(row.status)} tone={statusTone(row.status)} />
              },
              {
                id: 'round',
                header: t('deliveryPage.retestRound'),
                render: (row) => format.number(toFiniteNumber(row.retestRound))
              },
              {
                id: 'actions',
                header: t('common.actions'),
                render: (row) => (
                  <Stack direction="row" wrap>
                    <Button
                      tone="secondary"
                      onClick={() => fixDefect.mutate(row.id ?? '')}
                      disabled={row.status !== 'New' && row.status !== 'Reopened'}
                    >
                      {t('deliveryPage.markFixed')}
                    </Button>
                    <Button
                      tone="neutral"
                      onClick={() => retest.mutate(row.id ?? '')}
                      disabled={row.status !== 'ReadyForRetest' || Number(row.retestRound ?? 0) >= 2}
                    >
                      {t('deliveryPage.retest')}
                    </Button>
                  </Stack>
                )
              }
            ]}
          />
          <LocalizedMutationFeedback mutation={fixDefect} successMessage={t('deliveryPage.defectMarkedFixed')} />
          <LocalizedMutationFeedback mutation={retest} successMessage={t('deliveryPage.defectRetested')} />
        </Card>
        <SectionHeader title={t('deliveryPage.independentAcceptance')} />
        <Card>
          <Select
            label={t('deliveryPage.requirement')}
            value={acceptanceRequirement}
            onChange={setAcceptanceRequirement}
            options={[
              { value: '', label: t('deliveryPage.selectRequirement') },
              ...(state.requirements ?? []).map((item) => ({
                value: item.id ?? '',
                label: item.title ?? ''
              }))
            ]}
          />

          <Stack direction="row" spacing={2} wrap>
            <Stack sx={{ flex: '1 1 180px' }}>
              <FormField label={t('deliveryPage.functionalScore')}>
                <TextInput value={functionalScore} onChange={setFunctionalScore} />
              </FormField>
            </Stack>
            <Stack sx={{ flex: '1 1 180px' }}>
              <FormField label={t('deliveryPage.usabilityScore')}>
                <TextInput value={usabilityScore} onChange={setUsabilityScore} />
              </FormField>
            </Stack>
            <Stack sx={{ flex: '1 1 180px' }}>
              <FormField label={t('deliveryPage.designScore')}>
                <TextInput value={designScore} onChange={setDesignScore} />
              </FormField>
            </Stack>
            <Stack sx={{ flex: '1 1 180px' }}>
              <FormField label={t('deliveryPage.accessibilityScore')}>
                <TextInput value={accessibilityScore} onChange={setAccessibilityScore} />
              </FormField>
            </Stack>
          </Stack>
          <Button onClick={() => acceptance.mutate()} disabled={!acceptanceRequirement} loading={acceptance.isPending}>
            {t('deliveryPage.submitAcceptanceReport')}
          </Button>
          <LocalizedMutationFeedback mutation={acceptance} successMessage={t('deliveryPage.acceptanceReportSubmitted')} />
        </Card>
        <SectionHeader title={t('common.release')} />
        <Stack direction="row" spacing={2} wrap sx={{ alignItems: 'stretch' }}>
          <Stack sx={{ flex: '1 1 420px' }}>
            <Card>
              <FormField label={t('common.version')}>
                <TextInput value={version} onChange={setVersion} />
              </FormField>
              <FormField label={t('deliveryPage.artifactReference')}>
                <TextInput value={artifactRef} onChange={setArtifactRef} />
              </FormField>
              <Button onClick={() => setReleaseConfirmOpen(true)} disabled={!version || !artifactRef} loading={release.isPending}>
                {t('deliveryPage.runReleaseGate')}
              </Button>
              <LocalizedMutationFeedback mutation={release} successMessage={t('deliveryPage.releaseGateCompleted')} />
              {(state.releases?.length ?? 0) === 0 ? (
                <EmptyState
                  title={t('deliveryPage.noReleaseYet')}
                  description={t('deliveryPage.acceptEveryRequirementAndCloseP0P1DefectsFirst')}
                />
              ) : null}
              <Select
                label={t('deliveryPage.observeRelease')}
                value={observedReleaseId}
                onChange={setObservedReleaseId}
                options={[
                  { value: '', label: t('deliveryPage.selectRelease') },
                  ...(state.releases ?? []).map((item) => ({
                    value: item.id ?? '',
                    label: item.version ?? ''
                  }))
                ]}
              />
              <Select
                label={t('deliveryPage.health')}
                value={releaseHealthy}
                onChange={setReleaseHealthy}
                options={[
                  { value: 'true', label: t('deliveryPage.healthy') },
                  { value: 'false', label: t('deliveryPage.degraded') }
                ]}
              />
              <FormField label={t('deliveryPage.metricsJSON')}>
                <TextInput value={metricsJson} onChange={setMetricsJson} />
              </FormField>
              <Button tone="secondary" onClick={() => observe.mutate()} disabled={!observedReleaseId} loading={observe.isPending}>
                {t('deliveryPage.recordObservation')}
              </Button>
              <LocalizedMutationFeedback mutation={observe} successMessage={t('deliveryPage.releaseObservationRecorded')} />
            </Card>
          </Stack>
          <Stack sx={{ flex: '1 1 420px' }}>
            <Card title={t('deliveryPage.evidenceSummary')}>
              <LocalizedDataTable
                rows={state.releases ?? []}
                getRowId={(row) => row.id ?? ''}
                density="compact"
                columns={[
                  {
                    id: 'version',
                    header: t('common.version'),
                    render: (row) => row.version ?? ''
                  },
                  {
                    id: 'status',
                    header: t('common.status'),
                    render: (row) => <StatusChip label={domainLabel(row.status)} tone={statusTone(row.status)} />
                  },
                  {
                    id: 'artifact',
                    header: t('deliveryPage.artifact'),
                    render: (row) => row.artifactRef ?? ''
                  }
                ]}
              />
            </Card>
          </Stack>
        </Stack>
        <SectionHeader title={t('deliveryPage.terminateProject')} />
        <Card>
          <FormField label={t('deliveryPage.outcome')}>
            <TextInput value={terminationOutcome} onChange={setTerminationOutcome} />
          </FormField>
          <FormField label={t('deliveryPage.whatWorked')}>
            <TextInput value={whatWorked} onChange={setWhatWorked} multiline minRows={2} />
          </FormField>
          <FormField label={t('deliveryPage.whatFailed')}>
            <TextInput value={whatFailed} onChange={setWhatFailed} multiline minRows={2} />
          </FormField>
          <FormField label={t('deliveryPage.lessonsOnePerLine')}>
            <TextInput value={lessons} onChange={setLessons} multiline minRows={2} />
          </FormField>
          <Button
            tone="danger"
            onClick={() => setTerminateConfirmOpen(true)}
            disabled={!terminationOutcome || !lessons}
            loading={terminate.isPending}
          >
            {t('deliveryPage.terminateAndSaveRetrospective')}
          </Button>
          <LocalizedMutationFeedback mutation={terminate} successMessage={t('deliveryPage.projectTerminated')} />
        </Card>
        <ConfirmDialog
          open={releaseConfirmOpen}
          onClose={() => setReleaseConfirmOpen(false)}
          onConfirm={() => {
            release.mutate();
            setReleaseConfirmOpen(false);
          }}
          title={t('deliveryPage.runReleaseGate')}
          description={t('deliveryPage.acceptEveryRequirementAndCloseP0P1DefectsFirst')}
          confirmLabel={t('deliveryPage.runReleaseGate')}
          cancelLabel={t('common.no')}
          tone="danger"
          loading={release.isPending}
        />
        <ConfirmDialog
          open={terminateConfirmOpen}
          onClose={() => setTerminateConfirmOpen(false)}
          onConfirm={() => {
            terminate.mutate();
            setTerminateConfirmOpen(false);
          }}
          title={t('deliveryPage.terminateProject')}
          description={t('deliveryPage.terminateAndSaveRetrospective')}
          confirmLabel={t('deliveryPage.terminateAndSaveRetrospective')}
          cancelLabel={t('common.no')}
          tone="danger"
          loading={terminate.isPending}
        />
      </Stack>
    </ContentContainer>
  );
}
