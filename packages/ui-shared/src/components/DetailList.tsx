import type { ReactNode } from 'react';
import { Box, Stack } from '@mui/material';
import { Text } from './Text';

export interface DetailItem {
  label: string;
  value: ReactNode;
  mono?: boolean;
}

export interface DetailListProps {
  items: ReadonlyArray<DetailItem>;
  columns?: 1 | 2;
  dense?: boolean;
}

export function DetailList({ items, columns = 1, dense = false }: DetailListProps) {
  return (
    <Stack
      direction={{ xs: 'column', sm: columns === 2 ? 'row' : 'column' }}
      spacing={0}
      useFlexGap
      sx={{ flexWrap: 'wrap' }}
    >
      {items.map((item, index) => (
        <Stack
          key={`${item.label}-${index}`}
          spacing={0.25}
          sx={{ flex: columns === 2 ? '1 1 240px' : '1 1 100%', minWidth: 0, py: dense ? 1 : 1.5, borderBottom: index < items.length - 1 ? 1 : 0, borderColor: 'divider' }}
        >
          <Text variant="caption" color="text.secondary">{item.label}</Text>
          {typeof item.value === 'string' || typeof item.value === 'number'
            ? <Text variant={item.mono ? 'mono' : 'body'} sx={{ overflowWrap: 'anywhere' }}>{item.value}</Text>
            : <Box component="div" sx={{ overflowWrap: 'anywhere' }}>{item.value}</Box>}
        </Stack>
      ))}
    </Stack>
  );
}
