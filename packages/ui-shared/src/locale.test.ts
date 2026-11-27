import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  applyDocumentLocale,
  initialLocale,
  readLocalePreference,
  resolveLocale,
  storeLocalePreference
} from './locale';

beforeEach(() => {
  localStorage.clear();
  document.documentElement.lang = '';
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('locale utilities', () => {
  it('normalizes supported browser languages and falls back to zh-CN', () => {
    expect(resolveLocale('zh-TW')).toBe('zh-CN');
    expect(resolveLocale('EN-us')).toBe('en-US');
    expect(resolveLocale('fr-FR', null, undefined)).toBe('zh-CN');
  });

  it('prefers a valid saved preference over the browser language', () => {
    localStorage.setItem('regent.ui.locale', 'en-US');
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('zh-CN');
    expect(initialLocale()).toBe('en-US');
  });

  it('uses the browser language when no valid preference is saved', () => {
    localStorage.setItem('regent.ui.locale', 'fr-FR');
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-GB');
    expect(initialLocale()).toBe('en-US');
  });

  it('guards unavailable or failing storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('storage denied'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('storage denied'); });
    expect(readLocalePreference()).toBeNull();
    expect(() => storeLocalePreference('en-US')).not.toThrow();

    vi.unstubAllGlobals();
    vi.stubGlobal('localStorage', undefined);
    expect(readLocalePreference()).toBeNull();
    expect(() => storeLocalePreference('zh-CN')).not.toThrow();
  });

  it('persists display preference and applies the document language', () => {
    storeLocalePreference('en-US');
    applyDocumentLocale('en-US');
    expect(readLocalePreference()).toBe('en-US');
    expect(document.documentElement.lang).toBe('en-US');
  });
});
