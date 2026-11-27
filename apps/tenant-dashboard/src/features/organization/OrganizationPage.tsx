import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ContentContainer, FormField, Grid, MetricCard,
  PageHeader, SectionHeader, Select, Stack, StatusChip, Text, TextInput, createUuidV4,
  type ChipTone
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { toFiniteNumber } from '../../lib/formatting';
import { localizedCount } from '../../lib/localizedCount';

const positionTone: Record<string, ChipTone> = {
  Open: 'success',
  Filled: 'info',
  Frozen: 'warning',
  Closed: 'neutral'
};

export function OrganizationPage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
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
      id: createUuidV4(), name: positionName, responsibilities,
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

  if (charter.isLoading || positions.isLoading || employees.isLoading || tenants.isLoading) return <LocalizedLoadingState />;
  if (charter.error || positions.error || employees.error || tenants.error)
    return <LocalizedErrorState error={(charter.error ?? positions.error ?? employees.error ?? tenants.error)} />;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('organizationPage.organization')}
          description={t('organizationPage.charterPositionsEmployeesAndTenantOwnership')}
          meta={<>
            <StatusChip label={t('organizationPage.versionLabel', { version: format.number(toFiniteNumber(charter.data?.version)) })} tone="info" />
            <StatusChip label={localizedCount(t, format, 'organizationPage.employeesInTheCurrentTenant', employees.data?.length ?? 0)} tone="neutral" />
            <StatusChip label={t('organizationPage.positions')} tone="neutral" />
          </>}
        />
        <Grid columns={4}>
          <MetricCard
            label={t('organizationPage.companyCharter')}
            value={t('organizationPage.versionLabel', { version: format.number(toFiniteNumber(charter.data?.version)) })}
            hint={charter.data?.mission}
            icon="governance"
          />
          <MetricCard
            label={t('collaborationPage.employees')}
            value={format.number(employees.data?.length ?? 0)}
            hint={t('collaborationPage.activeOrganizationMembers')}
            icon="people"
          />
          <MetricCard
            label={t('organizationPage.positions')}
            value={format.number(positions.data?.length ?? 0)}
            hint={t('organizationPage.positionLifecycle')}
            icon="organization"
          />
          <MetricCard
            label={t('common.tenant')}
            value={format.number(tenants.data?.length ?? 0)}
            hint={t('organizationPage.createTenant')}
            icon="tenants"
          />
        </Grid>
        <Grid columns={2}>
          <Card
            title={t('organizationPage.companyCharter')}
            headingLevel="h2"
            action={<StatusChip label={t('organizationPage.versionLabel', { version: format.number(toFiniteNumber(charter.data?.version)) })} tone="info" />}
          >
            <FormField label={t('organizationPage.mission')}>
              <TextInput value={charterDraft?.mission ?? charter.data?.mission ?? ''} onChange={mission => setCharterDraft({ ...(charterDraft ?? charter.data!), mission })} multiline minRows={3} />
            </FormField>
            <FormField label={t('organizationPage.constraintsCommaSeparated')}>
              <TextInput value={(charterDraft?.constraints ?? charter.data?.constraints ?? []).join(', ')} onChange={value => setCharterDraft({ ...(charterDraft ?? charter.data!), constraints: value.split(',').map(item => item.trim()).filter(Boolean) })} />
            </FormField>
            <Grid columns={2}>
              <FormField label={t('organizationPage.monthlyBudget')}>
                <TextInput value={String(charterDraft?.monthlyBudgetMinor ?? charter.data?.monthlyBudgetMinor ?? 0)} onChange={value => setCharterDraft({ ...(charterDraft ?? charter.data!), monthlyBudgetMinor: Number(value) })} inputProps={{ inputMode: 'numeric' }} />
              </FormField>
              <FormField label={t('common.currency')}>
                <TextInput value={charterDraft?.currency ?? charter.data?.currency ?? 'CNY'} onChange={currency => setCharterDraft({ ...(charterDraft ?? charter.data!), currency })} />
              </FormField>
            </Grid>
            {charterTemplates.isLoading ? <LocalizedLoadingState label={t('organizationPage.loadingCharterTemplates')} />
              : charterTemplates.error ? <LocalizedErrorState error={charterTemplates.error} onRetry={() => void charterTemplates.refetch()} />
                : <Stack direction="row" wrap spacing={1.5} alignItems="center">
                  <Stack sx={{ flex: '1 1 220px' }}>
                    <Select label={t('organizationPage.charterTemplate')} value={charterTemplate} onChange={setCharterTemplate} options={(charterTemplates.data ?? []).map(template => ({ value: template.id ?? '', label: template.name ?? '' }))} />
                  </Stack>
                  <Button tone="secondary" disabled={!charterTemplates.data?.some(template => template.id === charterTemplate)} onClick={() => setCharterDraft(charterTemplates.data?.find(template => template.id === charterTemplate)?.charter ?? null)}>{t('organizationPage.applyTemplate')}</Button>
                </Stack>}
            <Stack direction="row" wrap spacing={1.5} alignItems="center">
              <Button onClick={() => saveCharter.mutate()} disabled={!charter.data || saveCharter.isPending} loading={saveCharter.isPending}>{t('organizationPage.saveCharter')}</Button>
              <Button tone="neutral" onClick={() => start.mutate()} loading={start.isPending}>{t('organizationPage.startOrVerifyCompany')}</Button>
            </Stack>
            <LocalizedMutationFeedback mutation={saveCharter} successMessage={t('organizationPage.charterSaved')} />
            <LocalizedMutationFeedback mutation={start} successMessage={t('organizationPage.companyStartedOrVerified')} />
          </Card>
          <Card title={t('organizationPage.createTenant')} headingLevel="h2">
            <Grid columns={2}>
              <FormField label={t('organizationPage.tenantSlug')}><TextInput value={tenantName} onChange={setTenantName} placeholder={t('organizationPage.secondCompany')} /></FormField>
              <FormField label={t('organizationPage.companyName')}><TextInput value={companyName} onChange={setCompanyName} placeholder={t('organizationPage.secondCompany2')} /></FormField>
            </Grid>
            <Stack direction="row" wrap spacing={1.5} alignItems="center">
              <Button onClick={() => createTenant.mutate()} disabled={!tenantName || !companyName} loading={createTenant.isPending}>{t('organizationPage.createTenant')}</Button>
              <LocalizedMutationFeedback mutation={createTenant} successMessage={t('organizationPage.tenantCreated')} />
            </Stack>
            <LocalizedDataTable
              rows={tenants.data ?? []}
              getRowId={row => row.id}
              density="compact"
              minWidth={560}
              ariaLabel={t('organizationPage.createTenant')}
              columns={[
                { id: 'name', header: t('common.tenant'), render: row => <Text variant="bodyStrong">{row.name}</Text> },
                { id: 'company', header: t('common.company'), render: row => row.companyName },
                { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone="info" /> }
              ]}
            />
          </Card>
        </Grid>
        <SectionHeader
          title={t('organizationPage.positions')}
          description={t('organizationPage.positionLifecycle')}
          action={<StatusChip label={format.number(positions.data?.length ?? 0)} tone="neutral" />}
        />
        <Card padding="none">
          <LocalizedDataTable
            rows={positions.data ?? []}
            getRowId={row => row.id}
            density="compact"
            minWidth={960}
            ariaLabel={t('organizationPage.positions')}
            columns={[
              { id: 'name', header: t('common.position'), render: row => <Stack spacing={0.25}><Text variant="bodyStrong">{row.name}</Text><Text variant="caption">{t('organizationPage.versionLabel', { version: format.number(toFiniteNumber(row.version)) })}</Text></Stack> },
              { id: 'responsibilities', header: t('organizationPage.responsibilities'), render: row => row.responsibilities },
              { id: 'capabilities', header: t('organizationPage.capabilities'), render: row => row.capabilities.join(', ') },
              { id: 'budget', header: t('organizationPage.budgetMinorUnits'), render: row => <Text variant="mono">{format.moneyMinor(toFiniteNumber(row.budgetMinor), charter.data?.currency ?? 'CNY')}</Text> },
              { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={positionTone[row.status] ?? 'neutral'} /> },
              { id: 'actions', header: t('common.actions'), render: row => <Stack direction="row" spacing={1}>
                <Button size="small" tone="neutral" onClick={() => setPositionStatus.mutate({ id: row.id, status: row.status === 'Frozen' ? 'Open' : 'Frozen' })}>{row.status === 'Frozen' ? t('organizationPage.unfreeze') : t('organizationPage.freeze')}</Button>
                <Button size="small" tone="danger" onClick={() => setPositionStatus.mutate({ id: row.id, status: 'Closed' })} disabled={row.status === 'Closed'}>{t('organizationPage.close')}</Button>
              </Stack> }
            ]}
          />
          <LocalizedMutationFeedback mutation={setPositionStatus} successMessage={t('organizationPage.positionStatusUpdated')} />
        </Card>
        <SectionHeader title={t('organizationPage.positionLifecycle')} />
        <Card>
          <Grid columns={2}>
            <Stack spacing={1.5}>
              <Text variant="h3">{t('organizationPage.mergePositions')}</Text>
              <Select label={t('organizationPage.source')} value={mergeSource} onChange={setMergeSource} options={[{ value: '', label: t('organizationPage.selectSource') }, ...(positions.data ?? []).map(position => ({ value: position.id, label: position.name }))]} />
              <Select label={t('organizationPage.target')} value={mergeTarget} onChange={setMergeTarget} options={[{ value: '', label: t('organizationPage.selectTarget') }, ...(positions.data ?? []).map(position => ({ value: position.id, label: position.name }))]} />
              <Stack direction="row" spacing={1.5} alignItems="center" wrap>
                <Button onClick={() => mergePositions.mutate()} disabled={!mergeSource || !mergeTarget || mergeSource === mergeTarget} loading={mergePositions.isPending}>{t('organizationPage.merge')}</Button>
                <LocalizedMutationFeedback mutation={mergePositions} successMessage={t('organizationPage.positionsMerged')} />
              </Stack>
            </Stack>
            <Stack spacing={1.5}>
              <Text variant="h3">{t('organizationPage.splitPosition')}</Text>
              <Select label={t('common.position')} value={splitId} onChange={setSplitId} options={[{ value: '', label: t('organizationPage.selectPosition') }, ...(positions.data ?? []).map(position => ({ value: position.id, label: position.name }))]} />
              <Grid columns={2}>
                <FormField label={t('organizationPage.newName')}><TextInput value={splitName} onChange={setSplitName} /></FormField>
                <FormField label={t('organizationPage.responsibilities')}><TextInput value={splitResponsibilities} onChange={setSplitResponsibilities} /></FormField>
              </Grid>
              <Stack direction="row" spacing={1.5} alignItems="center" wrap>
                <Button tone="secondary" onClick={() => splitPosition.mutate()} disabled={!splitId || !splitName} loading={splitPosition.isPending}>{t('organizationPage.split')}</Button>
                <LocalizedMutationFeedback mutation={splitPosition} successMessage={t('organizationPage.positionSplit')} />
              </Stack>
            </Stack>
          </Grid>
        </Card>
        <SectionHeader title={t('organizationPage.rehireWithBossApproval')} />
        <Card tone="warning">
          <Grid columns={3}>
            <FormField label={t('organizationPage.terminatedEmployeeID')}><TextInput value={rehireEmployeeId} onChange={setRehireEmployeeId} /></FormField>
            <FormField label={t('organizationPage.openPositionID')}><TextInput value={rehirePositionId} onChange={setRehirePositionId} /></FormField>
            <FormField label={t('organizationPage.bossApprovalID')}><TextInput value={rehireApprovalId} onChange={setRehireApprovalId} /></FormField>
          </Grid>
          <Stack direction="row" wrap spacing={1.5} alignItems="center">
            <Button tone="secondary" disabled={!rehireEmployeeId || !rehirePositionId || !rehireApprovalId} loading={rehire.isPending} onClick={() => rehire.mutate()}>{t('organizationPage.rehireEmployee')}</Button>
            <LocalizedMutationFeedback mutation={rehire} successMessage={t('organizationPage.employeeRehired')} />
          </Stack>
          <Text variant="caption">{t('organizationPage.privateMemoryRemainsIsolatedAfterRehireAndIsNotRestoredAutomatically')}</Text>
        </Card>
        <Grid columns={2}>
          <Card title={t('organizationPage.createPosition')} headingLevel="h2">
            <FormField label={t('common.name')}><TextInput value={positionName} onChange={setPositionName} placeholder={t('organizationPage.productLead')} /></FormField>
            <FormField label={t('organizationPage.responsibilities')}><TextInput value={responsibilities} onChange={setResponsibilities} placeholder={t('organizationPage.defineProductOpportunities')} /></FormField>
            <FormField label={t('organizationPage.capabilities')}><TextInput value={capabilities} onChange={setCapabilities} placeholder={t('organizationPage.productDefineResearchExecute')} /></FormField>
            <FormField label={t('organizationPage.budgetMinorUnits')}><TextInput value={budget} onChange={setBudget} inputProps={{ inputMode: 'numeric' }} /></FormField>
            <Stack direction="row" spacing={1.5} alignItems="center" wrap>
              <Button onClick={() => createPosition.mutate()} disabled={!positionName} loading={createPosition.isPending}>{t('organizationPage.createPosition')}</Button>
              <LocalizedMutationFeedback mutation={createPosition} successMessage={t('organizationPage.positionCreated')} />
            </Stack>
          </Card>
          <Card title={t('organizationPage.recruitmentPipeline')} headingLevel="h2">
            <FormField label={t('organizationPage.candidateName')}><TextInput value={candidateName} onChange={setCandidateName} placeholder={t('organizationPage.candidateA')} /></FormField>
            <FormField label={t('common.position')}>
              <Select label={t('common.position')} value={candidatePosition} onChange={setCandidatePosition} options={[
                { value: '', label: t('organizationPage.selectPosition') },
                ...(positions.data ?? []).map(position => ({ value: position.id, label: position.name }))
              ]} />
            </FormField>
            <FormField label={t('organizationPage.modelVersion')}><TextInput value={candidateModel} onChange={setCandidateModel} /></FormField>
            <FormField label={t('organizationPage.jobDescription')}><TextInput multiline minRows={3} value={candidateJobDescription} onChange={setCandidateJobDescription} placeholder={t('organizationPage.jobRelatedResponsibilitiesAndRequiredEvidence')} /></FormField>
            <Stack direction="row" spacing={1.5} alignItems="center" wrap>
              <Button onClick={() => createCandidate.mutate()} disabled={!candidateName || !candidatePosition || !candidateJobDescription} loading={createCandidate.isPending}>{t('organizationPage.createCandidate')}</Button>
              <LocalizedMutationFeedback mutation={createCandidate} successMessage={t('organizationPage.candidateCreated')} />
            </Stack>
            <Text variant="caption">{localizedCount(t, format, 'organizationPage.employeesInTheCurrentTenant', employees.data?.length ?? 0)}</Text>
          </Card>
        </Grid>
      </Stack>
    </ContentContainer>
  );
}
