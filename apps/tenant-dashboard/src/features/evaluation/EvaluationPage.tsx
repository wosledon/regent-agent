import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ContentContainer, FormField, Grid, MetricCard,
  PageHeader, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { localizedIntegerRangeError, localizedPositiveIntegerError } from '../../lib/localizedValidation';
import { useDomainLabels } from '../../lib/domainLabels';
import { stableOccurrenceKeys } from '../../lib/stableKeys';
import { toFiniteNumber } from '../../lib/formatting';
import { localizedCount } from '../../lib/localizedCount';

export function EvaluationPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const suites = useQuery({ queryKey: ['evaluation-suites'], queryFn: api.evaluationSuites });
  const runs = useQuery({ queryKey: ['evaluations'], queryFn: api.evaluations });
  const qa = useQuery({ queryKey: ['evaluation-qa'], queryFn: api.evaluationQa });
  const employees = useQuery({ queryKey: ['employees'], queryFn: api.employees });
  const [candidateEmployeeId, setCandidateEmployeeId] = useState('');
  const [suiteId, setSuiteId] = useState('');
  const [repeats, setRepeats] = useState('3');
  const [sampleCount, setSampleCount] = useState('100');
  const [successes, setSuccesses] = useState('92');
  const [baselineSuccesses, setBaselineSuccesses] = useState('85');
  const [discordantA, setDiscordantA] = useState('10');
  const [discordantB, setDiscordantB] = useState('2');
  const [calibrationPassed, setCalibrationPassed] = useState('true');
  const run = useMutation({
    mutationFn: ({ suiteId: selectedSuiteId, candidateEmployeeId: employeeId }: { suiteId: string; candidateEmployeeId: string }) => api.runEvaluation(selectedSuiteId, employeeId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['evaluations'] })
  });
  const gate = useMutation({
    mutationFn: () => api.runQaGate({
      suiteId, candidateEmployeeId, repeats: Number(repeats), sampleCount: Number(sampleCount),
      successes: Number(successes), baselineSuccesses: Number(baselineSuccesses),
      discordantA: Number(discordantA), discordantB: Number(discordantB), calibrationPassed: calibrationPassed === 'true'
    }), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['evaluation-qa'] })
  });

  if (suites.isLoading || runs.isLoading || qa.isLoading || employees.isLoading) return <LocalizedLoadingState />;
  const queryError = suites.error ?? runs.error ?? qa.error ?? employees.error;
  if (queryError) return <LocalizedErrorState error={queryError} />;

  const validCandidate = employees.data?.some(employee => employee.id === candidateEmployeeId) === true;
  const validSuite = suites.data?.some(suite => suite.id === suiteId) === true;
  const sampleSize = Number(sampleCount);
  const calibrations = qa.data?.calibrations ?? [];
  const calibrationKeys = stableOccurrenceKeys(calibrations.map(row =>
    `${row.sampleCount ?? ''}:${row.agreement ?? ''}:${row.kappa ?? ''}`
  ));
  const gateError = !validCandidate ? t('evaluationsPage.selectLoadedEmployee')
    : !validSuite ? t('evaluationsPage.selectLoadedSuite')
      : localizedPositiveIntegerError(repeats, t('evaluationsPage.repeats'), t)
        ?? localizedPositiveIntegerError(sampleCount, t('evaluationsPage.sampleCount'), t)
        ?? localizedIntegerRangeError(successes, t('evaluationsPage.successes'), 0, Number.isSafeInteger(sampleSize) ? sampleSize : Number.MAX_SAFE_INTEGER, t)
        ?? localizedIntegerRangeError(baselineSuccesses, t('evaluationsPage.baselineSuccesses'), 0, Number.isSafeInteger(sampleSize) ? sampleSize : Number.MAX_SAFE_INTEGER, t)
        ?? localizedIntegerRangeError(discordantA, t('evaluationsPage.discordantA'), 0, Number.MAX_SAFE_INTEGER, t)
        ?? localizedIntegerRangeError(discordantB, t('evaluationsPage.discordantB'), 0, Number.MAX_SAFE_INTEGER, t);

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader title={t('evaluationsPage.modelEvaluation')} description={t('evaluationsPage.versionedSuitesIndependentJudgesCalibrationAndStatisticalReleaseGates')} />
        <Grid columns={4}>
          <MetricCard label={t('evaluationsPage.benchmarkSuites')} value={suites.data?.length ?? 0} icon="evaluation" />
          <MetricCard label={t('evaluationsPage.evaluationRuns')} value={runs.data?.length ?? 0} icon="activity" />
          <MetricCard label={t('evaluationsPage.judgeCalibration')} value={calibrations.length} icon="evaluation" />
          <MetricCard label={t('evaluationsPage.qAGates')} value={qa.data?.gates?.length ?? 0} icon="governance" tone={(qa.data?.gates?.length ?? 0) > 0 ? 'info' : 'default'} />
        </Grid>
        <Grid columns={2}>
        <Card padding="small" title={t('evaluationsPage.benchmarkSuites')}>
          <LocalizedDataTable
            density="compact"
            rows={suites.data ?? []}
            getRowId={suite => suite.id}
            emptyTitle={t('evaluationsPage.noEvaluationSuites')}
            emptyDescription={t('evaluationsPage.createAVersionedSuiteBeforeRunningAnEvaluation')}
            columns={[
              { id: 'suite', header: t('evaluationsPage.suite'), render: suite => <Text variant="bodyStrong" component="h3">{t('evaluationsPage.v', { value0: suite.name, value1: format.number(toFiniteNumber(suite.version)) })}</Text> },
              { id: 'cases', header: t('evaluationsPage.cases'), render: suite => localizedCount(t, format, 'evaluationsPage.cases', toFiniteNumber(suite.caseCount) ?? 0) },
              { id: 'actions', header: t('common.actions'), align: 'right', render: suite => <Button size="small" onClick={() => run.mutate({ suiteId: suite.id, candidateEmployeeId })} disabled={!validCandidate || run.isPending} loading={run.isPending && run.variables?.suiteId === suite.id}>{t('evaluationsPage.runEvaluation')}</Button> }
            ]}
          />
          <LocalizedMutationFeedback mutation={run} successMessage={t('evaluationsPage.evaluationRunCompleted')} />
        </Card>
        <Card padding="small" title={t('evaluationsPage.qAGate')}>
          <Select label={t('evaluationsPage.candidateEmployee')} value={candidateEmployeeId} onChange={setCandidateEmployeeId} options={[
            { value: '', label: t('evaluationsPage.selectEmployee') },
            ...(employees.data ?? []).map(employee => ({ value: employee.id, label: `${employee.displayName} · ${employee.employeeNo}` }))
          ]} />
          <Select label={t('evaluationsPage.evaluationSuite')} value={suiteId} onChange={setSuiteId} options={[
            { value: '', label: t('evaluationsPage.selectSuite') },
            ...(suites.data ?? []).map(suite => ({ value: suite.id, label: t('evaluationsPage.suiteVersion', { name: suite.name, version: format.number(toFiniteNumber(suite.version)) }) }))
          ]} />
          <FormField label={t('evaluationsPage.repeats')}><TextInput value={repeats} onChange={setRepeats} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label={t('evaluationsPage.sampleCount')}><TextInput value={sampleCount} onChange={setSampleCount} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label={t('evaluationsPage.successes')}><TextInput value={successes} onChange={setSuccesses} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label={t('evaluationsPage.baselineSuccesses')}><TextInput value={baselineSuccesses} onChange={setBaselineSuccesses} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label={t('evaluationsPage.discordantA')}><TextInput value={discordantA} onChange={setDiscordantA} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <FormField label={t('evaluationsPage.discordantB')}><TextInput value={discordantB} onChange={setDiscordantB} inputProps={{ inputMode: 'numeric' }} /></FormField>
          <Select label={t('evaluationsPage.calibrationPassed')} value={calibrationPassed} onChange={setCalibrationPassed} options={[{ value: 'true', label: t('common.yes') }, { value: 'false', label: t('common.no') }]} />
          <Button onClick={() => gate.mutate()} disabled={!!gateError || gate.isPending} loading={gate.isPending}>{t('evaluationsPage.evaluateQAGate')}</Button>
          {gateError ? <Text color="error">{gateError}</Text> : null}
          <LocalizedMutationFeedback mutation={gate} successMessage={t('evaluationsPage.qAGateEvaluated')} />
        </Card>
      </Grid>
      <Card padding="small" title={t('evaluationsPage.evaluationRuns')}>
        <LocalizedDataTable density="compact" rows={runs.data ?? []} getRowId={row => row.id} columns={[
          { id: 'suite', header: t('evaluationsPage.suite'), render: row => row.suiteId },
          { id: 'candidate', header: t('common.candidate'), render: row => row.candidateEmployeeId },
          { id: 'score', header: t('evaluationsPage.score'), render: row => format.number(toFiniteNumber(row.score)) },
          { id: 'safety', header: t('evaluationsPage.safety'), render: row => <StatusChip label={row.safetyPassed ? domainLabel('Pass') : domainLabel('Fail')} tone={row.safetyPassed ? 'success' : 'error'} /> },
          { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Approved' ? 'success' : 'error'} /> }
        ]} />
      </Card>
      <Grid columns={2}>
        <Card padding="small" title={t('evaluationsPage.judgeCalibration')}>
          <LocalizedDataTable density="compact" rows={calibrations} getRowId={(_row, index) => calibrationKeys[index]} columns={[
            { id: 'samples', header: t('evaluationsPage.samples'), render: row => format.number(toFiniteNumber(row.sampleCount)) },
            { id: 'agreement', header: t('evaluationsPage.agreement'), render: row => format.percent(toFiniteNumber(row.agreement)) },
            { id: 'kappa', header: t('evaluationsPage.kappa'), render: row => format.number(toFiniteNumber(row.kappa), { minimumFractionDigits: 2, maximumFractionDigits: 2 }) },
            { id: 'status', header: t('common.status'), render: row => <StatusChip label={row.passed ? domainLabel('Pass') : domainLabel('Fail')} tone={row.passed ? 'success' : 'error'} /> }
          ]} />
        </Card>
        <Card padding="small" title={t('evaluationsPage.qAGates')}>
          <LocalizedDataTable density="compact" rows={qa.data?.gates ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'suite', header: t('evaluationsPage.suite'), render: row => row.suiteId ?? '' },
            { id: 'interval', header: t('evaluationsPage.wilson95'), render: row => row.scoreInterval ? t('common.interval', { low: format.percent(toFiniteNumber(row.scoreInterval.low)), high: format.percent(toFiniteNumber(row.scoreInterval.high)) }) : t('common.notAvailable') },
            { id: 'contamination', header: t('evaluationsPage.contaminated'), render: row => format.number(toFiniteNumber(row.contaminatedCases)) },
            { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Approved' ? 'success' : 'error'} /> }
          ]} />
        </Card>
        </Grid>
      </Stack>
    </ContentContainer>
  );
}
