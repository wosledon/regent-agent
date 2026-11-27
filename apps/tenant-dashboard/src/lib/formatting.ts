import { resolveLocale } from '@regent/ui-shared';

export const DEFAULT_TENANT_TIME_ZONE = 'UTC';

type DateInput = string | number | Date | null | undefined;
type NumberInput = number | null | undefined;

export interface TenantFormatter {
  date: (value: string | number | Date | null | undefined, options?: Intl.DateTimeFormatOptions) => string;
  time: (value: string | number | Date | null | undefined, options?: Intl.DateTimeFormatOptions) => string;
  dateTime: (value: string | number | Date | null | undefined, options?: Intl.DateTimeFormatOptions) => string;
  number: (value: number | null | undefined, options?: Intl.NumberFormatOptions) => string;
  percent: (ratio: number | null | undefined, options?: Intl.NumberFormatOptions) => string;
  moneyMinor: (minorUnits: number | null | undefined, currency: string) => string;
  moneyMajor: (majorUnits: number | null | undefined, currency: string) => string;
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

function validNumber(value: unknown): number | null {
  return toFiniteNumber(value);
}

export function resolveTenantTimeZone(value: string | null | undefined): string {
  const candidate = value?.trim();
  if (!candidate) return DEFAULT_TENANT_TIME_ZONE;
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: candidate }).format(new Date(0));
    return candidate;
  } catch {
    // Documentation is out of scope for this iteration: UTC is the deterministic fallback for unavailable or invalid tenant zones.
    return DEFAULT_TENANT_TIME_ZONE;
  }
}

// Use the runtime's ISO 4217 currency set so arbitrary three-letter codes cannot become money labels.
const supportedCurrencies = new Set(typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('currency') : []);

export function isSupportedCurrency(currency: string): boolean {
  return supportedCurrencies.has(currency);
}

function currencyMinorUnits(locale: string, currency: string): number | null {
  if (!isSupportedCurrency(currency)) return null;
  try {
    return new Intl.NumberFormat(locale, { style: 'currency', currency }).resolvedOptions().maximumFractionDigits ?? 2;
  } catch {
    return null;
  }
}

export function createTenantFormatter(localeValue: string, timeZoneValue: string | null | undefined, unavailable: string): TenantFormatter {
  const locale = resolveLocale(localeValue);
  const timeZone = resolveTenantTimeZone(timeZoneValue);
  const date = (value: DateInput, options: Intl.DateTimeFormatOptions = {}) => {
    const parsed = validDate(value);
    if (!parsed) return unavailable;
    try {
      return new Intl.DateTimeFormat(locale, { timeZone, year: 'numeric', month: '2-digit', day: '2-digit', ...options }).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const time = (value: DateInput, options: Intl.DateTimeFormatOptions = {}) => {
    const parsed = validDate(value);
    if (!parsed) return unavailable;
    try {
      return new Intl.DateTimeFormat(locale, { timeZone, hour: '2-digit', minute: '2-digit', second: '2-digit', ...options }).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const dateTime = (value: DateInput, options: Intl.DateTimeFormatOptions = {}) => {
    const parsed = validDate(value);
    if (!parsed) return unavailable;
    try {
      return new Intl.DateTimeFormat(locale, { timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', ...options }).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const number = (value: NumberInput, options: Intl.NumberFormatOptions = {}) => {
    const parsed = validNumber(value);
    if (parsed === null) return unavailable;
    try {
      return new Intl.NumberFormat(locale, options).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const percent = (value: NumberInput, options: Intl.NumberFormatOptions = {}) => {
    const parsed = validNumber(value);
    if (parsed === null) return unavailable;
    try {
      return new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1, ...options }).format(parsed);
    } catch {
      return unavailable;
    }
  };
  const money = (value: NumberInput, currency: string, unit: 'minor' | 'major') => {
    const parsed = validNumber(value);
    const minorUnits = currencyMinorUnits(locale, currency);
    if (parsed === null || minorUnits === null) return unavailable;
    const majorUnits = unit === 'minor' ? parsed / 10 ** minorUnits : parsed;
    try {
      return new Intl.NumberFormat(locale, {
        style: 'currency', currency, currencyDisplay: 'code', minimumFractionDigits: minorUnits, maximumFractionDigits: minorUnits
      }).format(majorUnits);
    } catch {
      return unavailable;
    }
  };
  return {
    date,
    time,
    dateTime,
    number,
    percent,
    moneyMinor: (value, currency) => money(value, currency, 'minor'),
    moneyMajor: (value, currency) => money(value, currency, 'major')
  };
}
