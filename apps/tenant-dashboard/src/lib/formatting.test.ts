import { describe, expect, it } from 'vitest';
import { createTenantFormatter, DEFAULT_TENANT_TIME_ZONE, resolveTenantTimeZone, toFiniteNumber } from './formatting';

describe('tenant formatting', () => {
  it('formats dates and numbers with the active locale and persisted tenant time zone', () => {
    const formatter = createTenantFormatter('en-US', 'America/New_York', 'Not available');
    expect(formatter.dateTime('2026-11-25T12:00:00Z')).toMatch(/11\/25\/2026/);
    expect(formatter.dateTime('2026-11-25T12:00:00Z')).toMatch(/07:00:00/);
    expect(formatter.number(12345.5)).toBe('12,345.5');
    expect(formatter.number(12345, { notation: 'compact', maximumFractionDigits: 1 })).toBe('12.3K');
    expect(formatter.percent(0.555)).toBe('55.5%');

    const chinese = createTenantFormatter('zh-CN', 'Asia/Shanghai', '暂无数据');
    expect(chinese.dateTime('2026-11-25T12:00:00Z')).toMatch(/2026\/11\/25/);
    expect(chinese.dateTime('2026-11-25T12:00:00Z')).toMatch(/20:00:00/);
    expect(chinese.number(12345.5)).toBe('12,345.5');
    expect(chinese.number(12345, { notation: 'compact', maximumFractionDigits: 1 })).toBe('1.2万');
    expect(chinese.percent(0.555)).toBe('55.5%');
  });

  it('formats minor and major money units with explicit ISO currency codes', () => {
    const formatter = createTenantFormatter('en-US', 'UTC', 'Not available');
    expect(formatter.moneyMinor(12345, 'USD')).toBe('USD 123.45');
    expect(formatter.moneyMajor(123.45, 'USD')).toBe('USD 123.45');
    expect(formatter.moneyMinor(12345, 'CNY')).toBe('CNY 123.45');
    expect(formatter.moneyMinor(12345, 'INVALID')).toBe('Not available');
    expect(formatter.moneyMinor(12345, 'ZZZ')).toBe('Not available');
    expect(formatter.moneyMinor(12345, 'BTC')).toBe('Not available');
    expect(formatter.moneyMinor(12345, 'usd')).toBe('Not available');
    expect(formatter.moneyMinor(12345, 'JPY')).toBe('JPY 12,345');
    expect(formatter.moneyMinor(12345, 'KWD')).toBe('KWD 12.345');
  });

  it('uses localized fallbacks for invalid values and a deterministic time-zone fallback', () => {
    const formatter = createTenantFormatter('zh-CN', 'Invalid/Zone', '暂无数据');
    expect(formatter.date('not-a-date')).toBe('暂无数据');
    expect(formatter.dateTime(undefined)).toBe('暂无数据');
    expect(formatter.number(Number.NaN)).toBe('暂无数据');
    expect(formatter.percent(null)).toBe('暂无数据');
    expect(toFiniteNumber(null)).toBeNull();
    expect(toFiniteNumber(undefined)).toBeNull();
    expect(toFiniteNumber('')).toBeNull();
    expect(toFiniteNumber('12.5')).toBe(12.5);
    expect(toFiniteNumber('not-a-number')).toBeNull();
    expect(resolveTenantTimeZone(undefined)).toBe(DEFAULT_TENANT_TIME_ZONE);
    expect(resolveTenantTimeZone('Invalid/Zone')).toBe(DEFAULT_TENANT_TIME_ZONE);
    expect(resolveTenantTimeZone('Europe/London')).toBe('Europe/London');
  });
});
