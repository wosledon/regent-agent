import type { TFunction } from 'i18next';
import type { OpsFormatter } from './formatting';

export function localizedCount(
  t: TFunction,
  format: OpsFormatter,
  resourceKey: string,
  count: number,
  values: Record<string, string | number> = {}
): string {
  const rawCount = Number.isFinite(count) ? count : 0;
  return t(resourceKey, { ...values, count: rawCount, value: format.number(rawCount) });
}
