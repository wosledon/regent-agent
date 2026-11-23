import { Alert } from '@mui/material';
import { Button } from './Button';

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return <Alert severity="error" action={onRetry ? <Button tone="neutral" onClick={onRetry}>Retry</Button> : undefined}>{message}</Alert>;
}
