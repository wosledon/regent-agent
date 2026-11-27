import { useCallback, useEffect, useMemo, useRef, useState, type PropsWithChildren } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useLocation, useNavigate } from 'react-router-dom';
import { authBootstrapFailure, buildLoginPath, isPublicAuthRoute } from '@regent/ui-shared';
import { api, OPS_PERMISSION_DENIED_EVENT, OPS_UNAUTHORIZED_EVENT, type OperatorSession } from '../lib/api';
import { AuthContext, type AuthStatus } from './AuthContext';

const legacyStorageKey = 'regent.ops.user';
const permissionMessage = 'Permission denied. Your account is not authorized for Operations.';
const bootstrapErrorMessage = 'Unable to verify your session. Check your connection and retry.';

function clearLegacySession() {
  try { sessionStorage.removeItem(legacyStorageKey); } catch { /* Storage can be unavailable in hardened browser contexts. */ }
}

type BootstrapOutcome =
  | { requestGeneration: number; kind: 'authenticated'; user: OperatorSession }
  | { requestGeneration: number; kind: 'anonymous' }
  | { requestGeneration: number; kind: 'permission-denied' | 'error'; error: unknown };

export function AuthProvider({ children }: PropsWithChildren) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();
  const navigateRef = useRef(navigate);
  const locationRef = useRef(location);
  const generation = useRef(0);
  const loggingOut = useRef(false);
  const [status, setStatus] = useState<AuthStatus>('checking');
  const [user, setUser] = useState<OperatorSession | null>(null);
  const [error, setError] = useState<string | null>(null);

  const clearLocalState = useCallback(() => {
    generation.current += 1;
    clearLegacySession();
    queryClient.clear();
    setUser(null);
    setError(null);
    setStatus('anonymous');
  }, [queryClient]);

  const readSession = useCallback(async (): Promise<BootstrapOutcome> => {
    const requestGeneration = ++generation.current;
    try {
      return { requestGeneration, kind: 'authenticated', user: await api.me() };
    } catch (cause) {
      const failure = authBootstrapFailure(cause);
      return failure === 'anonymous'
        ? { requestGeneration, kind: 'anonymous' }
        : { requestGeneration, kind: failure, error: cause };
    }
  }, []);

  const applyBootstrap = useCallback((outcome: BootstrapOutcome) => {
    if (outcome.requestGeneration !== generation.current) return;
    if (outcome.kind === 'anonymous') {
      clearLocalState();
      return;
    }
    clearLegacySession();
    queryClient.clear();
    setUser(null);
    if (outcome.kind === 'authenticated') {
      setError(null);
      setUser(outcome.user);
      setStatus('authenticated');
      return;
    }
    setError(outcome.kind === 'permission-denied' ? permissionMessage : bootstrapErrorMessage);
    setStatus(outcome.kind);
  }, [clearLocalState, queryClient]);

  const reauthenticate = useCallback(async () => {
    setStatus('checking');
    setUser(null);
    setError(null);
    const outcome = await readSession();
    applyBootstrap(outcome);
    if (outcome.kind === 'error' || outcome.kind === 'permission-denied') throw outcome.error;
  }, [applyBootstrap, readSession]);

  useEffect(() => { navigateRef.current = navigate; }, [navigate]);
  useEffect(() => { locationRef.current = location; }, [location]);
  useEffect(() => {
    clearLegacySession();
    void readSession().then(applyBootstrap);
  }, [applyBootstrap, readSession]);
  useEffect(() => {
    const onUnauthorized = () => {
      clearLocalState();
      const current = locationRef.current;
      if (loggingOut.current || isPublicAuthRoute(current.pathname) || current.pathname === '/status') return;
      const target = `${current.pathname}${current.search}${current.hash}`;
      if (current.pathname !== '/login') navigateRef.current(buildLoginPath(target), { replace: true });
    };
    const onPermissionDenied = () => {
      generation.current += 1;
      clearLegacySession();
      queryClient.clear();
      setUser(null);
      setError(permissionMessage);
      setStatus('permission-denied');
    };
    window.addEventListener(OPS_UNAUTHORIZED_EVENT, onUnauthorized);
    window.addEventListener(OPS_PERMISSION_DENIED_EVENT, onPermissionDenied);
    return () => {
      window.removeEventListener(OPS_UNAUTHORIZED_EVENT, onUnauthorized);
      window.removeEventListener(OPS_PERMISSION_DENIED_EVENT, onPermissionDenied);
    };
  }, [clearLocalState, queryClient]);

  const login = useCallback(async () => {
    await api.login();
    await reauthenticate();
  }, [reauthenticate]);

  const logout = useCallback(async () => {
    loggingOut.current = true;
    try {
      await api.logout();
    } catch {
      // Local sign-out remains authoritative even when the server is unavailable.
    } finally {
      clearLocalState();
      navigate('/login', { replace: true });
      loggingOut.current = false;
    }
  }, [clearLocalState, navigate]);

  const value = useMemo(() => ({ status, user, error, retry: reauthenticate, login, logout }),
    [status, user, error, reauthenticate, login, logout]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
