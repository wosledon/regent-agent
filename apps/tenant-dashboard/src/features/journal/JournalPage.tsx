import { useQuery } from '@tanstack/react-query';
import {
  Card, ContentContainer, DetailList, EmptyState, FreshnessBadge, Grid, MetricCard,
  PageHeader, SectionHeader, SemanticLink, Stack, StatusChip
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { api } from '../../lib/api-client/api';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { useDomainLabels } from '../../lib/domainLabels';
import { resolveDashboardCardPresentation } from '../../lib/dashboardPresentation';

export function JournalPage() {
  const { t } = useTranslation();
  const format = useTenantFormatting();
  const domainLabel = useDomainLabels();
  const { data, isLoading, error } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });
  if (isLoading) return <LocalizedLoadingState />;
  if (error) return <LocalizedErrorState error={error} />;
  if (!data) return <ContentContainer><EmptyState title={t('journalPage.noRecentDecision')} /></ContentContainer>;

  const decision = data.cards.find(card => card.id === 'decisions');
  const presentation = decision ? resolveDashboardCardPresentation(decision, { t, format, domainLabel }) : null;
  const freshness = decision?.freshness ?? 'unknown';

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('journalPage.companyJournal')}
          meta={<StatusChip label={t('common.domain.audit')} tone="info" />}
        />
        <Grid columns={3}>
          <MetricCard
            label={t('journalPage.recentDecision')}
            value={decision ? t('common.domain.decided') : t('common.domain.unknown')}
            tone={decision ? 'success' : 'default'}
            icon="journal"
          />
          <MetricCard
            label={t('journalPage.auditIntegrity')}
            value={t('common.domain.anchored')}
            tone="success"
            icon="audit"
          />
          <MetricCard
            label={t('common.status')}
            value={<FreshnessBadge freshness={freshness} label={t(`common.freshness.${freshness}`)} />}
            hint={format.dateTime(data.generatedAt)}
            icon="clock"
          />
        </Grid>
        <Grid columns={2}>
          <Stack spacing={2}>
            <SectionHeader title={t('journalPage.recentDecision')} />
            <Card padding="small">
              {decision && presentation ? (
                <DetailList dense columns={2} items={[
                  { label: t('common.title'), value: presentation.title },
                  { label: t('common.summary'), value: presentation.summary },
                  { label: t('common.status'), value: <FreshnessBadge freshness={freshness} label={t(`common.freshness.${freshness}`)} /> },
                  { label: t('common.severity'), value: domainLabel(decision.severity) },
                  ...(decision.metricPresentation?.kind === 'timestamp' ? [] : [{ label: t('common.created'), value: format.dateTime(decision.dataAt) }]),
                  { label: t('common.evidence'), value: presentation.metric || t('common.notAvailable') }
                ]} />
              ) : (
                <EmptyState title={t('journalPage.noRecentDecision')} compact />
              )}
            </Card>
          </Stack>
          <Stack spacing={2}>
            <SectionHeader title={t('journalPage.auditIntegrity')} />
            <Card padding="small">
              <DetailList dense columns={1} items={[
                { label: t('common.status'), value: <StatusChip label={t('common.domain.anchored')} tone="success" /> },
                { label: t('common.evidence'), value: t('journalPage.hashChainEnabledBackendAuditEventStore') },
                { label: t('common.nextAction'), value: <SemanticLink href="/governance/audit">{t('common.domain.audit')}</SemanticLink> }
              ]} />
            </Card>
          </Stack>
        </Grid>
      </Stack>
    </ContentContainer>
  );
}
