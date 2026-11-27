import { useTranslation } from 'react-i18next';
import { StatusChip, type ChipTone } from '@regent/ui-shared';

const tones: Record<string, ChipTone> = { live: 'success', fresh: 'info', stale: 'warning', unknown: 'neutral' };
const labelKeys: Record<string, string> = {
  live: 'common.freshness.live', fresh: 'common.freshness.fresh', stale: 'common.freshness.stale', unknown: 'common.freshness.unknown'
};

export function LocalizedFreshnessBadge({ freshness }: { freshness: string }) {
  const { t } = useTranslation();
  return <StatusChip tone={tones[freshness] ?? 'neutral'} label={t(labelKeys[freshness] ?? 'common.freshness.unknown')} />;
}
