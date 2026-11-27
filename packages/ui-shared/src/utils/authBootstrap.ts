export type AuthBootstrapFailure = 'anonymous' | 'permission-denied' | 'error';

export function authBootstrapFailure(error: unknown): AuthBootstrapFailure {
  const status = typeof error === 'object' && error !== null && 'status' in error
    ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return 'anonymous';
  if (status === 403) return 'permission-denied';
  return 'error';
}

export function isPublicAuthRoute(pathname: string): boolean {
  const normalized = pathname.replace(/\/+$/, '') || '/';
  return normalized === '/login' || normalized === '/status';
}
