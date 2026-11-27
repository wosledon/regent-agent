import type { ReactNode } from 'react';
import { Stack } from '@mui/material';

export interface FilterBarProps {
  children: ReactNode;
  action?: ReactNode;
}

export function FilterBar({ children, action }: FilterBarProps) {
  return (
    <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} sx={{ alignItems: { md: 'center' }, justifyContent: 'space-between' }}>
      <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', minWidth: 0, flex: 1 }}>{children}</Stack>
      {action ? <Stack direction="row" spacing={1} sx={{ flexShrink: 0 }}>{action}</Stack> : null}
    </Stack>
  );
}
