import { Card, FreshnessBadge, SemanticLink, StatusChip, Text, type ChipTone } from '@regent/ui-shared';
import type { components } from '../../lib/api-client/schema';

type DashboardCardData = components['schemas']['DashboardCard'];

const severityTone: Record<string, ChipTone> = {
  low: 'neutral',
  medium: 'info',
  high: 'warning',
  critical: 'error'
};

export function DashboardCard({ card }: { card: DashboardCardData }) {
  return (
    <Card title={card.title} action={<FreshnessBadge freshness={card.freshness} />}>
      <Text variant="h2">{card.metric ?? card.summary}</Text>
      <Text variant="caption">{card.summary}</Text>
      <StatusChip tone={severityTone[card.severity] ?? 'neutral'} label={card.severity} />
      <SemanticLink href={card.href}>Open details</SemanticLink>
    </Card>
  );
}
