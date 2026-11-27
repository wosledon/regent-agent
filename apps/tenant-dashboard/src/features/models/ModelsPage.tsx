import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ConfirmDialog, ContentContainer, DetailList, FilterBar, FormField, Grid,
  MetricCard, PageHeader, SectionHeader, Select, Stack, StatusChip, Text, TextInput, createUuidV4
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { api } from '../../lib/api-client/api';
import {
  localizedCurrencyError, localizedNonNegativeNumberError, localizedPositiveIntegerError, localizedRequiredTextError
} from '../../lib/localizedValidation';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';

type ModelDraftField = 'inputPrice' | 'outputPrice' | 'currency' | 'tier' | 'protocol' | 'bindingVersion' | 'catalogVersion';
interface ModelDraftValue { source: string; value: string }
interface ModelContractTestProps { provider: string; modelId: string }
const modelProviders = ['Local', 'OpenAI', 'Anthropic'];

export function ModelsPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const pool = useQuery({ queryKey: ['model-pool'], queryFn: api.modelPool });
  const models = useMemo(() => pool.data?.catalog ?? [], [pool.data?.catalog]);
  const [requestedProvider, setRequestedProvider] = useState<string | null>(null);
  const [requestedModelId, setRequestedModelId] = useState('');
  const [employeeId, setEmployeeId] = useState('');
  const [reasoning, setReasoning] = useState('medium');
  const [contractTestRevision, setContractTestRevision] = useState(0);
  const [catalogModelId, setCatalogModelId] = useState('');
  const [deleteTarget, setDeleteTarget] = useState<{ provider: string; modelId: string } | null>(null);
  const [dirtyFields, setDirtyFields] = useState<Partial<Record<ModelDraftField, ModelDraftValue>>>({});
  const selected = requestedProvider === null
    ? models[0] ?? null
    : models.find(model => model.provider === requestedProvider && model.modelId === requestedModelId) ?? null;
  const provider = selected?.provider ?? requestedProvider ?? '';
  const modelId = selected?.modelId ?? requestedModelId;
  const selectedKey = selected ? `${selected.provider ?? ''}:${selected.modelId ?? ''}` : '';
  const contractTestKey = JSON.stringify([provider, modelId, selected?.version ?? null, selected !== null, contractTestRevision]);
  const draftValue = (field: ModelDraftField, serverValue: string) => dirtyFields[field]?.value ?? serverValue;
  const inputPrice = draftValue('inputPrice', selected?.inputPricePerMillion == null ? '' : String(selected.inputPricePerMillion));
  const outputPrice = draftValue('outputPrice', selected?.outputPricePerMillion == null ? '' : String(selected.outputPricePerMillion));
  const currency = draftValue('currency', selected?.currency ?? '');
  const tier = draftValue('tier', selected?.tier ?? 'Unassigned');
  const protocol = draftValue('protocol', selected?.protocol ?? 'Local');
  const bindingVersion = draftValue('bindingVersion', selected?.version ?? '');
  const catalogVersion = draftValue('catalogVersion', selected?.version ?? '');
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['model-pool'] });

  const markDirty = (field: ModelDraftField, value: string) => {
    setDirtyFields(current => ({ ...current, [field]: { source: selectedKey, value } }));
  };
  const selectModel = (nextProvider: string, nextModelId: string) => {
    setRequestedProvider(nextProvider);
    setRequestedModelId(nextModelId);
    setContractTestRevision(current => current + 1);
  };
  const hasForeignDraft = (fields: ModelDraftField[]) => fields.some(field => dirtyFields[field] && dirtyFields[field]!.source !== selectedKey);
  const clearDirtyFields = (...fields: ModelDraftField[]) => setDirtyFields(current => {
    const next = { ...current };
    for (const field of fields) delete next[field];
    return next;
  });
  const resetDraft = () => setDirtyFields({});

  const setPrice = useMutation({
    mutationFn: () => api.setManualPrice({
      id: createUuidV4(), provider, modelId, inputPricePerMillion: Number(inputPrice),
      outputPricePerMillion: Number(outputPrice), currency: currency.trim(), reason: 'Manual local configuration'
    }),
    onSuccess: async () => {
      clearDirtyFields('inputPrice', 'outputPrice', 'currency');
      await invalidate();
    }
  });
  const assignTier = useMutation({
    mutationFn: () => api.assignModelTier({ provider, modelId, tier, version: Number(bindingVersion) }),
    onSuccess: async () => {
      clearDirtyFields('tier');
      await invalidate();
    }
  });
  const bind = useMutation({
    mutationFn: () => api.bindModel({
      employeeId: employeeId.trim(), provider, modelId, version: bindingVersion, protocol, reasoningEffort: reasoning, status: 'Active'
    }),
    onSuccess: async () => {
      setEmployeeId('');
      clearDirtyFields('bindingVersion', 'protocol');
      await invalidate();
    }
  });
  const addModel = useMutation({
    mutationFn: () => api.addModel({
      provider, modelId: catalogModelId.trim(), version: catalogVersion, protocol,
      priceState: 'Unpriced', inputPricePerMillion: null, outputPricePerMillion: null,
      currency: currency.trim(), tier: 'Unassigned', source: 'manual-catalog'
    }),
    onSuccess: async () => {
      setCatalogModelId('');
      clearDirtyFields('catalogVersion', 'protocol', 'currency');
      await invalidate();
    }
  });
  const rotateModel = useMutation({
    mutationFn: ({ provider: modelProvider, modelId: id, version, protocol: modelProtocol }: { provider: string; modelId: string; version: string; protocol: string }) =>
      api.rotateModel(modelProvider, id, version, modelProtocol),
    onSuccess: async () => {
      clearDirtyFields('catalogVersion', 'protocol');
      setContractTestRevision(current => current + 1);
      await invalidate();
    }
  });
  const deleteModel = useMutation({
    mutationFn: ({ provider: modelProvider, modelId: id }: { provider: string; modelId: string }) =>
      api.deleteModel(modelProvider, id),
    onSuccess: async () => {
      setContractTestRevision(current => current + 1);
      await invalidate();
    }
  });
  const draftMutationPending = setPrice.isPending || assignTier.isPending || bind.isPending || addModel.isPending
    || rotateModel.isPending || deleteModel.isPending;

  if (pool.isLoading) return <LocalizedLoadingState />;
  if (pool.error) return <LocalizedErrorState error={pool.error} onRetry={() => void pool.refetch()} />;

  const priceError = !selected ? t('modelsPage.selectCatalogModel')
    : hasForeignDraft(['inputPrice', 'outputPrice', 'currency']) ? t('modelsPage.priceForeignDraft')
      : localizedNonNegativeNumberError(inputPrice, t('modelsPage.inputPriceMillion'), t)
        ?? localizedNonNegativeNumberError(outputPrice, t('modelsPage.outputPriceMillion'), t)
        ?? localizedCurrencyError(currency, t);
  const tierError = !selected ? t('modelsPage.selectCatalogModel')
    : hasForeignDraft(['tier', 'bindingVersion']) ? t('modelsPage.tierForeignDraft')
      : !['cheap', 'mid', 'premium'].includes(tier) ? t('modelsPage.selectTier')
        : localizedPositiveIntegerError(bindingVersion, t('common.version'), t);
  const bindingError = !selected ? t('modelsPage.selectCatalogModel')
    : hasForeignDraft(['bindingVersion', 'protocol']) ? t('modelsPage.bindingForeignDraft')
      : localizedRequiredTextError(employeeId, t('common.employeeID'), t)
        ?? localizedPositiveIntegerError(bindingVersion, t('common.version'), t);
  const addError = localizedRequiredTextError(catalogModelId, t('modelsPage.modelID'), t)
    ?? localizedPositiveIntegerError(catalogVersion, t('common.version'), t)
    ?? localizedCurrencyError(currency, t)
    ?? (!modelProviders.includes(protocol) ? t('modelsPage.supportedProtocol') : null)
    ?? (hasForeignDraft(['protocol', 'catalogVersion', 'currency']) ? t('modelsPage.catalogForeignDraft') : null);
  const rotationError = !selected ? t('modelsPage.selectCatalogModel')
    : hasForeignDraft(['protocol', 'catalogVersion']) ? t('modelsPage.rotationForeignDraft')
      : localizedPositiveIntegerError(catalogVersion, t('common.version'), t)
        ?? (!modelProviders.includes(protocol) ? t('modelsPage.supportedProtocol') : null);
  const foreignDraft = hasForeignDraft(['inputPrice', 'outputPrice', 'currency', 'tier', 'protocol', 'bindingVersion', 'catalogVersion']);
  const providerOptions = models.filter(model => model.provider === provider).map(model => ({ value: model.modelId ?? '', label: model.modelId ?? '' }));
  const hasSelectedDraft = (fields: ModelDraftField[]) => fields.some(field => dirtyFields[field]?.source === selectedKey);
  const priceDirty = hasSelectedDraft(['inputPrice', 'outputPrice', 'currency']);
  const tierDirty = hasSelectedDraft(['tier', 'bindingVersion']);
  const bindingDirty = hasSelectedDraft(['bindingVersion', 'protocol']);
  const rotationDirty = hasSelectedDraft(['protocol', 'catalogVersion']);
  const selectedDirty = priceDirty || tierDirty || bindingDirty || rotationDirty;
  const bindings = pool.data?.bindings ?? [];
  const unavailableEvents = pool.data?.unavailableEvents ?? [];
  const unpricedCount = models.filter(model => model.priceState?.toLowerCase() === 'unpriced').length;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('modelsPage.models')}
          description={t('modelsPage.localCatalogSnapshotsManualPricingTieringAndImmutableEmployeeBindings')}
          meta={<StatusChip label={t('modelsPage.modelCatalog')} tone="info" />}
        />
        <Grid columns={4}>
          <MetricCard label={t('modelsPage.modelCatalog')} value={format.number(models.length)} icon="model" />
          <MetricCard label={t('modelsPage.employeeBindings')} value={format.number(bindings.length)} icon="people" />
          <MetricCard
            label={t('modelsPage.unavailableModelEvents')}
            value={format.number(unavailableEvents.length)}
            tone={unavailableEvents.length ? 'warning' : 'default'}
            icon="warning"
          />
          <MetricCard
            label={domainLabel('Unpriced')}
            value={format.number(unpricedCount)}
            tone={unpricedCount ? 'warning' : 'default'}
            icon="billing"
          />
        </Grid>

        <Stack direction="row" spacing={3} wrap alignItems="flex-start">
          <Stack sx={{ flex: '1 1 580px', minWidth: 0 }}>
            <SectionHeader title={t('modelsPage.modelCatalog')} />
            <Card padding="small">
              <FilterBar>
                <Stack sx={{ flex: '1 1 220px' }}>
                  <Select
                    label={t('common.provider')}
                    value={provider}
                    disabled={draftMutationPending}
                    onChange={value => selectModel(value, models.find(model => model.provider === value)?.modelId ?? '')}
                    options={[{ value: '', label: t('modelsPage.selectProvider') }, ...modelProviders.map(value => ({ value, label: domainLabel(value) }))]}
                  />
                </Stack>
                <Stack sx={{ flex: '1 1 240px' }}>
                  <Select
                    label={t('common.model')}
                    value={modelId}
                    disabled={draftMutationPending}
                    onChange={value => selectModel(provider, value)}
                    options={[{ value: '', label: t('modelsPage.selectModel') }, ...providerOptions]}
                  />
                </Stack>
              </FilterBar>
              <LocalizedDataTable
                rows={models}
                getRowId={row => `${row.provider}:${row.modelId}`}
                density="compact"
                minWidth={760}
                columns={[
                  { id: 'provider', header: t('common.provider'), render: row => domainLabel(row.provider) },
                  { id: 'model', header: t('common.model'), render: row => <Text variant="mono">{row.modelId ?? ''}</Text> },
                  { id: 'tier', header: t('modelsPage.tier'), render: row => <StatusChip label={domainLabel(row.tier)} tone="info" /> },
                  { id: 'price', header: t('modelsPage.priceState'), render: row => t('modelsPage.priceStateValue', { state: domainLabel(row.priceState), currency: row.currency ?? '' }) },
                  { id: 'actions', header: t('common.actions'), render: row => {
                    const modelProvider = row.provider ?? '';
                    const id = row.modelId ?? '';
                    const rowSelected = `${modelProvider}:${id}` === selectedKey;
                    return <Stack direction="row" spacing={1} wrap>
                      <Button size="small" tone={rowSelected ? 'secondary' : 'neutral'} disabled={!modelProvider || !id || draftMutationPending} onClick={() => selectModel(modelProvider, id)}>
                        {t('modelsPage.selectModel')}
                      </Button>
                      <Button size="small" tone="danger" disabled={!modelProvider || !id || draftMutationPending} onClick={() => setDeleteTarget({ provider: modelProvider, modelId: id })}>
                        {t('common.delete')}
                      </Button>
                    </Stack>;
                  } }
                ]}
              />
              <LocalizedMutationFeedback mutation={deleteModel} successMessage={t('modelsPage.modelDeleted')} />
            </Card>

            <Card title={t('modelsPage.addCatalogModel')} padding="small">
              <Grid columns={2}>
                <Select
                  label={t('common.provider')}
                  value={provider}
                  disabled={draftMutationPending}
                  onChange={value => selectModel(value, models.find(model => model.provider === value)?.modelId ?? '')}
                  options={[{ value: '', label: t('modelsPage.selectProvider') }, ...modelProviders.map(value => ({ value, label: domainLabel(value) }))]}
                />
                <FormField label={t('modelsPage.modelID')}>
                  <TextInput value={catalogModelId} disabled={draftMutationPending} onChange={setCatalogModelId} />
                </FormField>
                <FormField label={t('common.version')}>
                  <TextInput value={catalogVersion} disabled={draftMutationPending} onChange={value => markDirty('catalogVersion', value)} />
                </FormField>
                <Select
                  label={t('modelsPage.protocol')}
                  value={protocol}
                  disabled={draftMutationPending}
                  onChange={value => markDirty('protocol', value)}
                  options={modelProviders.map(value => ({ value, label: domainLabel(value) }))}
                />
              </Grid>
              <Text variant="caption">{t('modelsPage.versionAndProtocolAlsoConfigureRotationForTheSelectedCatalogModel')}</Text>
              <Stack direction="row" spacing={1} wrap>
                <Button onClick={() => addModel.mutate()} disabled={!!addError || draftMutationPending} loading={addModel.isPending}>
                  {t('modelsPage.addModel')}
                </Button>
              </Stack>
              {addError ? <Text color="error">{addError}</Text> : null}
              <LocalizedMutationFeedback mutation={addModel} successMessage={t('modelsPage.catalogModelAdded')} />
            </Card>
          </Stack>

          <Stack sx={{ flex: '1 1 400px', minWidth: 0 }}>
            <Card
              title={selected ? `${domainLabel(selected.provider)} / ${selected.modelId ?? ''}` : t('modelsPage.priceAndTier')}
              padding="small"
              action={selectedDirty
                ? <StatusChip label={t('common.domain.draft')} tone="warning" />
                : selected ? <StatusChip label={domainLabel(selected.priceState)} tone={selected.priceState?.toLowerCase() === 'unpriced' ? 'warning' : 'success'} /> : undefined}
            >
              {selected ? (
                <DetailList
                  dense
                  columns={2}
                  items={[
                    { label: t('common.provider'), value: domainLabel(selected.provider) },
                    { label: t('common.model'), value: selected.modelId ?? '', mono: true },
                    { label: t('modelsPage.protocol'), value: domainLabel(selected.protocol) },
                    { label: t('common.version'), value: selected.version ?? '' },
                    { label: t('modelsPage.inputPriceMillion'), value: selected.inputPricePerMillion == null ? '—' : `${format.number(toFiniteNumber(selected.inputPricePerMillion))} ${selected.currency ?? ''}` },
                    { label: t('modelsPage.outputPriceMillion'), value: selected.outputPricePerMillion == null ? '—' : `${format.number(toFiniteNumber(selected.outputPricePerMillion))} ${selected.currency ?? ''}` },
                    { label: t('common.currency'), value: selected.currency ?? '' },
                    { label: t('modelsPage.tier'), value: domainLabel(selected.tier) }
                  ]}
                />
              ) : (
                <Text variant="secondary">{t('modelsPage.selectCatalogModel')}</Text>
              )}

              <SectionHeader title={t('modelsPage.priceAndTier')} action={priceDirty ? <StatusChip label={t('common.domain.draft')} tone="warning" /> : undefined} />
              <Grid columns={2}>
                <FormField label={t('modelsPage.inputPriceMillion')}>
                  <TextInput value={inputPrice} disabled={draftMutationPending} onChange={value => markDirty('inputPrice', value)} inputProps={{ inputMode: 'decimal' }} />
                </FormField>
                <FormField label={t('modelsPage.outputPriceMillion')}>
                  <TextInput value={outputPrice} disabled={draftMutationPending} onChange={value => markDirty('outputPrice', value)} inputProps={{ inputMode: 'decimal' }} />
                </FormField>
                <FormField label={t('common.currency')}>
                  <TextInput value={currency} disabled={draftMutationPending} onChange={value => markDirty('currency', value)} />
                </FormField>
              </Grid>
              <Stack direction="row" spacing={1} wrap>
                <Button onClick={() => setPrice.mutate()} disabled={!!priceError || draftMutationPending} loading={setPrice.isPending}>
                  {t('modelsPage.saveManualPrice')}
                </Button>
                <Button tone="neutral" onClick={resetDraft} disabled={!Object.keys(dirtyFields).length || draftMutationPending}>
                  {t('modelsPage.resetDraftToSelectedModel')}
                </Button>
              </Stack>
              {priceError ? <Text color="error">{priceError}</Text> : null}
              <LocalizedMutationFeedback mutation={setPrice} successMessage={t('modelsPage.manualPriceSaved')} />

              <Select
                label={t('modelsPage.tier')}
                value={tier}
                disabled={draftMutationPending}
                onChange={value => markDirty('tier', value)}
                options={['Unassigned', 'cheap', 'mid', 'premium'].map(value => ({ value, label: domainLabel(value) }))}
              />
              <Stack direction="row" spacing={1} wrap alignItems="center">
                <Button tone="secondary" onClick={() => assignTier.mutate()} disabled={!!tierError || draftMutationPending} loading={assignTier.isPending}>
                  {t('modelsPage.assignTier')}
                </Button>
                {tierDirty ? <StatusChip label={t('common.domain.draft')} tone="warning" /> : null}
              </Stack>
              {tierError ? <Text color="error">{tierError}</Text> : null}
              <LocalizedMutationFeedback mutation={assignTier} successMessage={t('modelsPage.modelTierSaved')} />

              <SectionHeader title={t('modelsPage.rotate')} action={rotationDirty ? <StatusChip label={t('common.domain.draft')} tone="warning" /> : undefined} />
              <Grid columns={2}>
                <FormField label={t('common.version')}>
                  <TextInput value={catalogVersion} disabled={draftMutationPending} onChange={value => markDirty('catalogVersion', value)} />
                </FormField>
                <Select
                  label={t('modelsPage.protocol')}
                  value={protocol}
                  disabled={draftMutationPending}
                  onChange={value => markDirty('protocol', value)}
                  options={modelProviders.map(value => ({ value, label: domainLabel(value) }))}
                />
              </Grid>
              <Button tone="secondary" onClick={() => selected && rotateModel.mutate({ provider, modelId, version: catalogVersion, protocol })} disabled={!selected || !!rotationError || draftMutationPending} loading={rotateModel.isPending}>
                {t('modelsPage.rotate')}
              </Button>
              {rotationError ? <Text color="error">{rotationError}</Text> : null}
              <LocalizedMutationFeedback mutation={rotateModel} successMessage={t('modelsPage.modelRotationSaved')} />
            </Card>

            <Card title={t('modelsPage.employeeBinding')} padding="small" action={bindingDirty ? <StatusChip label={t('common.domain.draft')} tone="warning" /> : undefined}>
              <Grid columns={2}>
                <FormField label={t('common.employeeID')}>
                  <TextInput value={employeeId} disabled={draftMutationPending} onChange={setEmployeeId} placeholder={t('modelsPage.employeeGUID')} />
                </FormField>
                <FormField label={t('common.version')}>
                  <TextInput value={bindingVersion} disabled={draftMutationPending} onChange={value => markDirty('bindingVersion', value)} />
                </FormField>
                <Select
                  label={t('modelsPage.protocol')}
                  value={protocol}
                  disabled={draftMutationPending}
                  onChange={value => markDirty('protocol', value)}
                  options={modelProviders.map(value => ({ value, label: domainLabel(value) }))}
                />
                <Select
                  label={t('modelsPage.reasoningEffort')}
                  value={reasoning}
                  disabled={draftMutationPending}
                  onChange={setReasoning}
                  options={['low', 'medium', 'high'].map(value => ({ value, label: domainLabel(value) }))}
                />
              </Grid>
              <Button onClick={() => bind.mutate()} disabled={!!bindingError || draftMutationPending} loading={bind.isPending}>
                {t('modelsPage.bindEmployee')}
              </Button>
              {bindingError ? <Text color="error">{bindingError}</Text> : null}
              <LocalizedMutationFeedback mutation={bind} successMessage={t('modelsPage.employeeBindingSaved')} />
              {selected ? <ModelContractTest key={contractTestKey} provider={provider} modelId={modelId} /> : null}
            </Card>
          </Stack>
        </Stack>

        {foreignDraft ? <Text color="error">{t('modelsPage.someDraftFieldsStillContainEditsFromAnotherModelTheyArePreservedButRelat')}</Text> : null}
        <Grid columns={2}>
          <Card title={t('modelsPage.employeeBindings')} padding="small">
            <LocalizedDataTable
              rows={bindings}
              getRowId={row => row.employeeId ?? ''}
              density="compact"
              columns={[
                { id: 'employee', header: t('modelsPage.employee'), render: row => <Text variant="mono">{row.employeeId ?? ''}</Text> },
                { id: 'model', header: t('common.model'), render: row => `${row.provider ?? ''}/${row.modelId ?? ''}` },
                { id: 'version', header: t('common.version'), render: row => row.version ?? '', numeric: true },
                { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone="neutral" /> }
              ]}
            />
          </Card>
          <Card title={t('modelsPage.unavailableModelEvents')} padding="small">
            <LocalizedDataTable
              rows={unavailableEvents}
              getRowId={row => row.id ?? ''}
              density="compact"
              columns={[
                { id: 'model', header: t('common.model'), render: row => `${row.provider ?? ''}/${row.modelId ?? ''}` },
                { id: 'failures', header: t('modelsPage.failures'), render: row => format.number(toFiniteNumber(row.consecutiveFailures)), numeric: true },
                { id: 'escalation', header: t('modelsPage.escalation'), render: row => domainLabel(row.escalation) },
                { id: 'detected', header: t('modelsPage.detected'), render: row => format.dateTime(row.detectedAt) }
              ]}
            />
          </Card>
        </Grid>

        <ConfirmDialog
          open={deleteTarget !== null}
          title={t('common.delete')}
          description={deleteTarget ? t('modelsPage.confirmDelete', deleteTarget) : undefined}
          confirmLabel={t('common.delete')}
          cancelLabel={t('common.no')}
          tone="danger"
          loading={deleteModel.isPending}
          requireText={deleteTarget ? `${deleteTarget.provider}/${deleteTarget.modelId}` : undefined}
          onConfirm={() => {
            if (!deleteTarget) return;
            deleteModel.mutate(deleteTarget);
            setDeleteTarget(null);
          }}
          onClose={() => {
            if (!deleteModel.isPending) setDeleteTarget(null);
          }}
        />
      </Stack>
    </ContentContainer>
  );
}

function ModelContractTest({ provider, modelId }: ModelContractTestProps) {
  const { t } = useTranslation();
  const test = useMutation({ mutationFn: () => api.testModel(provider, modelId) });
  return <>
    <Button tone="neutral" onClick={() => test.mutate()} disabled={test.isPending} loading={test.isPending}>{t('modelsPage.testLocalContract')}</Button>
    {test.isSuccess && test.data.provider === provider && test.data.modelId === modelId
      ? <StatusChip label={t(test.data.supported ? 'modelsPage.contractSupported' : 'modelsPage.contractUnsupported')} tone={test.data.supported ? 'success' : 'error'} />
      : null}
    <LocalizedMutationFeedback mutation={test} showSuccess={false} />
  </>;
}
