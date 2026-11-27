import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import i18n from './i18n';
import { createOpsFormatter, useOpsFormatter } from './formatting';
import { localizedCount } from './localizedCount';

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('Ops formatting', () => {
  it('formats dates in the active locale and explicit IANA time zone', () => {
    const english = createOpsFormatter('en-US', { timeZone: 'Asia/Shanghai', unavailable: '—' });
    const chinese = createOpsFormatter('zh-CN', { timeZone: 'Asia/Shanghai', unavailable: '—' });
    expect(english.dateTime('2026-11-25T10:00:00Z')).toBe('11/25/2026, 06:00:00 PM');
    expect(chinese.dateTime('2026-11-25T10:00:00Z')).toBe('2026/11/25 18:00:00');
  });

  it('uses the runtime time zone by default and localizes invalid values', () => {
    const format = createOpsFormatter('en-US', { unavailable: 'Unavailable' });
    const value = new Date('2026-11-25T10:00:00Z');
    const expected = new Intl.DateTimeFormat('en-US', {
      year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit'
    }).format(value);
    expect(format.dateTime(value)).toBe(expected);
    expect(format.dateTime('not-a-date')).toBe('Unavailable');
    expect(format.number(Number.NaN)).toBe('Unavailable');
    expect(format.percent(null)).toBe('Unavailable');
  });

  it('formats numbers, percentages, durations and lists with Intl', () => {
    const english = createOpsFormatter('en-US', { unavailable: '—' });
    const chinese = createOpsFormatter('zh-CN', { unavailable: '—' });
    expect(english.number(1234567.89)).toBe('1,234,567.89');
    expect(english.percent(0.999, { maximumFractionDigits: 2 })).toBe('99.9%');
    expect(english.duration(5, 'minute')).toBe('5 min');
    expect(english.duration(250, 'millisecond')).toBe('250 ms');
    expect(english.list(['a', 'b'])).toBe('a and b');
    expect(chinese.number(1234567.89)).toBe('1,234,567.89');
    expect(chinese.percent(0.999, { maximumFractionDigits: 2 })).toBe('99.9%');
    expect(chinese.duration(5, 'minute')).toBe('5分钟');
    expect(chinese.duration(250, 'millisecond')).toBe('250毫秒');
    expect(chinese.list(['a', 'b'])).toBe('a和b');
  });

  it('uses the active i18next locale in the formatter hook', async () => {
    const { result } = renderHook(() => useOpsFormatter());
    const english = result.current.dateTime('2026-11-25T10:00:00Z');
    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    const chinese = result.current.dateTime('2026-11-25T10:00:00Z');
    expect(english).not.toBe(chinese);
    expect(chinese).toMatch(/2026/);
  });

  it('selects English singular/plural and natural Chinese count forms', async () => {
    const format = createOpsFormatter('en-US', { unavailable: '—' });
    expect(localizedCount(i18n.t, format, 'overviewPage.tenants', 1)).toBe('1 tenant');
    expect(localizedCount(i18n.t, format, 'overviewPage.tenants', 2)).toBe('2 tenants');

    await i18n.changeLanguage('zh-CN');
    const chinese = createOpsFormatter('zh-CN', { unavailable: '—' });
    expect(localizedCount(i18n.t, chinese, 'overviewPage.tenants', 1)).toBe('1 个租户');
    expect(localizedCount(i18n.t, chinese, 'overviewPage.tenants', 2)).toBe('2 个租户');
  });
});
