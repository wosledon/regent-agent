import type { ReactNode } from 'react';
import { InboxOutlined } from '@mui/icons-material';
import { Stack, type SxProps, type Theme } from '@mui/material';
import { Text } from './Text';

export interface EmptyStateProps {
  title: string;
  description?: string;
  action?: ReactNode;
  compact?: boolean;
  sx?: SxProps<Theme>;
}

export function EmptyState({ title, description, action, compact = false, sx }: EmptyStateProps) {
  return (
    <Stack alignItems="center" spacing={1.25} sx={[{ px: 2, py: compact ? 3 : 6, textAlign: 'center' }, ...(Array.isArray(sx) ? sx : sx ? [sx] : [])]}>
      <InboxOutlined sx={{ fontSize: compact ? 28 : 36, color: 'text.disabled' }} aria-hidden="true" />
      <Text variant="h2" component="h2" sx={{ fontSize: '1rem' }}>{title}</Text>
      {description ? <Text variant="secondary" sx={{ maxWidth: '48ch' }}>{description}</Text> : null}
      {action ? <Stack sx={{ pt: 1 }}>{action}</Stack> : null}
    </Stack>
  );
}
