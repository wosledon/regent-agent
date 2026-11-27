import type { TFunction } from 'i18next';
import { supportedLocales } from '@regent/ui-shared';
import { isSupportedCurrency } from './formatting';
import {
  currencyError, finiteNumberError, genericReasonConstants, integerRangeError, jsonError, nonNegativeNumberError,
  numberRangeError, positiveIntegerError, requiredTextError, specificTextError, timeError, timeZoneError
} from './frontendValidation';

const genericReasonResourceKeys = [
  'validation.defaultReviewed', 'validation.defaultOkay', 'validation.defaultNotApplicable',
  'common.review', 'common.approve', 'common.reject', 'common.yes', 'common.no',
  'common.domain.approved', 'common.domain.rejected', 'common.domain.acknowledged', 'common.domain.none',
  'recruitmentPage.defaultRejectionReason', 'recruitmentPage.defaultAppealResolution'
] as const;

export function localizedGenericReasonDefaults(t: TFunction): string[] {
  const localizedDefaults = supportedLocales.flatMap(locale =>
    genericReasonResourceKeys.map(key => t(key, { lng: locale })));
  return [...genericReasonConstants, ...localizedDefaults];
}

function adapt(error: string | null, t: TFunction, comparisons: Record<string, string>, values: Record<string, string | number> = {}): string | null {
  if (!error) return null;
  const key = comparisons[error];
  return key ? t(key, values) : error;
}

export function localizedRequiredTextError(value: string, label: string, t: TFunction): string | null {
  return adapt(requiredTextError(value, 'Value'), t, { 'Value is required.': 'common.validation.required' }, { label });
}

export function localizedSpecificTextError(value: string, label: string, defaults: readonly string[], t: TFunction): string | null {
  return adapt(specificTextError(value, 'Value', defaults), t, {
    'Value is required.': 'common.validation.required',
    'Value must be specific rather than a default acknowledgement.': 'common.validation.specific'
  }, { label });
}

export function localizedFiniteNumberError(value: string, label: string, t: TFunction): string | null {
  return adapt(finiteNumberError(value, 'Value'), t, {
    'Value is required.': 'common.validation.required', 'Value must be a finite number.': 'common.validation.finiteNumber'
  }, { label });
}

export function localizedNonNegativeNumberError(value: string, label: string, t: TFunction): string | null {
  return adapt(nonNegativeNumberError(value, 'Value'), t, {
    'Value is required.': 'common.validation.required', 'Value must be a finite number.': 'common.validation.finiteNumber',
    'Value must be non-negative.': 'common.validation.nonNegative'
  }, { label });
}

export function localizedNumberRangeError(value: string, label: string, minimum: number, maximum: number, t: TFunction): string | null {
  return adapt(numberRangeError(value, 'Value', minimum, maximum), t, {
    'Value is required.': 'common.validation.required', 'Value must be a finite number.': 'common.validation.finiteNumber',
    [`Value must be between ${minimum} and ${maximum}.`]: 'common.validation.range'
  }, { label, minimum, maximum });
}

export function localizedPositiveIntegerError(value: string, label: string, t: TFunction): string | null {
  return adapt(positiveIntegerError(value, 'Value'), t, {
    'Value is required.': 'common.validation.required', 'Value must be a finite number.': 'common.validation.finiteNumber',
    'Value must be a positive integer.': 'common.validation.positiveInteger'
  }, { label });
}

export function localizedIntegerRangeError(value: string, label: string, minimum: number, maximum: number, t: TFunction): string | null {
  return adapt(integerRangeError(value, 'Value', minimum, maximum), t, {
    'Value is required.': 'common.validation.required', 'Value must be a finite number.': 'common.validation.finiteNumber',
    [`Value must be an integer from ${minimum} to ${maximum}.`]: 'common.validation.integerRange'
  }, { label, minimum, maximum });
}

export function localizedLocaleError(value: string, t: TFunction): string | null {
  return supportedLocales.includes(value.trim() as typeof supportedLocales[number]) ? null : t('common.validation.locale');
}

export function localizedCurrencyError(value: string, t: TFunction): string | null {
  const formatError = adapt(currencyError(value), t, {
    'Currency must use a three-letter ISO currency code.': 'common.validation.currency'
  });
  return formatError ?? (isSupportedCurrency(value.trim()) ? null : t('common.validation.currency'));
}

export function localizedTimeZoneError(value: string, t: TFunction): string | null {
  return adapt(timeZoneError(value), t, {
    'IANA time zone is required.': 'common.validation.timeZoneRequired',
    'Time zone must be a valid IANA time zone.': 'common.validation.timeZoneInvalid'
  });
}

export function localizedTimeError(value: string, label: string, t: TFunction): string | null {
  return adapt(timeError(value, 'Value'), t, { 'Value must use HH:mm.': 'common.validation.time' }, { label });
}

export function localizedJsonError(value: string, label: string, t: TFunction): string | null {
  return adapt(jsonError(value, 'Value'), t, { 'Value must contain valid JSON.': 'common.validation.json' }, { label });
}
