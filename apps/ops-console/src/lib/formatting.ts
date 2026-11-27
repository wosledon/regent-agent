import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { resolveLocale } from '@regent/ui-shared';

export type OpsDateInput = string | number | Date | null | undefined;
export type OpsDurationUnit = 'second' | 'minute' | 'hour' | 'millisecond';

export interface OpsFormatterOptions {
  timeZone?: string;
  unavailable?: string;
}

export interface OpsFormatter {
  date: (value: OpsDateInput, options?: Intl.DateTimeFormatOptions) => string;
  time: (value: OpsDateInput, options?: Intl.DateTimeFormatOptions) => string;
  dateTime: (value: OpsDateInput, options?: Intl.DateTimeFormatOptions) => string;
  number: (value: number | null | undefined, options?: Intl.NumberFormatOptions) => string;
  percent: (value: number | null | undefined, options?: Intl.NumberFormatOptions) => string;
  duration: (value: number | null | undefined, unit: OpsDurationUnit) => string;
  list: (values: readonly string[]) => string;
}

function validDate(value: unknown): Date | null {
  if (value === null || value === undefined || value === '') return null;
  const date = value instanceof Date ? value : new Date(value as string | number);
  return Number.isFinite(date.getTime()) ? date : null;
}

export function toFiniteNumber(value: unknown): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value !== 'string' || !value.trim()) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function validTimeZone(value: string | undefined): string | undefined {
  if (!value) return undefined;
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: value }).format(new Date(0));
    return value;
  } catch {
    return undefined;
  }
}

export function createOpsFormatter(localeValue: string, options: OpsFormatterOptions = {}): OpsFormatter {
  const locale = resolveLocale(localeValue);
  const unavailable = options.unavailable ?? '—';
  const timeZone = validTimeZone(options.timeZone);
  const date = (value: OpsDateInput, formatOptions: Intl.DateTimeFormatOptions = {}) => {
    const parsed = validDate(value);
    if (!parsed) return unavailable;
    try {
      return new Intl.DateTimeFormat(locale, {
        ...(timeZone ? { timeZone } : {}), year: 'numeric', month: '2-digit', day: '2-digit', ...formatOptions
      }).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const time = (value: OpsDateInput, formatOptions: Intl.DateTimeFormatOptions = {}) => {
    const parsed = validDate(value);
    if (!parsed) return unavailable;
    try {
      return new Intl.DateTimeFormat(locale, {
        ...(timeZone ? { timeZone } : {}), hour: '2-digit', minute: '2-digit', second: '2-digit', ...formatOptions
      }).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const dateTime = (value: OpsDateInput, formatOptions: Intl.DateTimeFormatOptions = {}) => {
    const parsed = validDate(value);
    if (!parsed) return unavailable;
    try {
      return new Intl.DateTimeFormat(locale, {
        ...(timeZone ? { timeZone } : {}),
        year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
        ...formatOptions
      }).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const number = (value: number | null | undefined, formatOptions: Intl.NumberFormatOptions = {}) => {
    const parsed = toFiniteNumber(value);
    if (parsed === null) return unavailable;
    try {
      return new Intl.NumberFormat(locale, formatOptions).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const percent = (value: number | null | undefined, formatOptions: Intl.NumberFormatOptions = {}) => {
    const parsed = toFiniteNumber(value);
    if (parsed === null) return unavailable;
    try {
      return new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1, ...formatOptions }).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const duration = (value: number | null | undefined, unit: OpsDurationUnit) => {
    const parsed = toFiniteNumber(value);
    if (parsed === null) return unavailable;
    try {
      return new Intl.NumberFormat(locale, { style: 'unit', unit, unitDisplay: 'short' }).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const list = (values: readonly string[]) => {
    if (!values.length) return unavailable;
    try {
      return new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' }).format(values);
    } catch {
      return unavailable;
    }
  };
  return { date, time, dateTime, number, percent, duration, list };
}

export function useOpsFormatter(): OpsFormatter {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? i18n.language;
  const unavailable = t('common.unavailable');
  return useMemo(() => createOpsFormatter(locale, { unavailable }), [locale, unavailable]);
}
