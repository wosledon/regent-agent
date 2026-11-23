import type { ChipProps } from '@mui/material';
import { Chip as MuiChip } from '@mui/material';

export type ChipTone = 'neutral' | 'success' | 'warning' | 'error' | 'info';

export interface StatusChipProps extends Omit<ChipProps, 'color'> {
  tone?: ChipTone;
}

export function StatusChip({ tone = 'neutral', ...props }: StatusChipProps) {
  const color = tone === 'success' ? 'success' : tone === 'warning' ? 'warning' : tone === 'error' ? 'error' : tone === 'info' ? 'info' : 'default';
  return <MuiChip color={color} size="small" variant={tone === 'neutral' ? 'outlined' : 'filled'} {...props} />;
}
