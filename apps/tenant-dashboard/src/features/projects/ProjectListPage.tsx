import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, EmptyState, ErrorState, FormField, FreshnessBadge, Grid, LoadingState, PageHeader, SemanticLink, Stack, StatusChip, Text, TextInput, type ChipTone } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

const healthTone: Record<string, ChipTone> = {
  on_track: 'success', at_risk: 'warning', blocked: 'error', pending_acceptance: 'info', live: 'success', failed: 'error'
};

export function ProjectListPage() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['projects'], queryFn: api.projects });
  const [name, setName] = useState('');
  const [owner, setOwner] = useState('Controller');
  const [mission, setMission] = useState('');
  const [targetDays, setTargetDays] = useState('14');
  const [budgetMinor, setBudgetMinor] = useState('500000');
  const [currency, setCurrency] = useState('CNY');
  const create = useMutation({
    mutationFn: () => api.createProject({ name, owner, mission, targetDays: Number(targetDays), budgetMinor: Number(budgetMinor), currency }),
    onSuccess: async () => { setName(''); setMission(''); await queryClient.invalidateQueries({ queryKey: ['projects'] }); }
  });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  return (
    <Stack spacing={3}>
      <PageHeader title="Projects" description="Deterministic health, delivery freshness and evidence-gated progress." />
      <Card title="Create project">
        <FormField label="Name"><TextInput value={name} onChange={setName} /></FormField>
        <FormField label="Owner"><TextInput value={owner} onChange={setOwner} /></FormField>
        <FormField label="Mission"><TextInput value={mission} onChange={setMission} multiline minRows={2} /></FormField>
        <FormField label="Target days"><TextInput value={targetDays} onChange={setTargetDays} /></FormField>
        <FormField label="Budget (minor units)"><TextInput value={budgetMinor} onChange={setBudgetMinor} /></FormField>
        <FormField label="Currency"><TextInput value={currency} onChange={setCurrency} /></FormField>
        <Button onClick={() => create.mutate()} disabled={!name || !owner || !mission || Number(targetDays) <= 0} loading={create.isPending}>Create project</Button>
      </Card>
      {!data?.length ? <EmptyState title="No projects" description="Create a project from the company charter to begin delivery." /> : null}
      <Grid columns={2}>
        {data?.map(project => (
          <Card key={project.id} title={project.name} action={<FreshnessBadge freshness={project.freshness} />}>
            <StatusChip tone={healthTone[project.health] ?? 'neutral'} label={project.health} />
            <Text>Owner: {project.owner}</Text>
            <Text variant="caption">Target: {new Date(project.targetAt).toLocaleDateString()}</Text>
            <Text>{project.currency} {project.spentMinor.toLocaleString()} / {project.budgetMinor.toLocaleString()}</Text>
            <SemanticLink href={`/projects/${project.id}`}>Open project</SemanticLink>
          </Card>
        ))}
      </Grid>
    </Stack>
  );
}
