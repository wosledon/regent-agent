import type { OperatorSession } from './api';

export function canWriteOperations(user: OperatorSession | null): boolean {
  if (!user || user.role === 'Ops Viewer') return false;
  const capabilities = user.capabilities ?? [];
  return !capabilities.includes('OperatorReader') || capabilities.includes('OperatorWriter');
}
