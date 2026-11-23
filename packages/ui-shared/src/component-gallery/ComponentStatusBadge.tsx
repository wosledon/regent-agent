import { StatusChip } from '../components/StatusChip';
import type { ComponentStatus } from './registry';

export interface ComponentStatusBadgeProps {
  status: ComponentStatus;
}

export function ComponentStatusBadge({ status }: ComponentStatusBadgeProps) {
  const tone = status === 'stable' ? 'success' : status === 'candidate' ? 'warning' : 'error';
  return <StatusChip tone={tone} label={status} />;
}
