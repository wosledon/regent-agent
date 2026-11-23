import type { ReactElement } from 'react';
import { Tooltip as MuiTooltip } from '@mui/material';

export interface TooltipProps {
  title: string;
  placement?: 'top' | 'right' | 'bottom' | 'left';
  children: ReactElement;
}

export function Tooltip({ title, placement = 'top', children }: TooltipProps) {
  return <MuiTooltip title={title} placement={placement} arrow>{children}</MuiTooltip>;
}
