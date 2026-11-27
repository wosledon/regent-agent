import { Alert, Stack } from '@mui/material';
import { Button } from './Button';
import { Text } from './Text';

export interface ErrorStateProps {
  message: string;
  onRetry?: () => void;
  status?: number;
  title?: string;
  retryLabel?: string;
  compact?: boolean;
}

export function ErrorState({ message, onRetry, status, title, retryLabel = 'Retry', compact = false }: ErrorStateProps) {
  const sanitizedPermissionMessage = message === 'Permission denied. You do not have access to this operation.'
    || message === 'Permission denied. You do not have access to this resource.';
  const permissionDenied = status === 403 || /\b403\b|forbidden/i.test(message)
    || (!sanitizedPermissionMessage && /permission denied/i.test(message));
  return (
    <Alert
      severity={permissionDenied ? 'warning' : 'error'}
      role="alert"
      action={onRetry ? <Button tone="neutral" size="small" onClick={onRetry}>{retryLabel}</Button> : undefined}
      sx={{ alignItems: 'center', px: compact ? 1.5 : 2, py: compact ? 1 : 2 }}
    >
      <Stack spacing={0.25}>
        <Text variant="bodyStrong">{title ?? (permissionDenied ? 'Permission denied' : 'Something went wrong')}</Text>
        <Text variant="secondary" sx={{ color: 'inherit' }}>
          {permissionDenied ? 'You do not have access to this resource.' : message}
        </Text>
      </Stack>
    </Alert>
  );
}
