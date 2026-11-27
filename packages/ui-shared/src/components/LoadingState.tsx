import { CircularProgress, Skeleton, Stack } from '@mui/material';
import { Text } from './Text';

export interface LoadingStateProps {
  label?: string;
  variant?: 'spinner' | 'skeleton';
  rows?: number;
}

export function LoadingState({ label = 'Loading', variant = 'spinner', rows = 3 }: LoadingStateProps) {
  if (variant === 'skeleton') {
    return (
      <Stack spacing={2} role="status" aria-label={label} aria-busy="true">
        {Array.from({ length: rows }, (_, index) => <Skeleton key={index} variant="rounded" height={index === 0 ? 32 : 48} />)}
      </Stack>
    );
  }
  return (
    <Stack alignItems="center" spacing={1.5} sx={{ py: 6 }} role="status" aria-busy="true">
      <CircularProgress size={24} role="progressbar" aria-label={label} />
      <Text variant="secondary">{label}</Text>
    </Stack>
  );
}
