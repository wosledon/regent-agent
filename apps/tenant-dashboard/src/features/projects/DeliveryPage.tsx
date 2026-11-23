import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import {
  Button, Card, DataTable, EmptyState, ErrorState, FormField, Grid, LoadingState,
  PageHeader, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function DeliveryPage() {
  const { projectId = '' } = useParams();
  const queryClient = useQueryClient();
  const delivery = useQuery({ queryKey: ['delivery', projectId], queryFn: () => api.delivery(projectId) });
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
  const [acceptanceReviewer, setAcceptanceReviewer] = useState('Independent Reviewer');
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
    mutationFn: () => api.addFinding(projectId, {
      id: crypto.randomUUID(), claim, keyClaim: keyClaim === 'true', counterEvidence: null,
      sources: [
        { id: sourceA, kind: 'research', location: sourceA },
        ...(sourceB ? [{ id: sourceB, kind: 'research', location: sourceB }] : [])
      ]
    }), onSuccess: async () => { setClaim(''); setSourceA(''); setSourceB(''); await invalidate(); }
  });
  const addRequirement = useMutation({
    mutationFn: () => api.createRequirement(projectId, {
      id: crypto.randomUUID(), title, problem, scope, priority, status: 'Draft',
      acceptanceCriteria: criteria.split('\n').map(value => value.trim()).filter(Boolean), version: 1, totalWeight: 0
    }), onSuccess: async () => { setTitle(''); setProblem(''); setScope(''); setCriteria(''); await invalidate(); }
  });
  const createBrief = useMutation({
    mutationFn: () => api.createBrief(projectId, {
      id: crypto.randomUUID(), title: briefTitle, problem: briefProblem,
      mvpScope: mvpScope.split(',').map(value => value.trim()).filter(Boolean),
      successMetrics: Object.fromEntries(successMetrics.split('\n').map(line => line.split('=')).filter(parts => parts.length === 2).map(parts => [parts[0].trim(), parts[1].trim()])),
      findingIds: findingIds.split(',').map(value => value.trim()).filter(Boolean), version: 1
    }), onSuccess: async () => { setBriefTitle(''); setBriefProblem(''); setMvpScope(''); await invalidate(); }
  });
  const decide = useMutation({
    mutationFn: () => api.decideBrief(projectId, briefId, decision, rationale,
      evidenceIds.split(',').map(value => value.trim()).filter(Boolean), Number(reround), 'Controller'), onSuccess: invalidate
  });
  const makeReady = useMutation({
    mutationFn: (requirementId: string) => api.readyRequirement(projectId, requirementId, 6), onSuccess: invalidate
  });
  const createPlan = useMutation({
    mutationFn: async (requirementId: string) => {
      const requirement = delivery.data?.requirements?.find(item => item.id === requirementId);
      if (!requirement) throw new Error('Requirement not found.');
      const acceptanceCriteria = requirement.acceptanceCriteria ?? [];
      const tasks = Array.from({ length: 3 }, (_, index) => ({
        id: crypto.randomUUID(), projectId, requirementId,
        title: `${requirement.title ?? 'Requirement'} — step ${index + 1}`, taskType: 'engineering.implement',
        requiredCapabilitiesJson: JSON.stringify(['engineering.implement']), dependsOn: [], weight: 2,
        status: 'Backlog', author: 'Controller', assignee: null, validator: null,
        acceptanceCriteria: acceptanceCriteria.slice(index, index + 1)
      }));
      const plan = await api.proposePlan(projectId, {
        id: crypto.randomUUID(), requirementId, requirementVersion: requirement.version ?? 1,
        tasks, validationErrors: [], status: 'Draft'
      });
      return api.approvePlan(projectId, plan.id ?? '');
    }, onSuccess: invalidate
  });
  const reportDefect = useMutation({
    mutationFn: () => api.reportDefect(projectId, {
      id: crypto.randomUUID(), projectId, requirementId: delivery.data?.requirements?.[0]?.id || '',
      taskId: null, title: defectTitle, severity: defectSeverity, status: 'New',
      reproductionSteps: defectSteps, reportedBy: 'Controller', fixer: null, retester: null, evidence: []
    }), onSuccess: async () => { setDefectTitle(''); setDefectSteps(''); await invalidate(); }
  });
  const release = useMutation({
    mutationFn: () => api.releaseProject(projectId, version, artifactRef), onSuccess: invalidate
  });
  const fixDefect = useMutation({ mutationFn: (id: string) => api.fixDefect(projectId, id), onSuccess: invalidate });
  const retest = useMutation({ mutationFn: (id: string) => api.retestDefect(projectId, id, true, 'Quality Reviewer'), onSuccess: invalidate });
  const acceptance = useMutation({
    mutationFn: () => api.addAcceptanceReport(projectId, {
      id: crypto.randomUUID(), requirementId: acceptanceRequirement, reviewer: acceptanceReviewer,
      functionalScore: Number(functionalScore), usabilityScore: Number(usabilityScore),
      designScore: Number(designScore), accessibilityScore: Number(accessibilityScore),
      evidence: [{ id: 'acceptance-evidence', kind: 'report', location: 'local' }]
    }), onSuccess: invalidate
  });
  const observe = useMutation({
    mutationFn: () => api.observeRelease(projectId, observedReleaseId, releaseHealthy === 'true', metricsJson), onSuccess: invalidate
  });
  const terminate = useMutation({
    mutationFn: () => api.terminateProject(projectId, {
      id: crypto.randomUUID(), outcome: terminationOutcome, whatWorked, whatFailed,
      lessons: lessons.split('\n').map(value => value.trim()).filter(Boolean)
    }), onSuccess: invalidate
  });

  if (delivery.isLoading) return <LoadingState />;
  if (delivery.error) return <ErrorState message={delivery.error.message} />;
  const state = delivery.data;
  if (!state) return null;

  return (
    <Stack spacing={3}>
      <PageHeader title="Delivery lifecycle" description="Evidence-gated research, requirements, reviews, acceptance and release." />
      <Grid columns={2}>
        <Card title="Research finding">
          <FormField label="Claim"><TextInput value={claim} onChange={setClaim} placeholder="Users need deterministic approval" /></FormField>
          <FormField label="Source A"><TextInput value={sourceA} onChange={setSourceA} placeholder="interview:owner-1" /></FormField>
          <FormField label="Source B"><TextInput value={sourceB} onChange={setSourceB} placeholder="survey:approval-1" /></FormField>
          <Select label="Key claim" value={keyClaim} onChange={setKeyClaim} options={[{ value: 'false', label: 'No' }, { value: 'true', label: 'Yes — two sources required' }]} />
          <Button onClick={() => addFinding.mutate()} disabled={!claim || !sourceA || (keyClaim === 'true' && !sourceB)} loading={addFinding.isPending}>Add finding</Button>
        </Card>
        <Card title="Requirement">
          <FormField label="Title"><TextInput value={title} onChange={setTitle} placeholder="Approval decision flow" /></FormField>
          <FormField label="Problem"><TextInput value={problem} onChange={setProblem} placeholder="Owners need explicit control" /></FormField>
          <FormField label="Scope"><TextInput value={scope} onChange={setScope} placeholder="Tenant dashboard" /></FormField>
          <FormField label="Acceptance criteria (one per line)"><TextInput value={criteria} onChange={setCriteria} multiline minRows={3} placeholder={'Owner can approve\nDuplicate decision is rejected'} /></FormField>
          <Select label="Priority" value={priority} onChange={setPriority} options={['P0', 'P1', 'P2'].map(value => ({ value, label: value }))} />
          <Button onClick={() => addRequirement.mutate()} disabled={!title || !problem || !scope || !criteria} loading={addRequirement.isPending}>Create requirement</Button>
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card title="Product brief">
          <FormField label="Brief title"><TextInput value={briefTitle} onChange={setBriefTitle} /></FormField>
          <FormField label="Problem"><TextInput value={briefProblem} onChange={setBriefProblem} /></FormField>
          <FormField label="MVP scope (comma separated)"><TextInput value={mvpScope} onChange={setMvpScope} /></FormField>
          <FormField label="Success metrics (key=value per line)"><TextInput value={successMetrics} onChange={setSuccessMetrics} multiline minRows={2} /></FormField>
          <FormField label="Finding IDs"><TextInput value={findingIds} onChange={setFindingIds} /></FormField>
          <Button onClick={() => createBrief.mutate()} disabled={!briefTitle || !briefProblem || !mvpScope || !successMetrics} loading={createBrief.isPending}>Create brief</Button>
        </Card>
        <Card title="Go / No-Go decision">
          <Select label="Brief" value={briefId} onChange={setBriefId} options={[{ value: '', label: 'Select brief' }, ...(state.briefs ?? []).map(brief => ({ value: brief.id ?? '', label: brief.title ?? '' }))]} />
          <Select label="Decision" value={decision} onChange={setDecision} options={[{ value: 'Go', label: 'Go' }, { value: 'NoGo', label: 'No-Go' }]} />
          <FormField label="Rationale"><TextInput value={rationale} onChange={setRationale} multiline minRows={2} /></FormField>
          <FormField label="Evidence IDs"><TextInput value={evidenceIds} onChange={setEvidenceIds} /></FormField>
          <FormField label="Reround"><TextInput value={reround} onChange={setReround} /></FormField>
          <Button tone="secondary" onClick={() => decide.mutate()} disabled={!briefId || !rationale || !evidenceIds} loading={decide.isPending}>Record decision</Button>
        </Card>
      </Grid>
      <Card title="Research findings">
        <DataTable rows={state.findings ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'claim', header: 'Claim', render: row => row.claim ?? '' },
          { id: 'sources', header: 'Sources', render: row => (row.sources ?? []).map(source => source.id).join(', ') },
          { id: 'key', header: 'Key claim', render: row => row.keyClaim ? 'Yes' : 'No' }
        ]} />
      </Card>
      <Card title="Requirements">
        <DataTable rows={state.requirements ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'title', header: 'Requirement', render: row => row.title ?? '' },
          { id: 'priority', header: 'Priority', render: row => row.priority ?? '' },
          { id: 'status', header: 'Status', render: row => <StatusChip label={row.status ?? 'Unknown'} tone={row.status === 'Accepted' ? 'success' : 'warning'} /> },
          { id: 'actions', header: 'Actions', render: row => <Stack direction="row"><Button tone="neutral" onClick={() => makeReady.mutate(row.id ?? '')} disabled={row.status !== 'Draft'}>Ready</Button><Button tone="secondary" onClick={() => createPlan.mutate(row.id ?? '')} disabled={row.status !== 'Ready'}>Plan</Button></Stack> }
        ]} />
      </Card>
      <Card title="Tasks and defects">
        <DataTable rows={state.tasks ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'title', header: 'Task', render: row => row.title ?? '' },
          { id: 'type', header: 'Type', render: row => row.taskType ?? '' },
          { id: 'weight', header: 'Weight', render: row => row.weight ?? 0 },
          { id: 'status', header: 'Status', render: row => row.status ?? '' }
        ]} />
        <FormField label="Defect title"><TextInput value={defectTitle} onChange={setDefectTitle} placeholder="Approval can be repeated" /></FormField>
        <FormField label="Reproduction steps"><TextInput value={defectSteps} onChange={setDefectSteps} multiline minRows={2} /></FormField>
        <Select label="Severity" value={defectSeverity} onChange={setDefectSeverity} options={['P0', 'P1', 'P2', 'P3'].map(value => ({ value, label: value }))} />
        <Button tone="danger" onClick={() => reportDefect.mutate()} disabled={!defectTitle || !defectSteps} loading={reportDefect.isPending}>Report defect</Button>
        <DataTable rows={state.defects ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'title', header: 'Defect', render: row => row.title ?? '' },
          { id: 'severity', header: 'Severity', render: row => row.severity ?? '' },
          { id: 'status', header: 'Status', render: row => row.status ?? '' },
          { id: 'round', header: 'Retest round', render: row => row.retestRound ?? 0 },
          { id: 'actions', header: 'Actions', render: row => <Stack direction="row">
            <Button tone="secondary" onClick={() => fixDefect.mutate(row.id ?? '')} disabled={row.status !== 'New' && row.status !== 'Reopened'}>Mark fixed</Button>
            <Button tone="neutral" onClick={() => retest.mutate(row.id ?? '')} disabled={row.status !== 'ReadyForRetest' || Number(row.retestRound ?? 0) >= 2}>Retest</Button>
          </Stack> }
        ]} />
      </Card>
      <Card title="Independent acceptance">
        <Select label="Requirement" value={acceptanceRequirement} onChange={setAcceptanceRequirement} options={[{ value: '', label: 'Select requirement' }, ...(state.requirements ?? []).map(item => ({ value: item.id ?? '', label: item.title ?? '' }))]} />
        <FormField label="Reviewer"><TextInput value={acceptanceReviewer} onChange={setAcceptanceReviewer} /></FormField>
        <FormField label="Functional score"><TextInput value={functionalScore} onChange={setFunctionalScore} /></FormField>
        <FormField label="Usability score"><TextInput value={usabilityScore} onChange={setUsabilityScore} /></FormField>
        <FormField label="Design score"><TextInput value={designScore} onChange={setDesignScore} /></FormField>
        <FormField label="Accessibility score"><TextInput value={accessibilityScore} onChange={setAccessibilityScore} /></FormField>
        <Button onClick={() => acceptance.mutate()} disabled={!acceptanceRequirement || !acceptanceReviewer} loading={acceptance.isPending}>Submit acceptance report</Button>
      </Card>
      <Grid columns={2}>
        <Card title="Release">
          <FormField label="Version"><TextInput value={version} onChange={setVersion} /></FormField>
          <FormField label="Artifact reference"><TextInput value={artifactRef} onChange={setArtifactRef} /></FormField>
          <Button onClick={() => release.mutate()} disabled={!version || !artifactRef} loading={release.isPending}>Run release gate</Button>
          {(state.releases?.length ?? 0) === 0 ? <EmptyState title="No release yet" description="Accept every requirement and close P0/P1 defects first." /> : null}
          <Select label="Observe release" value={observedReleaseId} onChange={setObservedReleaseId} options={[{ value: '', label: 'Select release' }, ...(state.releases ?? []).map(item => ({ value: item.id ?? '', label: item.version ?? '' }))]} />
          <Select label="Health" value={releaseHealthy} onChange={setReleaseHealthy} options={[{ value: 'true', label: 'Healthy' }, { value: 'false', label: 'Degraded' }]} />
          <FormField label="Metrics JSON"><TextInput value={metricsJson} onChange={setMetricsJson} /></FormField>
          <Button tone="secondary" onClick={() => observe.mutate()} disabled={!observedReleaseId} loading={observe.isPending}>Record observation</Button>
        </Card>
        <Card title="Evidence summary">
          <Text>{state.findings?.length ?? 0} findings · {state.reviews?.length ?? 0} reviews</Text>
          <Text>{state.acceptanceReports?.length ?? 0} acceptance reports · {state.evidence?.length ?? 0} DoneEvidence records</Text>
          <DataTable rows={state.releases ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'version', header: 'Version', render: row => row.version ?? '' },
            { id: 'status', header: 'Status', render: row => row.status ?? '' },
            { id: 'artifact', header: 'Artifact', render: row => row.artifactRef ?? '' }
          ]} />
        </Card>
      </Grid>
      <Card title="Terminate project">
        <FormField label="Outcome"><TextInput value={terminationOutcome} onChange={setTerminationOutcome} /></FormField>
        <FormField label="What worked"><TextInput value={whatWorked} onChange={setWhatWorked} multiline minRows={2} /></FormField>
        <FormField label="What failed"><TextInput value={whatFailed} onChange={setWhatFailed} multiline minRows={2} /></FormField>
        <FormField label="Lessons (one per line)"><TextInput value={lessons} onChange={setLessons} multiline minRows={2} /></FormField>
        <Button tone="danger" onClick={() => terminate.mutate()} disabled={!terminationOutcome || !lessons} loading={terminate.isPending}>Terminate and save retrospective</Button>
      </Card>
    </Stack>
  );
}
