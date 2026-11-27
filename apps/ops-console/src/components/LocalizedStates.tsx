import { Alert, CircularProgress, Stack } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { Button, FreshnessBadge, LoadingState, Text } from '@regent/ui-shared';

export interface LocalizedLoadingStateProps {
  label?: string;
  inline?: boolean;
  rows?: number;
}

export function LocalizedLoadingState({ label, inline = false, rows = 3 }: LocalizedLoadingStateProps) {
  const { t } = useTranslation();
  const resolvedLabel = label ?? t('common.loading');
  if (inline) {
    return (
      <Stack direction="row" spacing={1} alignItems="center" role="status" aria-busy="true" sx={{ py: 0.5 }}>
        <CircularProgress size={16} aria-hidden="true" />
        <Text variant="caption" color="text.secondary">{resolvedLabel}</Text>
      </Stack>
    );
  }
  return <LoadingState label={resolvedLabel} variant="skeleton" rows={rows} />;
}

export interface LocalizedErrorStateProps {
  message: string;
  onRetry?: () => void;
  inline?: boolean;
  stale?: boolean;
  permissionDenied?: boolean;
}

export function LocalizedErrorState({ message, onRetry, inline = false, stale = false, permissionDenied = false }: LocalizedErrorStateProps) {
  const { t } = useTranslation();
  const denied = permissionDenied || message === t('common.errors.permissionDenied');
  return (
    <Alert
      severity={denied || stale ? 'warning' : 'error'}
      role="alert"
      action={onRetry ? <Button tone="neutral" size="small" onClick={onRetry}>{t('common.retry')}</Button> : undefined}
      sx={{ alignItems: 'center', ...(inline ? { py: 0.5, '& .MuiAlert-message': { width: '100%' } } : {}) }}
    >
      <Stack direction="row" spacing={1} alignItems="center" sx={{ flexWrap: 'wrap' }}>
        {stale ? <FreshnessBadge freshness="stale" label={t('common.domain.stale')} /> : null}
        <Text component="span" variant="secondary" sx={{ color: 'inherit' }}>{message}</Text>
      </Stack>
    </Alert>
  );
}
