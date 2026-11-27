import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Card,
  ContentContainer,
  EmptyState,
  FormField,
  PageHeader,
  SemanticLink,
  Stack,
  StatusChip,
  Text,
  TextInput,
  type ChipTone
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedFreshnessBadge } from '../common/LocalizedFreshnessBadge';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';

const healthTone: Record<string, ChipTone> = {
  on_track: 'success',
  at_risk: 'warning',
  blocked: 'error',
  pending_acceptance: 'info',
  live: 'success',
  failed: 'error'
};

export function ProjectListPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['projects'], queryFn: api.projects });
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState('');
  const [owner, setOwner] = useState('Controller');
  const [mission, setMission] = useState('');
  const [targetDays, setTargetDays] = useState('14');
  const [budgetMinor, setBudgetMinor] = useState('500000');
  const [currency, setCurrency] = useState('CNY');
  const create = useMutation({
    mutationFn: () =>
      api.createProject({ name, owner, mission, targetDays: Number(targetDays), budgetMinor: Number(budgetMinor), currency }),
    onSuccess: async () => {
      setName('');
      setMission('');
      setCreateOpen(false);
      await queryClient.invalidateQueries({ queryKey: ['projects'] });
    }
  });

  if (isLoading) return <LocalizedLoadingState />;
  if (error) return <LocalizedErrorState error={error} />;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('projectsPage.projects')}
          description={t('projectsPage.deterministicHealthDeliveryFreshnessAndEvidenceGatedProgress')}
          actions={
            <Button tone={createOpen ? 'neutral' : 'primary'} onClick={() => setCreateOpen((open) => !open)} disabled={create.isPending}>
              {createOpen ? t('projectsPage.projects') : t('projectsPage.createProject')}
            </Button>
          }
        />
        {createOpen ? (
          <Card title={t('projectsPage.createProject')} headingLevel="h2">
            <Stack spacing={2}>
              <Stack direction="row" spacing={2} wrap>
                <Stack sx={{ flex: '1 1 240px' }}>
                  <FormField label={t('common.name')} required>
                    <TextInput value={name} onChange={setName} />
                  </FormField>
                </Stack>
                <Stack sx={{ flex: '1 1 240px' }}>
                  <FormField label={t('projectsPage.owner')} required>
                    <TextInput value={owner} onChange={setOwner} />
                  </FormField>
                </Stack>
              </Stack>
              <FormField label={t('projectsPage.mission')} required>
                <TextInput value={mission} onChange={setMission} multiline minRows={2} />
              </FormField>
              <Stack direction="row" spacing={2} wrap>
                <Stack sx={{ flex: '1 1 180px' }}>
                  <FormField label={t('projectsPage.targetDays')} required>
                    <TextInput value={targetDays} onChange={setTargetDays} />
                  </FormField>
                </Stack>
                <Stack sx={{ flex: '1 1 180px' }}>
                  <FormField label={t('projectsPage.budgetMinorUnits')} required>
                    <TextInput value={budgetMinor} onChange={setBudgetMinor} />
                  </FormField>
                </Stack>
                <Stack sx={{ flex: '1 1 140px' }}>
                  <FormField label={t('common.currency')} required>
                    <TextInput value={currency} onChange={setCurrency} />
                  </FormField>
                </Stack>
              </Stack>
              <Stack direction="row" spacing={2} wrap alignItems="center">
                <Button
                  onClick={() => create.mutate()}
                  disabled={!name || !owner || !mission || Number(targetDays) <= 0}
                  loading={create.isPending}
                >
                  {t('projectsPage.createProject')}
                </Button>
                <LocalizedMutationFeedback mutation={create} successMessage={t('projectsPage.projectCreated')} />
              </Stack>
            </Stack>
          </Card>
        ) : null}
        {!data?.length ? (
          <EmptyState
            title={t('projectsPage.noProjects')}
            description={t('projectsPage.createAProjectFromTheCompanyCharterToBeginDelivery')}
          />
        ) : (
          <Card padding="none">
            <LocalizedDataTable
              rows={data}
              getRowId={(project) => project.id}
              density="compact"
              stickyHeader
              minWidth={960}
              ariaLabel={t('projectsPage.projects')}
              columns={[
                {
                  id: 'name',
                  header: t('common.name'),
                  render: (project) => (
                    <SemanticLink href={`/projects/${project.id}`}>
                      <Text variant="bodyStrong">{project.name}</Text>
                    </SemanticLink>
                  )
                },
                {
                  id: 'health',
                  header: t('common.status'),
                  render: (project) => <StatusChip tone={healthTone[project.health] ?? 'neutral'} label={domainLabel(project.health)} />
                },
                { id: 'owner', header: t('projectsPage.owner'), render: (project) => project.owner },
                { id: 'target', header: t('projectsPage.target'), render: (project) => format.date(project.targetAt) },
                {
                  id: 'budget',
                  header: t('common.amount'),
                  render: (project) => (
                    <Text variant="mono">
                      {t('common.budgetUsage', {
                        spent: format.moneyMinor(toFiniteNumber(project.spentMinor), project.currency),
                        budget: format.moneyMinor(toFiniteNumber(project.budgetMinor), project.currency)
                      })}
                    </Text>
                  )
                },
                {
                  id: 'freshness',
                  header: t('common.freshnessLabel'),
                  render: (project) => <LocalizedFreshnessBadge freshness={project.freshness} />
                }
              ]}
            />
          </Card>
        )}
      </Stack>
    </ContentContainer>
  );
}
