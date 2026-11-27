import { useCallback, useEffect, useMemo, useRef, useState, type PropsWithChildren } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { authBootstrapFailure, buildLoginPath } from '@regent/ui-shared';
import { api, TENANT_PERMISSION_DENIED_EVENT, TENANT_UNAUTHORIZED_EVENT, type TenantSessionSummary } from '../lib/api-client/api';
import {
  AuthContext,
  deniedEffectiveAccess,
  isRestrictedSessionKind,
  type AuthStatus,
  type CollaborationScope,
  type EffectiveAccess,
  type SessionSummary
} from './AuthContext';

const legacyStorageKey = 'regent.session.summary';
const permissionMessage = 'Permission denied. Your account cannot access this workspace.';
const bootstrapErrorMessage = 'Unable to verify your session. Check your connection and retry.';

function clearLegacySession() {
  try { sessionStorage.removeItem(legacyStorageKey); } catch { /* Storage can be unavailable in hardened browser contexts. */ }
}

type BootstrapOutcome =
  | { requestGeneration: number; kind: 'authenticated'; summary: TenantSessionSummary }
  | { requestGeneration: number; kind: 'anonymous' }
  | { requestGeneration: number; kind: 'permission-denied' | 'error'; error: unknown };

function mapEffectiveAccess(value: unknown): EffectiveAccess {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return deniedEffectiveAccess;
  const candidate = value as Record<string, unknown>;
  const scope = candidate.collaboration_scope;
  if (typeof candidate.can_read_chat !== 'boolean'
    || typeof candidate.can_read_collaboration !== 'boolean'
    || (scope !== 'none' && scope !== 'participant' && scope !== 'observer')) return deniedEffectiveAccess;
  const canReadChat = candidate.can_read_chat;
  const canReadCollaboration = candidate.can_read_collaboration;
  if ((scope === 'none' && (canReadChat || canReadCollaboration))
    || (scope === 'observer' && (!canReadChat || !canReadCollaboration))) return deniedEffectiveAccess;
  const collaborationScope: CollaborationScope = scope;
  return { canReadChat, canReadCollaboration, collaborationScope };
}

function summaryFromSession(result: TenantSessionSummary): SessionSummary {
  return {
    email: result.email,
    displayName: result.display_name,
    tenantId: result.tenant_id,
    role: result.role,
    authMethod: result.auth_method,
    sessionKind: result.session_kind,
    ...(result.capabilities ? { capabilities: result.capabilities } : {}),
    effectiveAccess: isRestrictedSessionKind(result.session_kind)
      ? deniedEffectiveAccess
      : mapEffectiveAccess(result.effective_access)
  };
}

export function AuthProvider({ children }: PropsWithChildren) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const navigateRef = useRef(navigate);
  const generation = useRef(0);
  const loggingOut = useRef(false);
  const [status, setStatus] = useState<AuthStatus>('checking');
  const [session, setSession] = useState<SessionSummary | null>(null);
  const [error, setError] = useState<string | null>(null);

  const clearLocalState = useCallback(() => {
    generation.current += 1;
    clearLegacySession();
    queryClient.clear();
    setSession(null);
    setError(null);
    setStatus('anonymous');
  }, [queryClient]);

  const readSession = useCallback(async (): Promise<BootstrapOutcome> => {
    const requestGeneration = ++generation.current;
    try {
      return { requestGeneration, kind: 'authenticated', summary: await api.session() };
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
    setSession(null);
    if (outcome.kind === 'authenticated') {
      setError(null);
      setSession(summaryFromSession(outcome.summary));
      setStatus('authenticated');
      return;
    }
    setError(outcome.kind === 'permission-denied' ? permissionMessage : bootstrapErrorMessage);
    setStatus(outcome.kind);
  }, [clearLocalState, queryClient]);

  const reauthenticate = useCallback(async () => {
    queryClient.clear();
    setStatus('checking');
    setSession(null);
    setError(null);
    const outcome = await readSession();
    applyBootstrap(outcome);
    if (outcome.kind === 'error' || outcome.kind === 'permission-denied') throw outcome.error;
  }, [applyBootstrap, queryClient, readSession]);

  useEffect(() => { navigateRef.current = navigate; }, [navigate]);
  useEffect(() => {
    clearLegacySession();
    void readSession().then(applyBootstrap);
  }, [applyBootstrap, readSession]);
  useEffect(() => {
    const onUnauthorized = () => {
      clearLocalState();
      if (loggingOut.current) return;
      const target = `${window.location.pathname}${window.location.search}${window.location.hash}`;
      if (window.location.pathname !== '/login') navigateRef.current(buildLoginPath(target), { replace: true });
    };
    const onPermissionDenied = () => {
      generation.current += 1;
      clearLegacySession();
      queryClient.clear();
      setSession(null);
      setError(permissionMessage);
      setStatus('permission-denied');
    };
    window.addEventListener(TENANT_UNAUTHORIZED_EVENT, onUnauthorized);
    window.addEventListener(TENANT_PERMISSION_DENIED_EVENT, onPermissionDenied);
    return () => {
      window.removeEventListener(TENANT_UNAUTHORIZED_EVENT, onUnauthorized);
      window.removeEventListener(TENANT_PERMISSION_DENIED_EVENT, onPermissionDenied);
    };
  }, [clearLocalState, queryClient]);

  const login = useCallback(async (email: string) => {
    await api.devLogin(email);
    await reauthenticate();
  }, [reauthenticate]);

  const passwordLogin = useCallback(async (email: string, password: string) => {
    await api.passwordLogin(email, password);
    await reauthenticate();
  }, [reauthenticate]);

  const register = useCallback(async (email: string, password: string, displayName: string, companyName: string) => {
    const result = await api.register(email, password, displayName, companyName);
    await reauthenticate();
    return result.recovery_codes;
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

  const switchTenant = useCallback(async (tenantId: string) => {
    await api.switchTenant(tenantId);
    queryClient.clear();
    await reauthenticate();
  }, [queryClient, reauthenticate]);

  const emergencyLogin = useCallback(async (tenantId: string, credential: string, purpose: string) => {
    const result = await api.useEmergencyAccess(tenantId, credential, purpose);
    await reauthenticate();
    return result.rotatedCredential;
  }, [reauthenticate]);

  const value = useMemo(() => ({
    status, session, error, retry: reauthenticate, login, passwordLogin, register, logout, switchTenant, emergencyLogin
  }), [status, session, error, reauthenticate, login, passwordLogin, register, logout, switchTenant, emergencyLogin]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
