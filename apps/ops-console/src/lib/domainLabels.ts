import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';

const labelKeys: Record<string, string> = {
  active: 'common.domain.active',
  suspended: 'common.domain.suspended',
  pending: 'common.domain.pending',
  approved: 'common.domain.approved',
  revoked: 'common.domain.revoked',
  completed: 'common.domain.completed',
  blocked: 'common.domain.blocked',
  running: 'common.domain.running',
  failed: 'common.domain.failed',
  pendingapproval: 'common.domain.pendingApproval',
  verified: 'common.domain.verified',
  operational: 'common.domain.operational',
  degraded: 'common.domain.degraded',
  down: 'common.domain.down',
  unknown: 'common.domain.unknown',
  majoroutage: 'common.domain.majorOutage',
  open: 'common.domain.open',
  resolved: 'common.domain.resolved',
  sev1: 'common.domain.sev1',
  sev2: 'common.domain.sev2',
  sev3: 'common.domain.sev3',
  sev4: 'common.domain.sev4',
  l0: 'common.domain.l0',
  l1: 'common.domain.l1',
  l2: 'common.domain.l2',
  l3: 'common.domain.l3',
  l4: 'common.domain.l4',
  firing: 'common.domain.firing',
  acknowledged: 'common.domain.acknowledged',
  silenced: 'common.domain.silenced',
  off: 'common.domain.off',
  local: 'common.domain.local',
  otlp: 'common.domain.otlp',
  elastic: 'common.domain.elastic',
  green: 'common.domain.green',
  yellow: 'common.domain.yellow',
  red: 'common.domain.red',
  reachable: 'common.domain.reachable',
  unreachable: 'common.domain.unreachable',
  unavailable: 'common.domain.unavailable',
  full: 'common.domain.full',
  tenant: 'common.domain.tenant',
  readonly: 'common.domain.readOnly',
  diagnostics: 'common.domain.diagnostics',
  covered: 'common.domain.covered',
  uncovered: 'common.domain.uncovered',
  measured: 'common.domain.measured',
  notconfigured: 'common.domain.notConfigured',
  unverified: 'common.domain.unverified',
  missed: 'common.domain.missed',
  passed: 'common.domain.passed',
  stale: 'common.domain.stale'
};

function canonicalDomainValue(value: string): string {
  return value.trim()
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '');
}

const normalizedLabelKeys = new Map(Object.entries(labelKeys).map(([value, key]) => [canonicalDomainValue(value), key]));

export const domainLabelKeys = Object.values(labelKeys);

export function domainLabelKey(value: string): string | null {
  return normalizedLabelKeys.get(canonicalDomainValue(value)) ?? null;
}

export function useDomainLabels() {
  const { t } = useTranslation();
  return useCallback((value: string | null | undefined) => {
    if (!value) return t('common.domain.unknown');
    const key = domainLabelKey(value);
    return key ? t(key) : t('common.domain.unknown');
  }, [t]);
}
