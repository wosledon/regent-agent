import { CircularProgress, Stack } from '@mui/material';

export function LoadingState({ label = 'Loading' }: { label?: string }) {
  return <Stack alignItems="center" sx={{ py: 6 }}><CircularProgress aria-label={label} /></Stack>;
}
