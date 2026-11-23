import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState,
  PageHeader, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function OrganizationPage() {
  const queryClient = useQueryClient();
  const charter = useQuery({ queryKey: ['charter'], queryFn: api.charter });
  const charterTemplates = useQuery({ queryKey: ['charter-templates'], queryFn: api.charterTemplates });
  const positions = useQuery({ queryKey: ['positions'], queryFn: api.positions });
  const employees = useQuery({ queryKey: ['employees'], queryFn: api.employees });
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants });
  const [positionName, setPositionName] = useState('');
  const [responsibilities, setResponsibilities] = useState('');
  const [capabilities, setCapabilities] = useState('');
  const [budget, setBudget] = useState('100000');
  const [candidateName, setCandidateName] = useState('');
  const [candidatePosition, setCandidatePosition] = useState('');
  const [candidateModel, setCandidateModel] = useState('local-cheap-v1');
  const [candidateJobDescription, setCandidateJobDescription] = useState('');
  const [tenantName, setTenantName] = useState('');
  const [companyName, setCompanyName] = useState('');
  const [charterDraft, setCharterDraft] = useState<Awaited<ReturnType<typeof api.charter>> | null>(null);
  const [charterTemplate, setCharterTemplate] = useState('baseline');
  const [mergeSource, setMergeSource] = useState('');
  const [mergeTarget, setMergeTarget] = useState('');
  const [splitId, setSplitId] = useState('');
  const [splitName, setSplitName] = useState('');
  const [splitResponsibilities, setSplitResponsibilities] = useState('');
  const [rehireEmployeeId, setRehireEmployeeId] = useState('');
  const [rehirePositionId, setRehirePositionId] = useState('');
  const [rehireApprovalId, setRehireApprovalId] = useState('');

  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['charter'] }),
    queryClient.invalidateQueries({ queryKey: ['positions'] }),
    queryClient.invalidateQueries({ queryKey: ['employees'] }),
    queryClient.invalidateQueries({ queryKey: ['tenants'] })
  ]);
  const start = useMutation({ mutationFn: api.startCompany, onSuccess: refresh });
  const saveCharter = useMutation({
    mutationFn: () => api.updateCharter(charterDraft ?? charter.data!),
    onSuccess: async () => { setCharterDraft(null); await refresh(); }
  });
  const createPosition = useMutation({
    mutationFn: () => api.createPosition({
      id: crypto.randomUUID(), name: positionName, responsibilities,
      capabilities: capabilities.split(',').map(value => value.trim()).filter(Boolean),
      budgetMinor: Number(budget), parentPositionId: null, status: 'Open', version: 1
    }),
    onSuccess: async () => { setPositionName(''); await refresh(); }
  });
  const setPositionStatus = useMutation({ mutationFn: ({ id, status }: { id: string; status: string }) => api.setPositionStatus(id, status), onSuccess: refresh });
  const mergePositions = useMutation({ mutationFn: () => api.mergePositions(mergeSource, mergeTarget), onSuccess: async () => { setMergeSource(''); setMergeTarget(''); await refresh(); } });
  const splitPosition = useMutation({ mutationFn: () => api.splitPosition(splitId, splitName, splitResponsibilities), onSuccess: async () => { setSplitName(''); setSplitResponsibilities(''); await refresh(); } });
  const createCandidate = useMutation({
    mutationFn: () => api.createCandidate({ positionId: candidatePosition, displayName: candidateName, modelVersion: candidateModel, jobDescription: candidateJobDescription }),
    onSuccess: async () => { setCandidateName(''); setCandidateJobDescription(''); await refresh(); }
  });
  const rehire = useMutation({ mutationFn: () => api.rehireEmployee(rehireEmployeeId, rehirePositionId, rehireApprovalId), onSuccess: async () => { setRehireApprovalId(''); await refresh(); } });
  const createTenant = useMutation({
    mutationFn: () => api.createTenant({ name: tenantName, companyName }),
    onSuccess: async () => { setTenantName(''); setCompanyName(''); await refresh(); }
  });

  if (charter.isLoading || positions.isLoading || employees.isLoading || tenants.isLoading) return <LoadingState />;
  if (charter.error || positions.error || employees.error || tenants.error)
    return <ErrorState message={(charter.error ?? positions.error ?? employees.error ?? tenants.error)!.message} />;

  return (
    <Stack spacing={3}>
      <PageHeader title="Organization" description="Charter, positions, employees and tenant ownership." />
      <Grid columns={2}>
        <Card title="Company charter" action={<StatusChip label={`v${charter.data?.version ?? 1}`} tone="info" />}>
          <FormField label="Mission"><TextInput value={charterDraft?.mission ?? charter.data?.mission ?? ''} onChange={mission => setCharterDraft({ ...(charterDraft ?? charter.data!), mission })} multiline minRows={2} /></FormField>
          <FormField label="Constraints (comma separated)"><TextInput value={(charterDraft?.constraints ?? charter.data?.constraints ?? []).join(', ')} onChange={value => setCharterDraft({ ...(charterDraft ?? charter.data!), constraints: value.split(',').map(item => item.trim()).filter(Boolean) })} /></FormField>
          <FormField label="Monthly budget"><TextInput value={String(charterDraft?.monthlyBudgetMinor ?? charter.data?.monthlyBudgetMinor ?? 0)} onChange={value => setCharterDraft({ ...(charterDraft ?? charter.data!), monthlyBudgetMinor: Number(value) })} /></FormField>
          <FormField label="Currency"><TextInput value={charterDraft?.currency ?? charter.data?.currency ?? 'CNY'} onChange={currency => setCharterDraft({ ...(charterDraft ?? charter.data!), currency })} /></FormField>
          <Select label="Charter template" value={charterTemplate} onChange={setCharterTemplate} options={(charterTemplates.data ?? []).map(template => ({ value: template.id ?? '', label: template.name ?? '' }))} />
          <Stack direction="row"><Button tone="secondary" onClick={() => setCharterDraft(charterTemplates.data?.find(template => template.id === charterTemplate)?.charter ?? null)}>Apply template</Button><Button onClick={() => saveCharter.mutate()} loading={saveCharter.isPending}>Save charter</Button><Button tone="neutral" onClick={() => start.mutate()} loading={start.isPending}>Start or verify company</Button></Stack>
        </Card>
        <Card title="Create tenant">
          <FormField label="Tenant slug"><TextInput value={tenantName} onChange={setTenantName} placeholder="second-company" /></FormField>
          <FormField label="Company name"><TextInput value={companyName} onChange={setCompanyName} placeholder="Second Company" /></FormField>
          <Button onClick={() => createTenant.mutate()} disabled={!tenantName || !companyName} loading={createTenant.isPending}>Create tenant</Button>
          <DataTable rows={tenants.data ?? []} getRowId={row => row.id} columns={[
            { id: 'name', header: 'Tenant', render: row => row.name },
            { id: 'company', header: 'Company', render: row => row.companyName },
            { id: 'status', header: 'Status', render: row => row.status }
          ]} />
        </Card>
      </Grid>
      <Card title="Positions">
        <DataTable rows={positions.data ?? []} getRowId={row => row.id} columns={[
          { id: 'name', header: 'Position', render: row => row.name },
          { id: 'responsibilities', header: 'Responsibilities', render: row => row.responsibilities },
          { id: 'capabilities', header: 'Capabilities', render: row => row.capabilities.join(', ') },
          { id: 'status', header: 'Status', render: row => row.status },
          { id: 'actions', header: 'Actions', render: row => <Stack direction="row">
            <Button tone="neutral" onClick={() => setPositionStatus.mutate({ id: row.id, status: row.status === 'Frozen' ? 'Open' : 'Frozen' })}>{row.status === 'Frozen' ? 'Unfreeze' : 'Freeze'}</Button>
            <Button tone="danger" onClick={() => setPositionStatus.mutate({ id: row.id, status: 'Closed' })} disabled={row.status === 'Closed'}>Close</Button>
          </Stack> }
        ]} />
      </Card>
      <Card title="Position lifecycle">
        <Grid columns={2}>
          <Stack>
            <Text variant="h3">Merge positions</Text>
            <Select label="Source" value={mergeSource} onChange={setMergeSource} options={[{ value: '', label: 'Select source' }, ...(positions.data ?? []).map(position => ({ value: position.id, label: position.name }))]} />
            <Select label="Target" value={mergeTarget} onChange={setMergeTarget} options={[{ value: '', label: 'Select target' }, ...(positions.data ?? []).map(position => ({ value: position.id, label: position.name }))]} />
            <Button onClick={() => mergePositions.mutate()} disabled={!mergeSource || !mergeTarget || mergeSource === mergeTarget} loading={mergePositions.isPending}>Merge</Button>
          </Stack>
          <Stack>
            <Text variant="h3">Split position</Text>
            <Select label="Position" value={splitId} onChange={setSplitId} options={[{ value: '', label: 'Select position' }, ...(positions.data ?? []).map(position => ({ value: position.id, label: position.name }))]} />
            <FormField label="New name"><TextInput value={splitName} onChange={setSplitName} /></FormField>
            <FormField label="Responsibilities"><TextInput value={splitResponsibilities} onChange={setSplitResponsibilities} /></FormField>
            <Button tone="secondary" onClick={() => splitPosition.mutate()} disabled={!splitId || !splitName} loading={splitPosition.isPending}>Split</Button>
          </Stack>
        </Grid>
      </Card>
      <Card title="Rehire with Boss approval">
        <Stack spacing={2}>
          <FormField label="Terminated employee ID"><TextInput value={rehireEmployeeId} onChange={setRehireEmployeeId} /></FormField>
          <FormField label="Open position ID"><TextInput value={rehirePositionId} onChange={setRehirePositionId} /></FormField>
          <FormField label="Boss approval ID"><TextInput value={rehireApprovalId} onChange={setRehireApprovalId} /></FormField>
          <Button tone="secondary" disabled={!rehireEmployeeId || !rehirePositionId || !rehireApprovalId} loading={rehire.isPending} onClick={() => rehire.mutate()}>Rehire employee</Button>
          <Text variant="caption">Private memory remains isolated after rehire and is not restored automatically.</Text>
        </Stack>
      </Card>
      <Grid columns={2}>
        <Card title="Create position">
          <FormField label="Name"><TextInput value={positionName} onChange={setPositionName} placeholder="Product Lead" /></FormField>
          <FormField label="Responsibilities"><TextInput value={responsibilities} onChange={setResponsibilities} placeholder="Define product opportunities" /></FormField>
          <FormField label="Capabilities"><TextInput value={capabilities} onChange={setCapabilities} placeholder="product.define, research.execute" /></FormField>
          <FormField label="Budget (minor units)"><TextInput value={budget} onChange={setBudget} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <Button onClick={() => createPosition.mutate()} disabled={!positionName} loading={createPosition.isPending}>Create position</Button>
        </Card>
        <Card title="Recruitment pipeline">
          <FormField label="Candidate name"><TextInput value={candidateName} onChange={setCandidateName} placeholder="Candidate A" /></FormField>
          <FormField label="Position">
            <Select label="Position" value={candidatePosition} onChange={setCandidatePosition} options={[
              { value: '', label: 'Select position' },
              ...(positions.data ?? []).map(position => ({ value: position.id, label: position.name }))
            ]} />
          </FormField>
          <FormField label="Model version"><TextInput value={candidateModel} onChange={setCandidateModel} /></FormField>
          <FormField label="Job description"><TextInput multiline minRows={2} value={candidateJobDescription} onChange={setCandidateJobDescription} placeholder="Job-related responsibilities and required evidence" /></FormField>
          <Button onClick={() => createCandidate.mutate()} disabled={!candidateName || !candidatePosition || !candidateJobDescription} loading={createCandidate.isPending}>Create candidate</Button>
          <Text variant="caption">{employees.data?.length ?? 0} employees in the current tenant.</Text>
        </Card>
      </Grid>
    </Stack>
  );
}
