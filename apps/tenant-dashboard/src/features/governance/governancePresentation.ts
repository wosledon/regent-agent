import type { ChipTone } from '@regent/ui-shared';

const riskTones: Record<string, ChipTone> = {
  critical: 'error',
  high: 'error',
  sev1: 'error',
  medium: 'warning',
  moderate: 'warning',
  sev2: 'warning',
  low: 'info',
  info: 'info',
  sev3: 'info',
  sev4: 'neutral'
};

const statusTones: Record<string, ChipTone> = {
  pending: 'warning',
  open: 'warning',
  contained: 'warning',
  softdeleted: 'warning',
  active: 'success',
  approved: 'success',
  admitted: 'success',
  resolved: 'success',
  closed: 'success',
  completed: 'success',
  eligible: 'success',
  pass: 'success',
  released: 'success',
  restored: 'success',
  rejected: 'error',
  blocked: 'error',
  failed: 'error',
  invalid: 'error',
  expired: 'error',
  draft: 'neutral',
  unknown: 'neutral',
  archived: 'neutral',
  recovered: 'info',
  underreview: 'info',
  reviewing: 'info'
};

export function governanceRiskTone(value?: string): ChipTone {
  return riskTones[value?.toLowerCase() ?? ''] ?? 'neutral';
}

export function governanceStatusTone(value?: string): ChipTone {
  return statusTones[value?.toLowerCase() ?? ''] ?? 'info';
}
