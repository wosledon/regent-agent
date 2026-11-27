import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ContentContainer, DetailList, EmptyState, FormField, Grid,
  MetricCard, PageHeader, Select, Stack, StatusChip, Text, TextInput, createUuidV4
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import type { components } from '../../lib/api-client/schema';
import {
  localizedCurrencyError, localizedIntegerRangeError, localizedNonNegativeNumberError,
  localizedPositiveIntegerError, localizedRequiredTextError, localizedTimeError, localizedTimeZoneError
} from '../../lib/localizedValidation';
import { stableOccurrenceKeys } from '../../lib/stableKeys';
import { toFiniteNumber } from '../../lib/formatting';
import { localizedCount } from '../../lib/localizedCount';

type WorkSchedule = components['schemas']['WorkSchedule'];
type WorkWindow = components['schemas']['WorkWindow'];
type CapacityPolicy = components['schemas']['CapacityPolicy'];

const emptyWindow: WorkWindow = { days: '', startLocal: '', endLocal: '', kind: 'Silent' };
type PolicyVersion = WorkSchedule['version'];

function numericPolicyVersion(value: PolicyVersion): number | null {
  const normalized = typeof value === 'string' && /^\d+$/.test(value.trim()) ? Number(value.trim()) : value;
  return typeof normalized === 'number' && Number.isSafeInteger(normalized) && normalized >= 0 ? normalized : null;
}

function shouldHydrateVersion(candidate: PolicyVersion, boundary: { value: PolicyVersion } | null): boolean {
  if (!boundary) return true;
  const candidateVersion = numericPolicyVersion(candidate);
  const boundaryVersion = numericPolicyVersion(boundary.value);
  return candidateVersion !== null && boundaryVersion !== null && candidateVersion > boundaryVersion;
}

function savedVersionBoundary(current: { value: PolicyVersion } | null, saved: PolicyVersion): { value: PolicyVersion } {
  if (!current) return { value: saved };
  const currentVersion = numericPolicyVersion(current.value);
  const savedVersion = numericPolicyVersion(saved);
  return currentVersion !== null && (savedVersion === null || savedVersion < currentVersion) ? current : { value: saved };
}

function isOlderVersion(candidate: PolicyVersion, boundary: { value: PolicyVersion } | null): boolean {
  if (!boundary) return false;
  const candidateVersion = numericPolicyVersion(candidate);
  const boundaryVersion = numericPolicyVersion(boundary.value);
  return candidateVersion !== null && boundaryVersion !== null && candidateVersion < boundaryVersion;
}

export function RuntimePolicyPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const policy = useQuery({ queryKey: ['runtime-policy'], queryFn: api.runtimePolicy });
  const summary = useQuery({ queryKey: ['cost-summary'], queryFn: () => api.costSummary() });
  const [projectId, setProjectId] = useState('');
  const [softLimit, setSoftLimit] = useState('1000');
  const [hardLimit, setHardLimit] = useState('1200');
  const [currency, setCurrency] = useState('CNY');
  const [sourceEventId, setSourceEventId] = useState('');
  const [amount, setAmount] = useState('10');
  const [category, setCategory] = useState('Model');
  const [scheduleDraft, setScheduleDraft] = useState<WorkSchedule | null>(null);
  const [capacityDraft, setCapacityDraft] = useState<CapacityPolicy | null>(null);
  const [scheduleDirty, setScheduleDirty] = useState(false);
  const [capacityDirty, setCapacityDirty] = useState(false);
  const scheduleVersionRef = useRef<{ value: WorkSchedule['version'] } | null>(null);
  const capacityVersionRef = useRef<{ value: CapacityPolicy['version'] } | null>(null);
  const invalidationRequests = () => [
    queryClient.invalidateQueries({ queryKey: ['runtime-policy'] }),
    queryClient.invalidateQueries({ queryKey: ['cost-summary'] })
  ];
  const invalidate = () => Promise.all(invalidationRequests());
  const invalidateInBackground = () => { void Promise.allSettled(invalidationRequests()); };

  useEffect(() => {
    const schedule = policy.data?.schedule;
    if (!schedule || scheduleDirty) return;
    const hydrateTimer = setTimeout(() => {
      if (!shouldHydrateVersion(schedule.version, scheduleVersionRef.current)) return;
      scheduleVersionRef.current = { value: schedule.version };
      setScheduleDraft(schedule);
    }, 0);
    return () => clearTimeout(hydrateTimer);
  }, [policy.data?.schedule, scheduleDirty]);
  useEffect(() => {
    const capacity = policy.data?.capacity;
    if (!capacity || capacityDirty) return;
    const hydrateTimer = setTimeout(() => {
      if (!shouldHydrateVersion(capacity.version, capacityVersionRef.current)) return;
      capacityVersionRef.current = { value: capacity.version };
      setCapacityDraft(capacity);
    }, 0);
    return () => clearTimeout(hydrateTimer);
  }, [policy.data?.capacity, capacityDirty]);

  const setBudget = useMutation({
    mutationFn: () => api.setBudget({ projectId: projectId.trim(), softLimit: Number(softLimit), hardLimit: Number(hardLimit), currency: currency.trim(), carryForward: false }),
    onSuccess: async () => { setProjectId(''); await invalidate(); }
  });
  const recordCost = useMutation({
    mutationFn: () => api.recordCost({
      id: createUuidV4(), sourceEventId: sourceEventId.trim(), taskId: null, projectId: projectId.trim() || null,
      employeeId: null, model: 'local-cheap', amount: Number(amount), currency: currency.trim(), category,
      allocationStatus: 'Direct', correctionOf: null, reason: null, createdAt: new Date().toISOString()
    }), onSuccess: async () => { setSourceEventId(''); await invalidate(); }
  });
  const updateSchedule = useMutation({
    mutationFn: () => {
      if (!scheduleDraft) throw new Error(t('runtimePolicyPage.scheduleRequired'));
      return api.updateSchedule(scheduleDraft);
    },
    onSuccess: result => {
      if (!isOlderVersion(result.version, scheduleVersionRef.current)) setScheduleDraft(result);
      scheduleVersionRef.current = savedVersionBoundary(scheduleVersionRef.current, result.version);
      setScheduleDirty(false);
      invalidateInBackground();
    }
  });
  const updateCapacity = useMutation({
    mutationFn: () => {
      if (!capacityDraft) throw new Error(t('runtimePolicyPage.capacityRequired'));
      return api.updateCapacity(capacityDraft);
    },
    onSuccess: result => {
      if (!isOlderVersion(result.version, capacityVersionRef.current)) setCapacityDraft(result);
      capacityVersionRef.current = savedVersionBoundary(capacityVersionRef.current, result.version);
      setCapacityDirty(false);
      invalidateInBackground();
    }
  });

  if (policy.isLoading || summary.isLoading) return <LocalizedLoadingState />;
  if ((policy.error && !policy.data) || (summary.error && !summary.data)) {
    return <LocalizedErrorState error={(policy.error ?? summary.error)} />;
  }
  const state = policy.data;
  if (!state) return <EmptyState title={t('runtimePolicyPage.noRuntimePolicy')} description={t('runtimePolicyPage.runtimePolicyDataIsNotAvailableForThisTenant')} />;
  const warnings = summary.data?.warnings ?? [];
  const warningKeys = stableOccurrenceKeys(warnings);
  const capacityCpuPercent = toFiniteNumber(state.capacity?.noisyNeighborCpuPercent);

  const windowDraft = scheduleDraft?.windows?.[0] ?? emptyWindow;
  const budgetError = localizedRequiredTextError(projectId, t('runtimePolicyPage.projectID'), t)
    ?? localizedNonNegativeNumberError(softLimit, t('runtimePolicyPage.softLimit'), t)
    ?? localizedNonNegativeNumberError(hardLimit, t('runtimePolicyPage.hardLimit'), t)
    ?? (Number(hardLimit) < Number(softLimit) ? t('runtimePolicyPage.hardLimitOrder') : null)
    ?? localizedCurrencyError(currency, t);
  const costError = localizedRequiredTextError(sourceEventId, t('runtimePolicyPage.sourceEventID'), t)
    ?? localizedNonNegativeNumberError(amount, t('common.amount'), t)
    ?? localizedCurrencyError(currency, t);
  const persistedWindowError = scheduleDraft?.windows?.map((window, index) => {
    const windowLabel = t('runtimePolicyPage.windowIndex', { index: format.number(index + 1) });
    return localizedTimeError(window.startLocal ?? '', t('runtimePolicyPage.windowStartWithIndex', { window: windowLabel }), t)
      ?? localizedTimeError(window.endLocal ?? '', t('runtimePolicyPage.windowEndWithIndex', { window: windowLabel }), t)
      ?? localizedRequiredTextError(window.days ?? '', t('runtimePolicyPage.windowDaysWithIndex', { window: windowLabel }), t);
  }).find(Boolean) ?? null;
  const scheduleError = !scheduleDraft ? t('runtimePolicyPage.scheduleRequired')
    : localizedTimeZoneError(scheduleDraft.timeZone ?? '', t)
      ?? (!scheduleDraft.alwaysAvailable && !scheduleDraft.windows?.length ? t('runtimePolicyPage.windowRequired') : null)
      ?? persistedWindowError;
  const capacityError = !capacityDraft ? t('runtimePolicyPage.capacityRequired')
    : localizedPositiveIntegerError(String(capacityDraft.maxConcurrentTasks ?? ''), t('runtimePolicyPage.maxConcurrentTasks'), t)
      ?? localizedPositiveIntegerError(String(capacityDraft.maxQueueLength ?? ''), t('runtimePolicyPage.maxQueueLength'), t)
      ?? localizedPositiveIntegerError(String(capacityDraft.apiP95TargetMs ?? ''), t('runtimePolicyPage.apiP95Target'), t)
      ?? localizedIntegerRangeError(String(capacityDraft.noisyNeighborCpuPercent ?? ''), t('runtimePolicyPage.noisyNeighborCpuPercent'), 0, 100, t);

  const updateScheduleField = <K extends keyof WorkSchedule>(key: K, value: WorkSchedule[K]) => {
    if (!scheduleDraft) return;
    setScheduleDraft({ ...scheduleDraft, [key]: value });
    setScheduleDirty(true);
  };
  const updateWindowField = <K extends keyof WorkWindow>(key: K, value: WorkWindow[K]) => {
    if (!scheduleDraft) return;
    const windows = scheduleDraft.windows ?? [];
    setScheduleDraft({
      ...scheduleDraft,
      windows: windows.length ? [{ ...windows[0], [key]: value }, ...windows.slice(1)] : [{ ...emptyWindow, [key]: value }]
    });
    setScheduleDirty(true);
  };
  const updateCapacityField = <K extends keyof CapacityPolicy>(key: K, value: CapacityPolicy[K]) => {
    if (!capacityDraft) return;
    setCapacityDraft({ ...capacityDraft, [key]: value === '' ? '' : Number(value) });
    setCapacityDirty(true);
  };

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('runtimePolicyPage.budgetCostScheduling')}
          description={t('runtimePolicyPage.originalCurrencyAccountingDeterministicBudgetsWorkWindowsAndBackpressure')}
        />
        <Grid columns={4}>
          <MetricCard label={t('runtimePolicyPage.costLedger')} value={format.number(state.costs?.length ?? 0)} icon="billing" />
          <MetricCard label={t('runtimePolicyPage.originalCurrencyTotals')} value={format.number(summary.data?.totals.length ?? 0)} icon="wallet" />
          <MetricCard
            label={t('common.domain.unallocated')}
            value={format.number(toFiniteNumber(summary.data?.unallocatedCount) ?? 0)}
            tone={(toFiniteNumber(summary.data?.unallocatedCount) ?? 0) > 0 ? 'warning' : 'default'}
            icon="warning"
          />
          <MetricCard
            label={t('runtimePolicyPage.capacityAndBackpressure')}
            value={t('common.interval', {
              low: format.number(toFiniteNumber(state.capacity?.maxConcurrentTasks)),
              high: format.number(toFiniteNumber(state.capacity?.maxQueueLength))
            })}
            hint={`${t('common.version')} ${format.number(toFiniteNumber(state.capacity?.version))}`}
            icon="activity"
          />
        </Grid>

        <Stack direction="row" spacing={3} wrap alignItems="flex-start">
          <Stack sx={{ flex: '2 1 620px', minWidth: 0 }}>
            <Card title={t('runtimePolicyPage.costLedger')} padding="small">
              <Grid columns={2}>
                <FormField label={t('runtimePolicyPage.sourceEventID')}>
                  <TextInput value={sourceEventId} onChange={setSourceEventId} placeholder={t('runtimePolicyPage.usageUniqueEvent')} />
                </FormField>
                <FormField label={t('runtimePolicyPage.projectIDOptional')}>
                  <TextInput value={projectId} onChange={setProjectId} />
                </FormField>
                <FormField label={t('common.amount')}>
                  <TextInput value={amount} onChange={setAmount} inputProps={{ inputMode: 'decimal' }} />
                </FormField>
                <FormField label={t('common.currency')}>
                  <TextInput value={currency} onChange={setCurrency} />
                </FormField>
                <Select
                  label={t('common.category')}
                  value={category}
                  onChange={setCategory}
                  options={['Model', 'Tool', 'Storage', 'Correction'].map(value => ({ value, label: domainLabel(value) }))}
                />
              </Grid>
              <Button onClick={() => recordCost.mutate()} disabled={!!costError || recordCost.isPending} loading={recordCost.isPending}>
                {t('runtimePolicyPage.recordCostOnce')}
              </Button>
              {costError ? <Text color="error">{costError}</Text> : null}
              <LocalizedMutationFeedback mutation={recordCost} successMessage={t('runtimePolicyPage.costRecordedOnce')} />
              <LocalizedDataTable
                rows={state.costs ?? []}
                getRowId={row => row.id ?? ''}
                density="compact"
                columns={[
                  { id: 'event', header: t('runtimePolicyPage.event'), render: row => <Text variant="mono">{row.sourceEventId ?? ''}</Text> },
                  { id: 'amount', header: t('common.amount'), render: row => format.moneyMajor(toFiniteNumber(row.amount), row.currency ?? ''), numeric: true },
                  { id: 'allocation', header: t('runtimePolicyPage.allocation'), render: row => <StatusChip label={domainLabel(row.allocationStatus)} tone="neutral" /> },
                  { id: 'project', header: t('common.project'), render: row => row.projectId ?? domainLabel('Unallocated') }
                ]}
              />
            </Card>
          </Stack>
          <Stack sx={{ flex: '1 1 320px', minWidth: 0 }}>
            <Card title={t('runtimePolicyPage.originalCurrencyTotals')} padding="small">
              <LocalizedDataTable
                rows={summary.data?.totals ?? []}
                getRowId={row => row.currency}
                density="compact"
                minWidth={280}
                columns={[
                  { id: 'currency', header: t('common.currency'), render: row => row.currency },
                  { id: 'amount', header: t('common.amount'), render: row => format.moneyMajor(toFiniteNumber(row.amount), row.currency), numeric: true }
                ]}
              />
              <Text>{localizedCount(t, format, 'runtimePolicyPage.unallocatedEntries', toFiniteNumber(summary.data?.unallocatedCount) ?? 0)}</Text>
              <Stack direction="row" spacing={1} wrap>
                {warnings.map((warning, index) => <StatusChip key={warningKeys[index]} label={warning} tone="warning" />)}
              </Stack>
              <Text variant="caption">{t('runtimePolicyPage.currenciesAreNeverConvertedOrSummedTogether')}</Text>
            </Card>
          </Stack>
        </Stack>

        <Card title={t('runtimePolicyPage.projectBudget')} padding="small">
          <Grid columns={4}>
            <FormField label={t('runtimePolicyPage.projectID')}><TextInput value={projectId} onChange={setProjectId} /></FormField>
            <FormField label={t('runtimePolicyPage.softLimit')}><TextInput value={softLimit} onChange={setSoftLimit} inputProps={{ inputMode: 'decimal' }} /></FormField>
            <FormField label={t('runtimePolicyPage.hardLimit')}><TextInput value={hardLimit} onChange={setHardLimit} inputProps={{ inputMode: 'decimal' }} /></FormField>
            <FormField label={t('common.currency')}><TextInput value={currency} onChange={setCurrency} /></FormField>
          </Grid>
          <Button onClick={() => setBudget.mutate()} disabled={!!budgetError || setBudget.isPending} loading={setBudget.isPending}>
            {t('runtimePolicyPage.saveBudget')}
          </Button>
          {budgetError ? <Text color="error">{budgetError}</Text> : null}
          <LocalizedMutationFeedback mutation={setBudget} successMessage={t('runtimePolicyPage.projectBudgetSaved')} />
          <LocalizedDataTable
            rows={state.budgets ?? []}
            getRowId={row => `${row.projectId}:${row.currency}`}
            density="compact"
            columns={[
              { id: 'project', header: t('common.project'), render: row => <Text variant="mono">{row.projectId ?? ''}</Text> },
              { id: 'soft', header: t('runtimePolicyPage.soft'), render: row => format.moneyMajor(toFiniteNumber(row.softLimit), row.currency ?? ''), numeric: true },
              { id: 'hard', header: t('runtimePolicyPage.hard'), render: row => format.moneyMajor(toFiniteNumber(row.hardLimit), row.currency ?? ''), numeric: true }
            ]}
          />
        </Card>

        <Grid columns={2}>
          <Card
            title={t('runtimePolicyPage.workSchedule')}
            padding="small"
            action={scheduleDirty
              ? <StatusChip label={t('common.domain.draft')} tone="warning" />
              : <StatusChip label={`${t('common.version')} ${format.number(toFiniteNumber(scheduleDraft?.version))}`} tone="neutral" />}
          >
            <Grid columns={2}>
              <Select label={t('runtimePolicyPage.availability')} value={scheduleDraft?.alwaysAvailable ? 'true' : 'false'} disabled={!scheduleDraft || updateSchedule.isPending} onChange={value => updateScheduleField('alwaysAvailable', value === 'true')} options={[{ value: 'true', label: t('runtimePolicyPage.24x7Configurable') }, { value: 'false', label: t('runtimePolicyPage.useWindows') }]} />
              <FormField label={t('runtimePolicyPage.iANATimeZone')}><TextInput value={scheduleDraft?.timeZone ?? ''} disabled={!scheduleDraft || updateSchedule.isPending} onChange={value => updateScheduleField('timeZone', value)} /></FormField>
              <FormField label={t('runtimePolicyPage.windowDays')}><TextInput value={windowDraft.days ?? ''} disabled={!scheduleDraft || updateSchedule.isPending} onChange={value => updateWindowField('days', value)} /></FormField>
              <FormField label={t('runtimePolicyPage.windowStart')}><TextInput value={windowDraft.startLocal ?? ''} disabled={!scheduleDraft || updateSchedule.isPending} onChange={value => updateWindowField('startLocal', value)} placeholder={t('runtimePolicyPage.defaultStartTime')} /></FormField>
              <FormField label={t('runtimePolicyPage.windowEnd')}><TextInput value={windowDraft.endLocal ?? ''} disabled={!scheduleDraft || updateSchedule.isPending} onChange={value => updateWindowField('endLocal', value)} placeholder={t('runtimePolicyPage.defaultEndTime')} /></FormField>
              <Select label={t('runtimePolicyPage.windowKind')} value={windowDraft.kind ?? 'Silent'} disabled={!scheduleDraft || updateSchedule.isPending} onChange={value => updateWindowField('kind', value)} options={[{ value: 'Silent', label: t('runtimePolicyPage.silent') }, { value: 'Cheap', label: t('runtimePolicyPage.cheapPrice') }]} />
              <Select label={t('runtimePolicyPage.emergencyWake')} value={scheduleDraft?.emergencyWake ? 'true' : 'false'} disabled={!scheduleDraft || updateSchedule.isPending} onChange={value => updateScheduleField('emergencyWake', value === 'true')} options={[{ value: 'true', label: t('common.enabled') }, { value: 'false', label: t('common.disabled') }]} />
            </Grid>
            <Button onClick={() => updateSchedule.mutate()} disabled={!scheduleDraft || !!scheduleError || updateSchedule.isPending} loading={updateSchedule.isPending}>
              {t('runtimePolicyPage.saveSchedule')}
            </Button>
            {scheduleError ? <Text color="error">{scheduleError}</Text> : null}
            <LocalizedMutationFeedback mutation={updateSchedule} successMessage={t('runtimePolicyPage.workScheduleSaved')} />
          </Card>

          <Card
            title={t('runtimePolicyPage.capacityAndBackpressure')}
            padding="small"
            action={capacityDirty
              ? <StatusChip label={t('common.domain.draft')} tone="warning" />
              : <StatusChip label={`${t('common.version')} ${format.number(toFiniteNumber(capacityDraft?.version))}`} tone="neutral" />}
          >
            <Grid columns={2}>
              <FormField label={t('runtimePolicyPage.maxConcurrentTasks')}><TextInput type="number" value={String(capacityDraft?.maxConcurrentTasks ?? '')} disabled={!capacityDraft || updateCapacity.isPending} onChange={value => updateCapacityField('maxConcurrentTasks', value)} inputProps={{ inputMode: 'numeric' }} /></FormField>
              <FormField label={t('runtimePolicyPage.maxQueueLength')}><TextInput type="number" value={String(capacityDraft?.maxQueueLength ?? '')} disabled={!capacityDraft || updateCapacity.isPending} onChange={value => updateCapacityField('maxQueueLength', value)} inputProps={{ inputMode: 'numeric' }} /></FormField>
            </Grid>
            <Button tone="secondary" onClick={() => updateCapacity.mutate()} disabled={!capacityDraft || !!capacityError || updateCapacity.isPending} loading={updateCapacity.isPending}>
              {t('runtimePolicyPage.saveCapacityPolicy')}
            </Button>
            {capacityError ? <Text color="error">{capacityError}</Text> : null}
            <LocalizedMutationFeedback mutation={updateCapacity} successMessage={t('runtimePolicyPage.capacityPolicySaved')} />
            <DetailList
              dense
              columns={2}
              items={[
                { label: t('runtimePolicyPage.apiP95Target'), value: `${format.number(toFiniteNumber(state.capacity?.apiP95TargetMs))} ms` },
                { label: t('runtimePolicyPage.noisyNeighborCpuPercent'), value: format.percent(capacityCpuPercent === null ? null : capacityCpuPercent / 100) },
                { label: t('common.version'), value: format.number(toFiniteNumber(state.capacity?.version)) }
              ]}
            />
            <Text>{t('runtimePolicyPage.concurrentQueuedP95MsCPUGuardV', {
              value0: format.number(toFiniteNumber(state.capacity?.maxConcurrentTasks)),
              value1: format.number(toFiniteNumber(state.capacity?.maxQueueLength)),
              value2: format.number(toFiniteNumber(state.capacity?.apiP95TargetMs)),
              value3: format.percent(capacityCpuPercent === null ? null : capacityCpuPercent / 100),
              value4: format.number(toFiniteNumber(state.capacity?.version))
            })}</Text>
          </Card>
        </Grid>
      </Stack>
    </ContentContainer>
  );
}
