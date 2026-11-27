import { Card, SemanticLink, Stack, StatusChip, Text, type ChipTone } from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import type { components } from '../../lib/api-client/schema';
import { LocalizedFreshnessBadge } from '../common/LocalizedFreshnessBadge';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { resolveDashboardCardPresentation, type ResolvedDashboardCard } from '../../lib/dashboardPresentation';

type DashboardCardData = components['schemas']['DashboardCard'];
interface DashboardCardProps {
  card: DashboardCardData;
  presentation?: ResolvedDashboardCard;
}

const severityTone: Record<string, ChipTone> = {
  low: 'neutral',
  medium: 'info',
  high: 'warning',
  critical: 'error'
};

export function DashboardCard({ card, presentation: resolved }: DashboardCardProps) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const presentation = resolved ?? resolveDashboardCardPresentation(card, { t, format, domainLabel });
  const metric = presentation.metric || presentation.summary || t('common.notAvailable');
  const summary = presentation.summary || t('common.notAvailable');

  return (
    <Card
      title={presentation.title}
      headingLevel="h2"
      action={<LocalizedFreshnessBadge freshness={card.freshness} />}
      footer={<SemanticLink href={card.href}>{t('dashboardPage.openDetails')}</SemanticLink>}
      sx={{ height: '100%' }}
    >
      <Stack spacing={1.5}>
        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
          <Text variant="metric" component="span" sx={{ overflowWrap: 'anywhere' }}>
            {metric}
          </Text>
          <StatusChip tone={severityTone[card.severity] ?? 'neutral'} label={domainLabel(card.severity)} />
        </Stack>
        {summary !== metric ? <Text variant="secondary">{summary}</Text> : null}
      </Stack>
    </Card>
  );
}
