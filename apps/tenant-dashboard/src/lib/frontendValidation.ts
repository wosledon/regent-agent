export const genericReasonConstants = [
  'reviewed from tenant dashboard', 'approve', 'approved', 'reject', 'rejected', 'reviewed', 'ok', 'n/a', 'none'
] as const;

export function requiredTextError(value: string, label: string): string | null {
  return value.trim() ? null : `${label} is required.`;
}

function normalizedText(value: string): string {
  return value.trim().toLowerCase().replace(/[.!?。！？]+$/, '');
}

export function specificTextError(value: string, label: string, defaultValues: readonly string[]): string | null {
  const requiredError = requiredTextError(value, label);
  if (requiredError) return requiredError;
  const normalized = normalizedText(value);
  return defaultValues.some(defaultValue => normalizedText(defaultValue) === normalized)
    ? `${label} must be specific rather than a default acknowledgement.`
    : null;
}

export function finiteNumberError(value: string, label: string): string | null {
  if (!value.trim()) return `${label} is required.`;
  return Number.isFinite(Number(value)) ? null : `${label} must be a finite number.`;
}

export function nonNegativeNumberError(value: string, label: string): string | null {
  const error = finiteNumberError(value, label);
  if (error) return error;
  return Number(value) >= 0 ? null : `${label} must be non-negative.`;
}

export function numberRangeError(value: string, label: string, minimum: number, maximum: number): string | null {
  const error = finiteNumberError(value, label);
  if (error) return error;
  const number = Number(value);
  return number >= minimum && number <= maximum ? null : `${label} must be between ${minimum} and ${maximum}.`;
}

export function positiveIntegerError(value: string, label: string): string | null {
  const error = finiteNumberError(value, label);
  if (error) return error;
  const number = Number(value);
  return Number.isSafeInteger(number) && number > 0 ? null : `${label} must be a positive integer.`;
}

export function integerRangeError(value: string, label: string, minimum: number, maximum = Number.MAX_SAFE_INTEGER): string | null {
  const error = finiteNumberError(value, label);
  if (error) return error;
  const number = Number(value);
  return Number.isSafeInteger(number) && number >= minimum && number <= maximum
    ? null
    : `${label} must be an integer from ${minimum} to ${maximum}.`;
}

export function currencyError(value: string): string | null {
  return /^[A-Z]{3}$/.test(value.trim()) ? null : 'Currency must use a three-letter ISO currency code.';
}

export function timeZoneError(value: string): string | null {
  const timeZone = value.trim();
  if (!timeZone) return 'IANA time zone is required.';
  if (!/^[A-Za-z][A-Za-z0-9_+-]*(?:\/[A-Za-z0-9_+-]+)*$/.test(timeZone)) {
    return 'Time zone must be a valid IANA time zone.';
  }
  try {
    new Intl.DateTimeFormat('en-US', { timeZone }).format(new Date(0));
    return null;
  } catch {
    return 'Time zone must be a valid IANA time zone.';
  }
}

export function timeError(value: string, label: string): string | null {
  const match = /^(\d{2}):(\d{2})$/.exec(value.trim());
  if (!match) return `${label} must use HH:mm.`;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  return hours >= 0 && hours <= 23 && minutes >= 0 && minutes <= 59 ? null : `${label} must use HH:mm.`;
}

export function jsonError(value: string, label: string): string | null {
  if (!value.trim()) return `${label} must contain valid JSON.`;
  try {
    JSON.parse(value);
    return null;
  } catch {
    return `${label} must contain valid JSON.`;
  }
}
