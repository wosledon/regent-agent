import { useCallback, useMemo, useState, type PropsWithChildren } from 'react';
import { api } from '../lib/api-client/api';
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

export function AuthProvider({ children }: PropsWithChildren) {
  const [session, setSession] = useState<SessionSummary | null>(readSession);
  const login = useCallback(async (email: string) => {
    const result = await api.devLogin(email);
    const next = { email: result.email, displayName: result.display_name, tenantId: result.tenant_id };
    sessionStorage.setItem(storageKey, JSON.stringify(next));
    setSession(next);
  }, []);
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
  const value = useMemo(() => ({ session, login, logout, switchTenant }), [session, login, logout, switchTenant]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
