import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState,
  PageHeader, Select, Stack, StatusChip, TextInput
} from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function ModelsPage() {
  const queryClient = useQueryClient();
  const pool = useQuery({ queryKey: ['model-pool'], queryFn: api.modelPool });
  const [provider, setProvider] = useState('Local');
  const [modelId, setModelId] = useState('local-cheap');
  const [inputPrice, setInputPrice] = useState('0');
  const [outputPrice, setOutputPrice] = useState('0');
  const [currency, setCurrency] = useState('CNY');
  const [tier, setTier] = useState('cheap');
  const [employeeId, setEmployeeId] = useState('');
  const [bindingVersion, setBindingVersion] = useState('1');
  const [protocol, setProtocol] = useState('Local');
  const [reasoning, setReasoning] = useState('medium');
  const [testResult, setTestResult] = useState<Awaited<ReturnType<typeof api.testModel>> | null>(null);
  const [catalogModelId, setCatalogModelId] = useState('custom-model');
  const [catalogVersion, setCatalogVersion] = useState('1');
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['model-pool'] });
  const setPrice = useMutation({
    mutationFn: () => api.setManualPrice({
      id: crypto.randomUUID(), provider, modelId, inputPricePerMillion: Number(inputPrice),
      outputPricePerMillion: Number(outputPrice), currency, reason: 'Manual local configuration'
    }), onSuccess: invalidate
  });
  const assignTier = useMutation({ mutationFn: () => api.assignModelTier({ provider, modelId, tier, version: 1 }), onSuccess: invalidate });
  const bind = useMutation({
    mutationFn: () => api.bindModel({
      employeeId, provider, modelId, version: bindingVersion, protocol, reasoningEffort: reasoning, status: 'Active'
    }), onSuccess: async () => { setEmployeeId(''); await invalidate(); }
  });
  const test = useMutation({ mutationFn: () => api.testModel(provider, modelId), onSuccess: setTestResult });
  const addModel = useMutation({
    mutationFn: () => api.addModel({
      provider, modelId: catalogModelId, version: catalogVersion, protocol,
      priceState: 'Unpriced', inputPricePerMillion: null, outputPricePerMillion: null,
      currency, tier: 'Unassigned', source: 'manual-catalog'
    }), onSuccess: invalidate
  });
  const rotateModel = useMutation({
    mutationFn: ({ provider: modelProvider, modelId: id }: { provider: string; modelId: string }) =>
      api.rotateModel(modelProvider, id, catalogVersion, protocol), onSuccess: invalidate
  });
  const deleteModel = useMutation({
    mutationFn: ({ provider: modelProvider, modelId: id }: { provider: string; modelId: string }) =>
      api.deleteModel(modelProvider, id), onSuccess: invalidate
  });

  if (pool.isLoading) return <LoadingState />;
  if (pool.error) return <ErrorState message={pool.error.message} />;
  const state = pool.data;
  const models = state?.catalog ?? [];
  const selected = models.find(model => model.provider === provider && model.modelId === modelId);

  return (
    <Stack spacing={3}>
      <PageHeader title="Models" description="Local catalog snapshots, manual pricing, tiering and immutable employee bindings." />
      <Card title="Model catalog">
        <DataTable rows={models} getRowId={row => `${row.provider}:${row.modelId}`} columns={[
          { id: 'provider', header: 'Provider', render: row => row.provider ?? '' },
          { id: 'model', header: 'Model', render: row => row.modelId ?? '' },
          { id: 'protocol', header: 'Protocol', render: row => row.protocol ?? '' },
          { id: 'tier', header: 'Tier', render: row => <StatusChip label={row.tier ?? 'Unassigned'} tone="info" /> },
          { id: 'price', header: 'Price state', render: row => `${row.priceState ?? 'Unknown'} ${row.currency ?? ''}` },
          { id: 'actions', header: 'Actions', render: row => <Stack direction="row" wrap>
            <Button tone="secondary" onClick={() => rotateModel.mutate({ provider: row.provider ?? '', modelId: row.modelId ?? '' })}>Rotate</Button>
            <Button tone="danger" onClick={() => deleteModel.mutate({ provider: row.provider ?? '', modelId: row.modelId ?? '' })}>Delete</Button>
          </Stack> }
        ]} />
      </Card>
      <Card title="Add catalog model">
        <Stack spacing={2}>
          <Select label="Provider" value={provider} onChange={setProvider} options={['Local', 'OpenAI', 'Anthropic'].map(value => ({ value, label: value }))} />
          <FormField label="Model ID"><TextInput value={catalogModelId} onChange={setCatalogModelId} /></FormField>
          <FormField label="Version"><TextInput value={catalogVersion} onChange={setCatalogVersion} /></FormField>
          <Select label="Protocol" value={protocol} onChange={setProtocol} options={['Local', 'OpenAI', 'Anthropic'].map(value => ({ value, label: value }))} />
          <Button onClick={() => addModel.mutate()} disabled={!catalogModelId || !catalogVersion} loading={addModel.isPending}>Add model</Button>
        </Stack>
      </Card>
      <Grid columns={2}>
        <Card title="Price and tier">
          <Select label="Provider" value={provider} onChange={value => { setProvider(value); setProtocol(value); }} options={['Local', 'OpenAI', 'Anthropic'].map(value => ({ value, label: value }))} />
          <Select label="Model" value={modelId} onChange={setModelId} options={models.filter(model => model.provider === provider).map(model => ({ value: model.modelId ?? '', label: model.modelId ?? '' }))} />
          <FormField label="Input price / million"><TextInput value={inputPrice} onChange={setInputPrice} inputProps={{ inputMode: 'decimal' }} /></FormField>
          <FormField label="Output price / million"><TextInput value={outputPrice} onChange={setOutputPrice} inputProps={{ inputMode: 'decimal' }} /></FormField>
          <FormField label="Currency"><TextInput value={currency} onChange={setCurrency} /></FormField>
          <Button onClick={() => setPrice.mutate()} loading={setPrice.isPending}>Save manual price</Button>
          <Select label="Tier" value={tier} onChange={setTier} options={['cheap', 'mid', 'premium'].map(value => ({ value, label: value }))} />
          <Button tone="secondary" onClick={() => assignTier.mutate()} loading={assignTier.isPending}>Assign tier</Button>
        </Card>
        <Card title="Employee binding">
          <FormField label="Employee ID"><TextInput value={employeeId} onChange={setEmployeeId} placeholder="Employee GUID" /></FormField>
          <Select label="Provider" value={provider} onChange={setProvider} options={['Local', 'OpenAI', 'Anthropic'].map(value => ({ value, label: value }))} />
          <Select label="Model" value={modelId} onChange={setModelId} options={models.filter(model => model.provider === provider).map(model => ({ value: model.modelId ?? '', label: model.modelId ?? '' }))} />
          <FormField label="Version"><TextInput value={bindingVersion} onChange={setBindingVersion} /></FormField>
          <Select label="Protocol" value={protocol} onChange={setProtocol} options={['Local', 'OpenAI', 'Anthropic'].map(value => ({ value, label: value }))} />
          <Select label="Reasoning effort" value={reasoning} onChange={setReasoning} options={['low', 'medium', 'high'].map(value => ({ value, label: value }))} />
          <Button onClick={() => bind.mutate()} disabled={!employeeId || !selected} loading={bind.isPending}>Bind employee</Button>
          <Button tone="neutral" onClick={() => test.mutate()} loading={test.isPending}>Test local contract</Button>
          {testResult ? <StatusChip label={testResult.message ?? ''} tone={testResult.supported ? 'success' : 'error'} /> : null}
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card title="Employee bindings">
          <DataTable rows={state?.bindings ?? []} getRowId={row => row.employeeId ?? ''} columns={[
            { id: 'employee', header: 'Employee', render: row => row.employeeId ?? '' },
            { id: 'model', header: 'Model', render: row => `${row.provider ?? ''}/${row.modelId ?? ''}` },
            { id: 'version', header: 'Version', render: row => row.version ?? '' },
            { id: 'status', header: 'Status', render: row => row.status ?? '' }
          ]} />
        </Card>
        <Card title="Unavailable model events">
          <DataTable rows={state?.unavailableEvents ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'model', header: 'Model', render: row => `${row.provider ?? ''}/${row.modelId ?? ''}` },
            { id: 'failures', header: 'Failures', render: row => row.consecutiveFailures ?? 0 },
            { id: 'escalation', header: 'Escalation', render: row => row.escalation ?? '' },
            { id: 'detected', header: 'Detected', render: row => row.detectedAt ? new Date(row.detectedAt).toLocaleString() : '' }
          ]} />
        </Card>
      </Grid>
    </Stack>
  );
}
