import { StatusChip, type ChipTone } from './StatusChip';

export type Freshness = 'live' | 'fresh' | 'stale' | 'unknown';

export interface FreshnessBadgeProps {
  freshness: Freshness;
  label?: string;
}

const tone: Record<Freshness, ChipTone> = {
  live: 'success',
  fresh: 'info',
  stale: 'warning',
  unknown: 'neutral'
};

export function FreshnessBadge({ freshness, label }: FreshnessBadgeProps) {
  return <StatusChip tone={tone[freshness]} label={label ?? freshness} />;
}
