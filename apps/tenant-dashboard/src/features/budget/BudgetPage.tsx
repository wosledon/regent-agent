import { useQuery } from '@tanstack/react-query';
import {
  Card, ContentContainer, EmptyState, FreshnessBadge, Grid, MetricCard, PageHeader,
  SectionHeader, SemanticLink, Stack, StatusChip, Text, type ChipTone
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';

const healthTone: Record<string, ChipTone> = {
  healthy: 'success',
  degraded: 'warning',
  unavailable: 'error'
};

export function BudgetPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const { data, isLoading, error } = useQuery({ queryKey: ['projects'], queryFn: api.projects });
  if (isLoading) return <LocalizedLoadingState />;
  if (error) return <LocalizedErrorState error={error} />;
  const projects = data ?? [];
  const currencies = [...new Set(projects.map(project => project.currency))];
  if (currencies.length === 0) {
    return <ContentContainer><EmptyState title={t('budgetPage.noBudgets')} description={t('budgetPage.createAProjectToEstablishTheFirstBudget')} /></ContentContainer>;
  }
  const atRiskCount = projects.filter(project => {
    const spent = toFiniteNumber(project.spentMinor);
    const budget = toFiniteNumber(project.budgetMinor);
    return spent !== null && budget !== null && spent > budget;
  }).length;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('budgetPage.budgetCost')}
          description={t('budgetPage.projectBudgetsByOriginalCurrency')}
          actions={<SemanticLink href="/budget/runtime">{t('budgetPage.openCostLedgerAndScheduling')}</SemanticLink>}
        />
        <Grid columns={3}>
          <MetricCard label={t('common.project')} value={format.number(projects.length)} icon="projects" />
          <MetricCard label={t('common.currency')} value={format.number(currencies.length)} icon="billing" />
          <MetricCard
            label={t('common.domain.atRisk')}
            value={format.number(atRiskCount)}
            tone={atRiskCount ? 'warning' : 'default'}
            icon="warning"
          />
        </Grid>

        {currencies.map(currency => {
          const currencyProjects = projects.filter(project => project.currency === currency);
          const spentValues = currencyProjects.map(project => toFiniteNumber(project.spentMinor));
          const budgetValues = currencyProjects.map(project => toFiniteNumber(project.budgetMinor));
          const spent = spentValues.every((value): value is number => value !== null)
            ? spentValues.reduce((sum, value) => sum + value, 0)
            : null;
          const budget = budgetValues.every((value): value is number => value !== null)
            ? budgetValues.reduce((sum, value) => sum + value, 0)
            : null;
          const remaining = spent === null || budget === null ? null : budget - spent;
          const usedPercent = spent === null || budget === null || budget === 0 ? null : spent / budget;

          return (
            <Stack key={currency} spacing={2}>
              <SectionHeader
                title={currency}
                description={t('budgetPage.used', { value0: format.percent(usedPercent) })}
                action={<StatusChip label={format.number(currencyProjects.length)} tone="neutral" />}
              />
              <Grid columns={3}>
                <MetricCard
                  label={t('common.domain.used')}
                  value={format.moneyMinor(spent, currency)}
                  hint={t('budgetPage.used', { value0: format.percent(usedPercent) })}
                  icon="budget"
                />
                <MetricCard label={t('common.domain.budget')} value={format.moneyMinor(budget, currency)} icon="billing" />
                <MetricCard
                  label={t('common.domain.available')}
                  value={format.moneyMinor(remaining, currency)}
                  tone={remaining !== null && remaining < 0 ? 'error' : 'default'}
                  icon="wallet"
                />
              </Grid>
              <Card padding="small">
                <LocalizedDataTable
                  rows={currencyProjects}
                  getRowId={row => row.id}
                  density="compact"
                  columns={[
                    { id: 'project', header: t('common.project'), render: row => <SemanticLink href={`/projects/${row.id}`}>{row.name}</SemanticLink> },
                    { id: 'owner', header: t('common.domain.owner'), render: row => row.owner },
                    { id: 'status', header: t('common.status'), render: row => (
                      <Stack direction="row" spacing={1} wrap alignItems="center">
                        <StatusChip label={domainLabel(row.health)} tone={healthTone[row.health] ?? 'neutral'} />
                        <FreshnessBadge freshness={row.freshness} />
                      </Stack>
                    ) },
                    { id: 'budget', header: t('common.domain.budget'), render: row => format.moneyMinor(toFiniteNumber(row.budgetMinor), currency), numeric: true },
                    { id: 'spent', header: t('common.domain.used'), render: row => format.moneyMinor(toFiniteNumber(row.spentMinor), currency), numeric: true },
                    { id: 'used', header: t('common.domain.used'), render: row => {
                      const projectSpent = toFiniteNumber(row.spentMinor);
                      const projectBudget = toFiniteNumber(row.budgetMinor);
                      return format.percent(projectSpent === null || projectBudget === null || projectBudget === 0 ? null : projectSpent / projectBudget);
                    }, numeric: true }
                  ]}
                />
                <Text variant="caption">{t('budgetPage.projectBudgetsByOriginalCurrency')}</Text>
              </Card>
            </Stack>
          );
        })}
      </Stack>
    </ContentContainer>
  );
}
