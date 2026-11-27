import { useTranslation } from 'react-i18next';
import { LoadingState } from '@regent/ui-shared';

export function LocalizedLoadingState({ label }: { label?: string }) {
  const { t } = useTranslation();
  return <LoadingState label={label ?? t('common.loading')} />;
}
