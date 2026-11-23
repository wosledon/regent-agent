import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState, PageHeader, Select, Stack, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function AgentGovernancePage() {
  const queryClient = useQueryClient();
  const governance = useQuery({ queryKey: ['task-governance'], queryFn: api.taskGovernance });
  const evolution = useQuery({ queryKey: ['agent-evolution'], queryFn: api.agentEvolution });
  const [taskType, setTaskType] = useState('engineering.implement');
  const [capabilities, setCapabilities] = useState('engineering.implement,test.execute');
  const [employeeId, setEmployeeId] = useState('');
  const [dimension, setDimension] = useState('engineering');
  const [promptName, setPromptName] = useState('review');
  const [promptContent, setPromptContent] = useState('Review evidence independently.');
  const [promptKind, setPromptKind] = useState('Operations');
  const [onboardingEmployeeId, setOnboardingEmployeeId] = useState('');
  const [onboardingPromptId, setOnboardingPromptId] = useState('');
  const [toolName, setToolName] = useState('research.search');
  const [level, setLevel] = useState('Primary');
  const [disclosureCapabilities, setDisclosureCapabilities] = useState('research.execute');
  const [restricted, setRestricted] = useState(false);
  const [disclosed, setDisclosed] = useState<Awaited<ReturnType<typeof api.discloseTools>> | null>(null);
  const [reviewSubject, setReviewSubject] = useState('');
  const [debateQuestion, setDebateQuestion] = useState('');
  const [reassignTaskId, setReassignTaskId] = useState('');
  const [reassignEmployeeId, setReassignEmployeeId] = useState('');
  const [reassignApprovalId, setReassignApprovalId] = useState('');
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['task-governance'] }),
    queryClient.invalidateQueries({ queryKey: ['agent-evolution'] })
  ]);
  const registerType = useMutation({ mutationFn: () => api.registerTaskType({ taskType, requiredCapabilities: capabilities.split(',').map(x => x.trim()).filter(Boolean), toolProfile: 'Primary', doneTemplate: '{}', version: 1 }), onSuccess: invalidate });
  const assess = useMutation({ mutationFn: () => api.assessCapability({ employeeId, dimension, modelScore: 0.9, historyScore: 0.8, toolFit: 1, contextFit: 1, effectiveScore: 0, confidence: 0.9 }), onSuccess: invalidate });
  const createPrompt = useMutation({ mutationFn: () => api.createPrompt(promptName, promptContent, promptKind), onSuccess: async () => { setPromptContent(''); await invalidate(); } });
  const activatePrompt = useMutation({ mutationFn: api.activatePrompt, onSuccess: invalidate });
  const activateSkill = useMutation({ mutationFn: api.activateSkill, onSuccess: invalidate });
  const publishAsset = useMutation({ mutationFn: ({ type, id }: { type: 'Prompt' | 'Skill'; id: string }) => api.publishAgentAsset(type, id), onSuccess: invalidate });
  const onboarding = useMutation({ mutationFn: () => api.createOnboardingBaseline(onboardingEmployeeId, onboardingPromptId), onSuccess: invalidate });
  const setPolicy = useMutation({ mutationFn: () => api.setToolPolicy({ toolName, level, requiredCapabilities: disclosureCapabilities.split(',').map(x => x.trim()).filter(Boolean), summary: 'Local governed tool' }), onSuccess: invalidate });
  const disclose = useMutation({ mutationFn: () => api.discloseTools(disclosureCapabilities.split(',').map(x => x.trim()).filter(Boolean), restricted), onSuccess: setDisclosed });
  const createReview = useMutation({ mutationFn: () => api.createReviewTask({ id: crypto.randomUUID(), subjectId: reviewSubject, author: 'Controller', requiredQuorum: 2, findings: [], approvers: [], status: 'Open' }), onSuccess: invalidate });
  const approveReview = useMutation({ mutationFn: ({ id, approver }: { id: string; approver: string }) => api.approveReviewTask(id, approver), onSuccess: invalidate });
  const createDebate = useMutation({ mutationFn: () => api.createDebate({ id: crypto.randomUUID(), question: debateQuestion, options: ['Approve', 'Reject'], deadline: new Date(Date.now() + 60_000).toISOString(), decision: null, status: 'Open' }), onSuccess: async () => { setDebateQuestion(''); await invalidate(); } });
  const reassign = useMutation({ mutationFn: () => api.reassignTask(reassignTaskId, reassignEmployeeId, reassignApprovalId), onSuccess: invalidate });
  if (governance.isLoading || evolution.isLoading) return <LoadingState />;
  const error = governance.error ?? evolution.error;
  if (error) return <ErrorState message={error.message} />;
  const state = governance.data;
  const agentState = evolution.data;
  return <Stack spacing={3}>
    <PageHeader title="Agent & task governance" description="Capability scoring, work ledger, reviews, debates, prompts, skills and tool disclosure." />
    <Grid columns={2}>
      <Card title="Task type and capability">
        <FormField label="Task type"><TextInput value={taskType} onChange={setTaskType} /></FormField>
        <FormField label="Required capabilities"><TextInput value={capabilities} onChange={setCapabilities} /></FormField>
        <Button onClick={() => registerType.mutate()} loading={registerType.isPending}>Register task type</Button>
        <FormField label="Employee ID"><TextInput value={employeeId} onChange={setEmployeeId} /></FormField>
        <FormField label="Dimension"><TextInput value={dimension} onChange={setDimension} /></FormField>
        <Button tone="secondary" onClick={() => assess.mutate()} disabled={!employeeId} loading={assess.isPending}>Assess capability</Button>
        <DataTable rows={state?.capabilities ?? []} getRowId={row => `${row.employeeId}:${row.dimension}`} columns={[
          { id: 'employee', header: 'Employee', render: row => row.employeeId ?? '' },
          { id: 'dimension', header: 'Dimension', render: row => row.dimension ?? '' },
          { id: 'score', header: 'Effective', render: row => row.effectiveScore ?? 0 }
        ]} />
      </Card>
      <Card title="Prompt and skill evolution">
        <FormField label="Prompt name"><TextInput value={promptName} onChange={setPromptName} /></FormField>
        <FormField label="Prompt content"><TextInput value={promptContent} onChange={setPromptContent} multiline minRows={2} /></FormField>
        <Select label="Prompt kind" value={promptKind} onChange={setPromptKind} options={['Recruitment', 'Onboarding', 'Operations'].map(value => ({ value, label: value }))} />
        <Button onClick={() => createPrompt.mutate()} disabled={!promptContent} loading={createPrompt.isPending}>Create version</Button>
        <DataTable rows={agentState?.prompts ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'name', header: 'Prompt', render: row => row.name ?? '' },
          { id: 'version', header: 'Version', render: row => row.version ?? 1 },
          { id: 'kind', header: 'Kind', render: row => row.kind ?? 'Operations' },
          { id: 'status', header: 'Status', render: row => <StatusChip label={row.status ?? ''} tone="info" /> },
          { id: 'actions', header: 'Actions', render: row => <Stack direction="row" wrap>
            <Button tone="neutral" onClick={() => activatePrompt.mutate(row.id ?? '')} disabled={row.status === 'Active'}>Activate</Button>
            <Button tone="secondary" onClick={() => publishAsset.mutate({ type: 'Prompt', id: row.id ?? '' })} disabled={row.status !== 'Active'}>Publish</Button>
          </Stack> }
        ]} />
        <DataTable rows={agentState?.skills ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'name', header: 'Skill', render: row => row.name ?? '' },
          { id: 'version', header: 'Version', render: row => row.version ?? 1 },
          { id: 'status', header: 'Status', render: row => row.status ?? '' },
          { id: 'actions', header: 'Actions', render: row => <Stack direction="row" wrap>
            <Button tone="neutral" onClick={() => activateSkill.mutate(row.id ?? '')} disabled={row.status === 'Active'}>Activate</Button>
            <Button tone="secondary" onClick={() => publishAsset.mutate({ type: 'Skill', id: row.id ?? '' })} disabled={row.status !== 'Active'}>Publish</Button>
          </Stack> }
        ]} />
      </Card>
    </Grid>
    <Grid columns={2}>
      <Card title="Tool disclosure">
        <FormField label="Tool name"><TextInput value={toolName} onChange={setToolName} /></FormField>
        <Select label="Level" value={level} onChange={setLevel} options={['Primary', 'Auxiliary', 'Restricted', 'Forbidden'].map(value => ({ value, label: value }))} />
        <FormField label="Required capabilities"><TextInput value={disclosureCapabilities} onChange={setDisclosureCapabilities} /></FormField>
        <Button onClick={() => setPolicy.mutate()} loading={setPolicy.isPending}>Save policy</Button>
        <Button type="button" tone="secondary" onClick={() => setRestricted(value => !value)}>{restricted ? 'Restricted activated' : 'Restricted hidden'}</Button>
        <Button tone="neutral" onClick={() => disclose.mutate()} loading={disclose.isPending}>Disclose</Button>
        {disclosed?.tools.map(tool => <Text key={tool.name}>{tool.level}: {tool.name}</Text>)}
      </Card>
      <Card title="Reviews and debates">
        <FormField label="Review subject ID"><TextInput value={reviewSubject} onChange={setReviewSubject} /></FormField>
        <Button onClick={() => createReview.mutate()} disabled={!reviewSubject} loading={createReview.isPending}>Create review</Button>
        <DataTable rows={state?.reviews ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'subject', header: 'Subject', render: row => row.subjectId ?? '' },
          { id: 'status', header: 'Status', render: row => row.status ?? '' },
          { id: 'actions', header: 'Actions', render: row => <Button tone="neutral" onClick={() => approveReview.mutate({ id: row.id ?? '', approver: 'Reviewer' })} disabled={row.status === 'Approved'}>Approve</Button> }
        ]} />
        <FormField label="Debate question"><TextInput value={debateQuestion} onChange={setDebateQuestion} /></FormField>
        <Button tone="secondary" onClick={() => createDebate.mutate()} disabled={!debateQuestion} loading={createDebate.isPending}>Open debate</Button>
        <DataTable rows={state?.debates ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'question', header: 'Question', render: row => row.question ?? '' },
          { id: 'status', header: 'Status', render: row => row.status ?? '' }
        ]} />
      </Card>
    </Grid>
    <Card title="Onboarding baseline and public assets">
      <Stack spacing={2}>
        <FormField label="Employee ID"><TextInput value={onboardingEmployeeId} onChange={setOnboardingEmployeeId} /></FormField>
        <FormField label="Active prompt bundle ID"><TextInput value={onboardingPromptId} onChange={setOnboardingPromptId} /></FormField>
        <Button tone="secondary" disabled={!onboardingEmployeeId || !onboardingPromptId} loading={onboarding.isPending} onClick={() => onboarding.mutate()}>Create onboarding baseline</Button>
        <DataTable rows={agentState?.onboardingBaselines ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'employee', header: 'Employee', render: row => row.employeeId ?? '' },
          { id: 'prompt', header: 'Prompt', render: row => row.promptBundleId ?? '' },
          { id: 'status', header: 'Status', render: row => row.status ?? '' }
        ]} />
        <DataTable rows={agentState?.publicAssets ?? []} getRowId={row => row.id ?? ''} columns={[
          { id: 'asset', header: 'Asset', render: row => `${row.assetType}:${row.assetId}` },
          { id: 'version', header: 'Version', render: row => row.version ?? 1 },
          { id: 'publisher', header: 'Publisher', render: row => row.publishedBy ?? '' }
        ]} />
      </Stack>
    </Card>
    <Card title="Approved task reassignment">
      <Stack spacing={2}>
        <FormField label="Task ID"><TextInput value={reassignTaskId} onChange={setReassignTaskId} /></FormField>
        <FormField label="New employee ID"><TextInput value={reassignEmployeeId} onChange={setReassignEmployeeId} /></FormField>
        <FormField label="Approval ID"><TextInput value={reassignApprovalId} onChange={setReassignApprovalId} /></FormField>
        <Button tone="secondary" disabled={!reassignTaskId || !reassignEmployeeId || !reassignApprovalId} loading={reassign.isPending} onClick={() => reassign.mutate()}>Reassign with approval</Button>
      </Stack>
    </Card>
    <Card title="Work ledger">
      <DataTable rows={state?.ledger ?? []} getRowId={row => row.id ?? ''} columns={[
        { id: 'employee', header: 'Employee', render: row => row.employeeId ?? '' },
        { id: 'task', header: 'Task', render: row => row.taskId ?? '' },
        { id: 'type', header: 'Type', render: row => row.taskType ?? '' },
        { id: 'score', header: 'Weighted score', render: row => row.weightedScore ?? 0 }
      ]} />
    </Card>
  </Stack>;
}
