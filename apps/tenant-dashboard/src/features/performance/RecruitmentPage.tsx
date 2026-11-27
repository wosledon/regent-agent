import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Card,
  ConfirmDialog,
  ContentContainer,
  FormField,
  Grid,
  MetricCard,
  PageHeader,
  SectionHeader,
  Select,
  Stack,
  StatusChip,
  Text,
  TextInput,
  createUuidV4,
  type ChipTone
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import {
  localizedFiniteNumberError,
  localizedGenericReasonDefaults,
  localizedJsonError,
  localizedNumberRangeError,
  localizedPositiveIntegerError,
  localizedRequiredTextError,
  localizedSpecificTextError
} from '../../lib/localizedValidation';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { toFiniteNumber } from '../../lib/formatting';

interface RejectionDraft { reason: string; evidence: string }
interface RejectionVariables extends RejectionDraft { candidateId: string }
interface AppealResolutionVariables { appealId: string; resolution: string }

const emptyRejectionDraft: RejectionDraft = { reason: '', evidence: '' };

function candidateTone(status: string): ChipTone {
  if (status === 'Rejected') return 'error';
  if (status === 'Hired') return 'success';
  return 'info';
}

function appealTone(status: string): ChipTone {
  if (status === 'Open') return 'warning';
  if (status === 'Resolved') return 'success';
  if (status === 'Rejected') return 'error';
  return 'neutral';
}

function auditTone(status: string): ChipTone {
  if (status === 'Completed') return 'success';
  if (status === 'Failed') return 'error';
  if (status === 'Running') return 'info';
  return 'warning';
}

export function RecruitmentPage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const queryClient = useQueryClient();
  const interviews = useQuery({ queryKey: ['recruitment-interviews'], queryFn: () => api.recruitmentInterviews() });
  const appeals = useQuery({ queryKey: ['recruitment-appeals'], queryFn: api.recruitmentAppeals });
  const audits = useQuery({ queryKey: ['fairness-audits'], queryFn: api.fairnessAudits });
  const positions = useQuery({ queryKey: ['positions'], queryFn: api.positions });
  const candidates = useQuery({ queryKey: ['candidates'], queryFn: api.candidates });
  const [candidateId, setCandidateId] = useState('');
  const [stage, setStage] = useState('Screening');
  const [score, setScore] = useState('80');
  const [evidence, setEvidence] = useState('');
  const [appealReason, setAppealReason] = useState('');
  const [resolutionAppealId, setResolutionAppealId] = useState('');
  const [resolutionDrafts, setResolutionDrafts] = useState<Record<string, string>>({});
  const [rejectionCandidateId, setRejectionCandidateId] = useState('');
  const [rejectionDrafts, setRejectionDrafts] = useState<Record<string, RejectionDraft>>({});
  const [rejectionConfirmOpen, setRejectionConfirmOpen] = useState(false);
  const rejectionDraft = rejectionDrafts[rejectionCandidateId] ?? emptyRejectionDraft;
  const rejectionReason = rejectionDraft.reason;
  const rejectionEvidence = rejectionDraft.evidence;
  const resolution = resolutionDrafts[resolutionAppealId] ?? '';
  const [positionId, setPositionId] = useState('');
  const [period, setPeriod] = useState('2026-11');
  const [sampleSize, setSampleSize] = useState('100');
  const [ratio, setRatio] = useState('0.9');
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['recruitment-interviews'] }),
    queryClient.invalidateQueries({ queryKey: ['recruitment-appeals'] }),
    queryClient.invalidateQueries({ queryKey: ['fairness-audits'] }),
    queryClient.invalidateQueries({ queryKey: ['candidates'] })
  ]);
  const updateRejectionDraft = (field: keyof RejectionDraft, value: string) => {
    if (!rejectionCandidateId) return;
    setRejectionDrafts(current => ({
      ...current,
      [rejectionCandidateId]: { ...emptyRejectionDraft, ...current[rejectionCandidateId], [field]: value }
    }));
  };
  const updateResolution = (value: string) => {
    if (!resolutionAppealId) return;
    setResolutionDrafts(current => ({ ...current, [resolutionAppealId]: value }));
  };
  const record = useMutation({
    mutationFn: () => api.recordInterview({ id: createUuidV4(), candidateId, stage, status: 'Completed', score: Number(score), evidenceJson: evidence, createdAt: new Date().toISOString() }),
    onSuccess: invalidate
  });
  const appeal = useMutation({
    mutationFn: () => api.createCandidateAppeal(candidateId, appealReason.trim()),
    onSuccess: async () => { setAppealReason(''); await invalidate(); }
  });
  const resolve = useMutation({
    mutationFn: ({ appealId, resolution: appealResolution }: AppealResolutionVariables) => api.resolveCandidateAppeal(appealId, appealResolution.trim()),
    onSuccess: async (_data, { appealId }) => {
      setResolutionDrafts(current => {
        const next = { ...current };
        delete next[appealId];
        return next;
      });
      setResolutionAppealId(current => current === appealId ? '' : current);
      await invalidate();
    }
  });
  const audit = useMutation({
    mutationFn: () => api.runFairnessAudit(positionId, period.trim(), Number(sampleSize), Number(ratio)),
    onSuccess: invalidate
  });
  const rejectCandidate = useMutation({
    mutationFn: ({ candidateId: targetId, reason, evidence: rejectionEvidence }: RejectionVariables) =>
      api.rejectCandidate(targetId, `${reason.trim()}\nEvidence: ${rejectionEvidence.trim()}`),
    onSuccess: async (_data, { candidateId: targetId }) => {
      setRejectionDrafts(current => {
        const next = { ...current };
        delete next[targetId];
        return next;
      });
      setRejectionCandidateId(current => current === targetId ? '' : current);
      await invalidate();
    }
  });
  const retryCandidate = useMutation({ mutationFn: api.retryCandidate, onSuccess: invalidate });

  if (interviews.isLoading || appeals.isLoading || audits.isLoading || positions.isLoading || candidates.isLoading) return <LocalizedLoadingState />;
  const error = interviews.error ?? appeals.error ?? audits.error ?? positions.error ?? candidates.error;
  if (error) return <LocalizedErrorState error={error} />;

  const candidateRows = candidates.data ?? [];
  const appealRows = appeals.data ?? [];
  const auditRows = audits.data ?? [];
  const interviewRows = interviews.data ?? [];
  const validCandidate = candidateRows.some(candidate => candidate.id === candidateId);
  const validPosition = (positions.data ?? []).some(position => position.id === positionId);
  const validResolutionAppeal = appealRows.some(item => item.id === resolutionAppealId && item.status === 'Open');
  const validRejectionCandidate = candidateRows.some(candidate => candidate.id === rejectionCandidateId);
  const recordError = !validCandidate ? t('recruitmentPage.selectLoadedCandidate')
    : localizedNumberRangeError(score, t('recruitmentPage.interviewScore'), 0, 100, t)
      ?? localizedJsonError(evidence, t('recruitmentPage.evidenceJSON'), t);
  const appealError = !validCandidate ? t('recruitmentPage.selectLoadedCandidate')
    : localizedRequiredTextError(appealReason, t('recruitmentPage.appealReason'), t);
  const resolutionError = !validResolutionAppeal
    ? t('recruitmentPage.selectOpenAppealError')
    : localizedSpecificTextError(resolution, t('recruitmentPage.appealResolution'), localizedGenericReasonDefaults(t), t);
  const rejectionError = !validRejectionCandidate ? t('recruitmentPage.selectRejectCandidate')
    : localizedSpecificTextError(rejectionReason, t('recruitmentPage.jobRelatedRejectionReason'), localizedGenericReasonDefaults(t), t)
      ?? localizedRequiredTextError(rejectionEvidence, t('recruitmentPage.rejectionEvidence'), t);
  const auditError = !validPosition ? t('recruitmentPage.selectLoadedPosition')
    : localizedRequiredTextError(period, t('recruitmentPage.auditPeriod'), t)
      ?? localizedPositiveIntegerError(sampleSize, t('recruitmentPage.sampleSize'), t)
      ?? localizedFiniteNumberError(ratio, t('recruitmentPage.selectionRateRatio'), t);
  const candidateOptions = [{ value: '', label: t('recruitmentPage.selectCandidate') }, ...candidateRows.map(candidate => ({ value: candidate.id, label: candidate.displayName }))];
  const openAppeals = appealRows.filter(item => item.status === 'Open').length;
  const rejectionCandidate = candidateRows.find(candidate => candidate.id === rejectionCandidateId);

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('recruitmentPage.recruitmentGovernance')}
          description={t('recruitmentPage.interviewStagesCandidateAppealsAndFairnessAudits')}
          meta={<>
            <StatusChip label={t('common.evidence')} tone="neutral" />
            <StatusChip label={t('common.status')} tone="info" />
          </>}
        />
        <Grid columns={4}>
          <MetricCard label={t('common.candidate')} value={format.number(candidateRows.length)} hint={t('recruitmentPage.candidates')} icon="people" />
          <MetricCard label={t('common.appeal')} value={format.number(openAppeals)} hint={t('recruitmentPage.candidateAppeal')} icon="approval" tone={openAppeals > 0 ? 'warning' : 'default'} />
          <MetricCard label={t('recruitmentPage.interviewStage')} value={format.number(interviewRows.length)} hint={t('recruitmentPage.recordStage')} icon="evaluation" />
          <MetricCard label={t('recruitmentPage.fairnessAudit')} value={format.number(auditRows.length)} hint={t('recruitmentPage.runAudit')} icon="audit" />
        </Grid>

        <Grid columns={2}>
          <Card title={t('recruitmentPage.interviewStage')} headingLevel="h2" padding="small" action={<StatusChip label={domainLabel(stage)} tone="info" />}>
            <Grid columns={2}>
              <Select label={t('recruitmentPage.interviewCandidate')} value={candidateId} onChange={setCandidateId} options={candidateOptions} />
              <Select label={t('common.stage')} value={stage} onChange={setStage} options={['Screening', 'Written', 'Practical', 'Interview', 'Offer'].map(value => ({ value, label: domainLabel(value) }))} />
            </Grid>
            <FormField label={t('recruitmentPage.score')} error={recordError && validCandidate ? recordError : undefined}>
              <TextInput value={score} onChange={setScore} inputProps={{ inputMode: 'decimal' }} />
            </FormField>
            <FormField label={t('recruitmentPage.evidenceJSON')} error={recordError && validCandidate ? recordError : undefined}>
              <TextInput value={evidence} onChange={setEvidence} multiline minRows={3} />
            </FormField>
            <Stack direction="row" spacing={1.5} alignItems="center" wrap>
              <Button onClick={() => record.mutate()} disabled={!!recordError || record.isPending} loading={record.isPending}>{t('recruitmentPage.recordStage')}</Button>
              <LocalizedMutationFeedback mutation={record} successMessage={t('recruitmentPage.interviewStageRecorded')} />
            </Stack>
            {recordError ? <Text color="error">{recordError}</Text> : null}
          </Card>

          <Card title={t('recruitmentPage.candidateAppeal')} headingLevel="h2" padding="small" action={<StatusChip label={format.number(openAppeals)} tone="warning" />}>
            <Select label={t('recruitmentPage.appealCandidate')} value={candidateId} onChange={setCandidateId} options={candidateOptions} />
            <FormField label={t('common.reason')}>
              <TextInput value={appealReason} onChange={setAppealReason} multiline minRows={4} />
            </FormField>
            <Stack direction="row" spacing={1.5} alignItems="center" wrap>
              <Button tone="secondary" onClick={() => appeal.mutate()} disabled={!!appealError || appeal.isPending} loading={appeal.isPending}>{t('recruitmentPage.openAppeal')}</Button>
              <LocalizedMutationFeedback mutation={appeal} successMessage={t('recruitmentPage.candidateAppealOpened')} />
            </Stack>
            {appealError ? <Text color="error">{appealError}</Text> : null}
          </Card>
        </Grid>

        <SectionHeader title={t('recruitmentPage.candidateAppeal')} action={<StatusChip label={format.number(openAppeals)} tone="warning" />} />
        <Card padding="none">
          <LocalizedDataTable
            rows={appealRows}
            getRowId={row => row.id ?? ''}
            density="compact"
            minWidth={960}
            ariaLabel={t('recruitmentPage.candidateAppeal')}
            columns={[
              { id: 'candidate', header: t('common.candidate'), render: row => <Text variant="bodyStrong">{row.candidateId ?? ''}</Text> },
              { id: 'reason', header: t('common.reason'), render: row => <Text sx={{ minWidth: 240, whiteSpace: 'pre-wrap' }}>{row.reason}</Text> },
              { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status ?? '')} tone={appealTone(row.status ?? '')} /> },
              { id: 'resolution', header: t('recruitmentPage.appealResolution'), render: row => <Text sx={{ minWidth: 240, whiteSpace: 'pre-wrap' }}>{row.resolution || t('common.notAvailable')}</Text> },
              { id: 'actions', header: t('common.actions'), render: row => <Button size="small" tone="neutral" disabled={row.status !== 'Open' || resolve.isPending} onClick={() => setResolutionAppealId(row.id ?? '')}>{t('recruitmentPage.prepareResolution')}</Button> }
            ]}
          />
        </Card>
        <Card title={t('recruitmentPage.appealResolution')} headingLevel="h2" padding="small">
          <Select label={t('recruitmentPage.appealToResolve')} value={resolutionAppealId} onChange={setResolutionAppealId} options={[
            { value: '', label: t('recruitmentPage.selectOpenAppeal') },
            ...appealRows.filter(item => item.status === 'Open').map(item => ({ value: item.id ?? '', label: `${item.candidateId ?? ''} · ${item.reason ?? ''}` }))
          ]} />
          <FormField label={t('recruitmentPage.appealResolution')}>
            <TextInput value={resolution} onChange={updateResolution} multiline minRows={3} placeholder={t('recruitmentPage.describeTheActualReviewAndResolution')} />
          </FormField>
          <Stack direction="row" spacing={1.5} alignItems="center" wrap>
            <Button tone="secondary" onClick={() => resolve.mutate({ appealId: resolutionAppealId, resolution })} disabled={!!resolutionError || resolve.isPending} loading={resolve.isPending}>{t('recruitmentPage.resolveAppeal')}</Button>
            <LocalizedMutationFeedback mutation={resolve} successMessage={t('recruitmentPage.appealResolutionRecorded')} />
          </Stack>
          {resolutionError ? <Text color="error">{resolutionError}</Text> : null}
        </Card>

        <SectionHeader title={t('recruitmentPage.candidates')} action={<StatusChip label={format.number(candidateRows.length)} tone="neutral" />} />
        <Card padding="none">
          <LocalizedDataTable
            rows={candidateRows}
            getRowId={row => row.id}
            density="compact"
            minWidth={980}
            ariaLabel={t('recruitmentPage.candidates')}
            columns={[
              { id: 'name', header: t('common.candidate'), render: row => <Text variant="bodyStrong">{row.displayName}</Text> },
              { id: 'jd', header: t('recruitmentPage.jobDescription'), render: row => <Text sx={{ minWidth: 280, whiteSpace: 'pre-wrap' }}>{row.jobDescription}</Text> },
              { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={candidateTone(row.status)} /> },
              { id: 'actions', header: t('common.actions'), render: row => <Stack direction="row" spacing={1} wrap>
                <Button size="small" tone="danger" disabled={row.status === 'Hired' || row.status === 'Rejected' || rejectCandidate.isPending} onClick={() => setRejectionCandidateId(row.id)}>{t('recruitmentPage.prepareRejection')}</Button>
                <Button size="small" tone="secondary" disabled={row.status !== 'Rejected' || retryCandidate.isPending} loading={retryCandidate.isPending && retryCandidate.variables === row.id} onClick={() => retryCandidate.mutate(row.id)}>{t('common.retry')}</Button>
              </Stack> }
            ]}
          />
        </Card>

        <Card title={t('recruitmentPage.prepareRejection')} headingLevel="h2" tone="warning" padding="small">
          <Select label={t('recruitmentPage.candidateToReject')} value={rejectionCandidateId} onChange={setRejectionCandidateId} options={candidateOptions} />
          <FormField label={t('recruitmentPage.jobRelatedRejectionReason')}>
            <TextInput value={rejectionReason} onChange={value => updateRejectionDraft('reason', value)} multiline minRows={3} />
          </FormField>
          <FormField label={t('recruitmentPage.rejectionEvidence')}>
            <TextInput value={rejectionEvidence} onChange={value => updateRejectionDraft('evidence', value)} multiline minRows={3} />
          </FormField>
          <Stack direction="row" spacing={1.5} alignItems="center" wrap>
            <Button tone="danger" disabled={!!rejectionError || rejectCandidate.isPending} loading={rejectCandidate.isPending} onClick={() => setRejectionConfirmOpen(true)}>{t('recruitmentPage.rejectCandidate')}</Button>
            <LocalizedMutationFeedback mutation={rejectCandidate} successMessage={t('recruitmentPage.candidateRejectedWithJobRelatedEvidence')} />
          </Stack>
          {rejectionError ? <Text color="error">{rejectionError}</Text> : null}
        </Card>
        <LocalizedMutationFeedback mutation={retryCandidate} successMessage={t('recruitmentPage.candidateReturnedToThePipeline')} />

        <SectionHeader title={t('recruitmentPage.fairnessAudit')} action={<StatusChip label={format.number(auditRows.length)} tone="neutral" />} />
        <Grid columns={2}>
          <Card title={t('recruitmentPage.fairnessAudit')} headingLevel="h2" padding="small">
            <Select label={t('common.position')} value={positionId} onChange={setPositionId} options={[{ value: '', label: t('recruitmentPage.selectPosition') }, ...(positions.data ?? []).map(position => ({ value: position.id, label: position.name }))]} />
            <Grid columns={2}>
              <FormField label={t('recruitmentPage.period')}><TextInput value={period} onChange={setPeriod} /></FormField>
              <FormField label={t('recruitmentPage.sampleSize')}><TextInput value={sampleSize} onChange={setSampleSize} inputProps={{ inputMode: 'numeric' }} /></FormField>
            </Grid>
            <FormField label={t('recruitmentPage.selectionRateRatio')}><TextInput value={ratio} onChange={setRatio} inputProps={{ inputMode: 'decimal' }} /></FormField>
            <Stack direction="row" spacing={1.5} alignItems="center" wrap>
              <Button onClick={() => audit.mutate()} disabled={!!auditError || audit.isPending} loading={audit.isPending}>{t('recruitmentPage.runAudit')}</Button>
              <LocalizedMutationFeedback mutation={audit} successMessage={t('recruitmentPage.fairnessAuditCompleted')} />
            </Stack>
            {auditError ? <Text color="error">{auditError}</Text> : null}
          </Card>
          <Card padding="none">
            <LocalizedDataTable
              rows={auditRows}
              getRowId={row => row.id ?? ''}
              density="compact"
              minWidth={620}
              ariaLabel={t('recruitmentPage.fairnessAudit')}
              columns={[
                { id: 'position', header: t('common.position'), render: row => row.positionId ?? '' },
                { id: 'period', header: t('recruitmentPage.period'), render: row => row.period ?? t('common.notAvailable') },
                { id: 'samples', header: t('recruitmentPage.samples'), numeric: true, render: row => format.number(toFiniteNumber(row.sampleSize)) },
                { id: 'ratio', header: t('recruitmentPage.ratio'), numeric: true, render: row => format.number(toFiniteNumber(row.selectionRateRatio)) },
                { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status ?? '')} tone={auditTone(row.status ?? '')} /> },
                { id: 'evidence', header: t('common.evidence'), render: row => <Text variant="mono" sx={{ minWidth: 180, whiteSpace: 'pre-wrap' }}>{row.detailsJson || t('common.notAvailable')}</Text> }
              ]}
            />
          </Card>
        </Grid>

        <ConfirmDialog
          open={rejectionConfirmOpen}
          title={t('recruitmentPage.rejectCandidate')}
          description={<Text component="span" sx={{ whiteSpace: 'pre-line' }}>{t('recruitmentPage.confirmReject', {
            candidate: rejectionCandidate?.displayName ?? rejectionCandidateId,
            reason: rejectionReason.trim(),
            evidence: rejectionEvidence.trim()
          })}</Text>}
          confirmLabel={t('recruitmentPage.rejectCandidate')}
          cancelLabel={t('common.no')}
          tone="danger"
          loading={rejectCandidate.isPending}
          requireText={rejectionCandidate?.displayName ?? rejectionCandidateId}
          onConfirm={() => { setRejectionConfirmOpen(false); rejectCandidate.mutate({ candidateId: rejectionCandidateId, ...rejectionDraft }); }}
          onClose={() => { if (!rejectCandidate.isPending) setRejectionConfirmOpen(false); }}
        />
      </Stack>
    </ContentContainer>
  );
}
