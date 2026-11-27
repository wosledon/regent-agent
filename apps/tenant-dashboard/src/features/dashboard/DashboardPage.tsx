import { useQuery } from '@tanstack/react-query';
import { Card, ContentContainer, EmptyState, PageHeader, SemanticLink, Stack, StatusChip, Text, type ChipTone } from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { resolveActionCenterNextAction, resolveDashboardCardPresentation } from '../../lib/dashboardPresentation';
import { DashboardCard } from './DashboardCard';

const severityTone: Record<string, ChipTone> = {
  low: 'neutral',
  medium: 'info',
  high: 'warning',
  critical: 'error'
};

const severityRank: Record<string, number> = {
  critical: 0,
  high: 1,
  medium: 2,
  low: 3
};

export function DashboardPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const { data, isLoading, error } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });
  if (isLoading) return <LocalizedLoadingState label={t('dashboardPage.loadingDashboard')} />;
  if (error) return <LocalizedErrorState error={error} />;
  if (!data) return <EmptyState title={t('dashboardPage.noDashboardData')} description={t('common.notAvailable')} />;

  const companyCard = data.cards.find((card) => card.id === 'company');
  const metricCards = data.cards.filter((card) => card.id !== 'company');
  const actions = [...(data.actions ?? [])].sort((left, right) => (severityRank[left.severity] ?? 4) - (severityRank[right.severity] ?? 4));

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={data.companyName}
          description={t('dashboardPage.generatedAt', { value0: format.dateTime(data.generatedAt) })}
          actions={
            <StatusChip
              tone={data.companyStatus.toLowerCase() === 'active' ? 'success' : 'neutral'}
              label={domainLabel(data.companyStatus)}
            />
          }
        />
        <Stack direction="row" spacing={2} wrap sx={{ alignItems: 'stretch' }}>
          {companyCard ? (
            <Stack sx={{ flex: actions.length ? '1 1 320px' : '1 1 100%' }}>
              <DashboardCard card={companyCard} presentation={resolveDashboardCardPresentation(companyCard, { t, format, domainLabel })} />
            </Stack>
          ) : null}
          {actions.length ? (
            <Stack sx={{ flex: '2 1 520px' }}>
              <Card title={t('dashboardPage.card.decisions.title')} sx={{ height: '100%' }}>
                <Stack spacing={2}>
                  {actions.map((action) => (
                    <Stack key={action.id} direction="row" spacing={1.5} wrap alignItems="center">
                      <StatusChip tone={severityTone[action.severity] ?? 'neutral'} label={domainLabel(action.severity)} />
                      <Stack spacing={0.5} sx={{ flex: '1 1 280px', minWidth: 0 }}>
                        <Text variant="bodyStrong">{action.title}</Text>
                        <Text variant="secondary">{resolveActionCenterNextAction(action, t)}</Text>
                        <Stack direction="row" spacing={1.5} wrap>
                          <Text variant="caption">{t('projectsPage.owner2', { value0: action.owner })}</Text>
                          <Text variant="caption">{domainLabel(action.sourceType)}</Text>
                          {action.slaDueAt ? (
                            <Text variant="caption">{t('projectsPage.target2', { value0: format.dateTime(action.slaDueAt) })}</Text>
                          ) : null}
                        </Stack>
                      </Stack>
                      <SemanticLink href={action.href}>{t('dashboardPage.openDetails')}</SemanticLink>
                    </Stack>
                  ))}
                </Stack>
              </Card>
            </Stack>
          ) : null}
        </Stack>
        <Stack direction="row" spacing={2} wrap sx={{ alignItems: 'stretch' }}>
          {metricCards.map((card, index) => (
            <Stack key={card.id} sx={{ flex: index === 0 ? '2 1 420px' : '1 1 240px' }}>
              <DashboardCard card={card} presentation={resolveDashboardCardPresentation(card, { t, format, domainLabel })} />
            </Stack>
          ))}
        </Stack>
      </Stack>
    </ContentContainer>
  );
}
