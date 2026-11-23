import { createContext } from 'react';

export interface SessionSummary {
  email: string;
  displayName: string;
  tenantId?: string;
}

export interface AuthContextValue {
  session: SessionSummary | null;
  login: (email: string) => Promise<void>;
  logout: () => Promise<void>;
  switchTenant: (tenantId: string) => Promise<void>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
