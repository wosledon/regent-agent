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
  const [tenantName, setTenantName] = useState('');
  const [companyName, setCompanyName] = useState('');

  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['charter'] }),
    queryClient.invalidateQueries({ queryKey: ['positions'] }),
    queryClient.invalidateQueries({ queryKey: ['employees'] }),
    queryClient.invalidateQueries({ queryKey: ['tenants'] })
  ]);
  const start = useMutation({ mutationFn: api.startCompany, onSuccess: refresh });
  const createPosition = useMutation({
    mutationFn: () => api.createPosition({
      id: crypto.randomUUID(), name: positionName, responsibilities,
      capabilities: capabilities.split(',').map(value => value.trim()).filter(Boolean),
      budgetMinor: Number(budget), parentPositionId: null, status: 'Open', version: 1
    }),
    onSuccess: async () => { setPositionName(''); await refresh(); }
  });
  const createCandidate = useMutation({
    mutationFn: () => api.createCandidate({ positionId: candidatePosition, displayName: candidateName, modelVersion: candidateModel }),
    onSuccess: async () => { setCandidateName(''); await refresh(); }
  });
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
          <Text variant="h3">{charter.data?.mission}</Text>
          <Text>{charter.data?.currency} {Number(charter.data?.monthlyBudgetMinor ?? 0).toLocaleString()}</Text>
          <Text variant="caption">{charter.data?.constraints.join(' · ')}</Text>
          <Button onClick={() => start.mutate()} loading={start.isPending}>Start or verify company</Button>
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
          { id: 'status', header: 'Status', render: row => row.status }
        ]} />
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
          <Button onClick={() => createCandidate.mutate()} disabled={!candidateName || !candidatePosition} loading={createCandidate.isPending}>Create candidate</Button>
          <Text variant="caption">{employees.data?.length ?? 0} employees in the current tenant.</Text>
        </Card>
      </Grid>
    </Stack>
  );
}
