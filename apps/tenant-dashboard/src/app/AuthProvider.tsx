import { useCallback, useMemo, useState, type PropsWithChildren } from 'react';
import { api, type AuthResponse } from '../lib/api-client/api';
import { AuthContext, type SessionSummary } from './AuthContext';

const storageKey = 'regent.session.summary';

function readSession(): SessionSummary | null {
  const raw = sessionStorage.getItem(storageKey);
  if (!raw) return null;
  try {
    const value = JSON.parse(raw) as SessionSummary;
    return value.email && value.displayName ? value : null;
  } catch {
    return null;
  }
}

function summaryFromAuth(result: Pick<AuthResponse, 'email' | 'display_name' | 'tenant_id' | 'role' | 'auth_method' | 'session_kind'>): SessionSummary {
  return {
    email: result.email, displayName: result.display_name, tenantId: result.tenant_id,
    role: result.role, authMethod: result.auth_method, sessionKind: result.session_kind
  };
}

export function AuthProvider({ children }: PropsWithChildren) {
  const [session, setSession] = useState<SessionSummary | null>(readSession);
  const acceptAuth = useCallback((result: Pick<AuthResponse, 'email' | 'display_name' | 'tenant_id' | 'role' | 'auth_method' | 'session_kind'>) => {
    const next = summaryFromAuth(result);
    sessionStorage.setItem(storageKey, JSON.stringify(next));
    setSession(next);
  }, []);
  const login = useCallback(async (email: string) => {
    acceptAuth(await api.devLogin(email));
  }, [acceptAuth]);
  const passwordLogin = useCallback(async (email: string, password: string) => {
    acceptAuth(await api.passwordLogin(email, password));
  }, [acceptAuth]);
  const register = useCallback(async (email: string, password: string, displayName: string, companyName: string) => {
    const result = await api.register(email, password, displayName, companyName);
    acceptAuth(result);
    return result.recovery_codes;
  }, [acceptAuth]);
  const logout = useCallback(async () => {
    await fetch('/auth/logout', { method: 'POST', credentials: 'include' });
    sessionStorage.removeItem(storageKey);
    setSession(null);
  }, []);
  const switchTenant = useCallback(async (tenantId: string) => {
    await api.switchTenant(tenantId);
    setSession(current => {
      const next = current ? { ...current, tenantId } : current;
      if (next) sessionStorage.setItem(storageKey, JSON.stringify(next));
      return next;
    });
  }, []);
  const emergencyLogin = useCallback(async (tenantId: string, credential: string, purpose: string) => {
    const result = await api.useEmergencyAccess(tenantId, credential, purpose);
    acceptAuth({
      email: result.email, display_name: result.displayName, tenant_id: result.tenantId,
      role: result.role, auth_method: 'Emergency', session_kind: 'Emergency'
    });
    return result.rotatedCredential;
  }, [acceptAuth]);
  const value = useMemo(() => ({ session, login, passwordLogin, register, logout, switchTenant, emergencyLogin }),
    [session, login, passwordLogin, register, logout, switchTenant, emergencyLogin]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
