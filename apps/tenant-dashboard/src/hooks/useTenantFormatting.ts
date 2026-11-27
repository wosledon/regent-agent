import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { resolveLocale } from '@regent/ui-shared';
import { createTenantFormatter } from '../lib/formatting';
import { useTenantLocalization } from './useTenantLocalization';

export function useTenantFormatting(enabled = true) {
  const { t, i18n } = useTranslation();
  const localization = useTenantLocalization(enabled);
  const locale = resolveLocale(i18n.resolvedLanguage ?? i18n.language);
  const timeZone = localization.data?.timeZone;
  const unavailable = t('common.notAvailable');
  return useMemo(() => createTenantFormatter(locale, timeZone, unavailable), [locale, timeZone, unavailable]);
}
