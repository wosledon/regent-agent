import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Card,
  ContentContainer,
  DetailList,
  EmptyState,
  FormField,
  PageHeader,
  Select,
  SemanticLink,
  Stack,
  StatusChip,
  TextInput,
  type ChipTone
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedFreshnessBadge } from '../common/LocalizedFreshnessBadge';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import type { components } from '../../lib/api-client/schema';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';

type ProjectDetail = components['schemas']['ProjectDetail'];

const healthTone: Record<string, ChipTone> = {
  on_track: 'success',
  at_risk: 'warning',
  blocked: 'error',
  pending_acceptance: 'info',
  live: 'success',
  failed: 'error'
};

function statusTone(status: string): ChipTone {
  const normalized = status.toLowerCase();
  if (['active', 'completed', 'accepted', 'done', 'live', 'resolved'].includes(normalized)) return 'success';
  if (['blocked', 'failed', 'cancelled', 'reopened'].includes(normalized)) return 'error';
  if (['atrisk', 'at_risk', 'paused', 'readyforretest', 'inreview'].includes(normalized)) return 'warning';
  return 'info';
}

export function ProjectDetailPage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const { projectId = '' } = useParams();
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['projects', projectId], queryFn: () => api.project(projectId) });
  const [stage, setStage] = useState('Approved');
  const [evidence, setEvidence] = useState('');
  const transition = useMutation({
    mutationFn: () => api.transitionProject(projectId, stage, evidence),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['projects', projectId] })
  });
  if (isLoading) return <LocalizedLoadingState />;
  if (error) return <LocalizedErrorState error={error} />;
  if (!data)
    return (
      <EmptyState title={t('projectsPage.projectNotFound')} description={t('projectsPage.returnToTheProjectListAndChooseAnotherProject')} />
    );
  return (
    <ContentContainer>
      <Stack spacing={3}>
        <ProjectDetailView project={data} />
        <Card title={t('projectsPage.projectState')} headingLevel="h2">
          <Stack spacing={2}>
            <Stack direction="row" spacing={2} wrap>
              <Stack sx={{ flex: '1 1 220px' }}>
                <Select
                  label={t('common.stage')}
                  value={stage}
                  onChange={setStage}
                  options={['Proposed', 'Approved', 'Active', 'Paused', 'Completed', 'Cancelled'].map((value) => ({
                    value,
                    label: domainLabel(value)
                  }))}
                />
              </Stack>
              <Stack sx={{ flex: '2 1 360px' }}>
                <FormField label={t('common.evidence')}>
                  <TextInput value={evidence} onChange={setEvidence} multiline minRows={2} />
                </FormField>
              </Stack>
            </Stack>
            <Stack direction="row" spacing={2} wrap alignItems="center">
              <Button onClick={() => transition.mutate()} loading={transition.isPending}>
                {t('projectsPage.transition')}
              </Button>
              <LocalizedMutationFeedback mutation={transition} successMessage={t('projectsPage.projectTransitioned')} />
            </Stack>
          </Stack>
        </Card>
      </Stack>
    </ContentContainer>
  );
}

function ProjectDetailView({ project }: { project: ProjectDetail }) {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const summary = project.project;
  return (
    <Stack spacing={3}>
      <PageHeader
        title={summary.name}
        description={t('projectsPage.projectMilestonesTasksAndEvidenceGatedDelivery')}
        actions={
          <Stack direction="row" spacing={2} wrap>
            <SemanticLink href="/projects">{t('projectsPage.projects')}</SemanticLink>
            <SemanticLink href={`/projects/${summary.id}/delivery`}>{t('projectsPage.openDeliveryLifecycle')}</SemanticLink>
          </Stack>
        }
      />
      <Card padding="small">
        <Stack direction="row" spacing={1.5} wrap alignItems="center">
          <StatusChip label={domainLabel(summary.stage)} tone={statusTone(summary.stage)} />
          <StatusChip label={domainLabel(summary.health)} tone={healthTone[summary.health] ?? 'neutral'} />
          <LocalizedFreshnessBadge freshness={summary.freshness} />
        </Stack>
      </Card>
      <Card>
        <DetailList
          columns={2}
          dense
          items={[
            { label: t('projectsPage.owner'), value: summary.owner },
            { label: t('projectsPage.target'), value: format.date(summary.targetAt) },
            {
              label: t('common.amount'),
              value: t('common.budgetUsage', {
                spent: format.moneyMinor(toFiniteNumber(summary.spentMinor), summary.currency),
                budget: format.moneyMinor(toFiniteNumber(summary.budgetMinor), summary.currency)
              }),
              mono: true
            }
          ]}
        />
      </Card>
      <Card title={t('projectsPage.milestones')} headingLevel="h2">
        <LocalizedDataTable
          rows={project.milestones}
          getRowId={(row) => row.id}
          density="compact"
          minWidth={640}
          columns={[
            { id: 'name', header: t('projectsPage.milestone'), render: (row) => row.name },
            { id: 'target', header: t('projectsPage.target'), render: (row) => format.date(row.targetAt) },
            {
              id: 'status',
              header: t('common.status'),
              render: (row) => <StatusChip label={domainLabel(row.status)} tone={statusTone(row.status)} />
            }
          ]}
        />
      </Card>
      <Card title={t('projectsPage.tasks')} headingLevel="h2">
        <LocalizedDataTable
          rows={project.tasks}
          getRowId={(row) => row.id}
          density="compact"
          minWidth={760}
          columns={[
            { id: 'title', header: t('common.task'), render: (row) => row.title },
            { id: 'type', header: t('projectsPage.type'), render: (row) => row.taskType },
            { id: 'assignee', header: t('projectsPage.assignee'), render: (row) => row.assignee },
            {
              id: 'status',
              header: t('common.status'),
              render: (row) => <StatusChip label={domainLabel(row.status)} tone={statusTone(row.status)} />
            }
          ]}
        />
      </Card>
    </Stack>
  );
}
