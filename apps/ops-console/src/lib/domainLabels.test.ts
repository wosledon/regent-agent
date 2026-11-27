import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import i18n from './i18n';
import { domainLabelKey, domainLabelKeys, useDomainLabels } from './domainLabels';

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

const domainPrefix = 'common.domain.';
const requiredDomainKeys = [
  'common.domain.active', 'common.domain.suspended', 'common.domain.pending', 'common.domain.approved',
  'common.domain.revoked', 'common.domain.completed', 'common.domain.blocked', 'common.domain.running',
  'common.domain.pendingApproval', 'common.domain.verified', 'common.domain.failed', 'common.domain.operational',
  'common.domain.degraded', 'common.domain.down', 'common.domain.unknown', 'common.domain.majorOutage',
  'common.domain.open', 'common.domain.resolved', 'common.domain.sev1', 'common.domain.sev2',
  'common.domain.sev3', 'common.domain.sev4', 'common.domain.l0', 'common.domain.l1', 'common.domain.l2',
  'common.domain.l3', 'common.domain.l4', 'common.domain.firing', 'common.domain.acknowledged',
  'common.domain.silenced', 'common.domain.off', 'common.domain.local', 'common.domain.otlp',
  'common.domain.elastic', 'common.domain.green', 'common.domain.yellow', 'common.domain.red',
  'common.domain.reachable', 'common.domain.unreachable', 'common.domain.unavailable', 'common.domain.full',
  'common.domain.tenant', 'common.domain.readOnly', 'common.domain.diagnostics', 'common.domain.covered',
  'common.domain.uncovered', 'common.domain.measured', 'common.domain.notConfigured',
  'common.domain.unverified', 'common.domain.missed', 'common.domain.passed', 'common.domain.stale'
];

function variantsFor(key: string): string[] {
  const canonical = key.slice(domainPrefix.length);
  const separated = canonical
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Za-z])([0-9])/g, '$1 $2')
    .replace(/([0-9])([A-Za-z])/g, '$1 $2')
    .toLowerCase();
  return [...new Set([
    canonical,
    `${canonical[0].toUpperCase()}${canonical.slice(1)}`,
    canonical.toUpperCase(),
    separated.replace(/\s+/g, '_'),
    separated.replace(/\s+/g, '-'),
    separated
  ])];
}

const normalizationCases = domainLabelKeys.flatMap(key =>
  variantsFor(key).map(value => [value, key] as const));

describe('Ops domain labels', () => {
  it('maps every domain value required by the Ops business UI', () => {
    expect(domainLabelKeys).toEqual(expect.arrayContaining(requiredDomainKeys));
  });

  it.each(normalizationCases)('normalizes %s for display only', (value, key) => {
    expect(domainLabelKey(value)).toBe(key);
  });

  it('localizes every mapped value and falls back to localized Unknown', async () => {
    const { result } = renderHook(() => useDomainLabels());
    for (const key of domainLabelKeys) {
      expect(result.current(key.slice(domainPrefix.length)), key).toBe(i18n.t(key));
    }
    expect(result.current('future-state')).toBe('Unknown');

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    for (const key of domainLabelKeys) {
      expect(result.current(key.slice(domainPrefix.length)), key).toBe(i18n.t(key));
    }
    expect(result.current('future-state')).toBe('未知');
  });

  it('has a non-empty resource for every mapped domain value', () => {
    for (const key of domainLabelKeys) {
      expect(i18n.exists(key), key).toBe(true);
      expect(i18n.t(key).trim(), key).not.toBe('');
    }
  });
});
