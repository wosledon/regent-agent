import { Alert } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { Button } from '@regent/ui-shared';
import { localizedErrorMessage } from '../../lib/errorMessages';

interface LocalizedErrorStateProps {
  error: unknown;
  onRetry?: () => void;
}

export function LocalizedErrorState({ error, onRetry }: LocalizedErrorStateProps) {
  const { t } = useTranslation();
  return (
    <Alert severity="error" action={onRetry ? <Button tone="neutral" onClick={onRetry}>{t('common.retry')}</Button> : undefined}>
      {localizedErrorMessage(error, t)}
    </Alert>
  );
}
