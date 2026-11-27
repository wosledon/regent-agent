export const supportedLocales = ['zh-CN', 'en-US'] as const;
export type AppLocale = typeof supportedLocales[number];

const storageKey = 'regent.ui.locale';

export function resolveLocale(...candidates: Array<string | null | undefined>): AppLocale {
  for (const candidate of candidates) {
    const normalized = candidate?.trim().toLowerCase();
    if (normalized?.startsWith('zh')) return 'zh-CN';
    if (normalized?.startsWith('en')) return 'en-US';
  }
  return 'zh-CN';
}

export function readLocalePreference(): AppLocale | null {
  try {
    const value = globalThis.localStorage?.getItem(storageKey);
    return value === 'zh-CN' || value === 'en-US' ? value : null;
  } catch {
    return null;
  }
}

export function storeLocalePreference(locale: AppLocale): void {
  try {
    globalThis.localStorage?.setItem(storageKey, locale);
  } catch {
    // Storage can be unavailable in hardened browser contexts; the in-memory locale still applies.
  }
}

export function applyDocumentLocale(locale: AppLocale): void {
  if (typeof document !== 'undefined') document.documentElement.lang = locale;
}

export function applyLocalePreference(locale: AppLocale): void {
  storeLocalePreference(locale);
  applyDocumentLocale(locale);
}

export function initialLocale(): AppLocale {
  const browserLocale = typeof navigator === 'undefined' ? undefined : navigator.language;
  return resolveLocale(readLocalePreference(), browserLocale);
}
