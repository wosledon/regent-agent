import { createContext } from 'react';

export interface SessionSummary {
  email: string;
  displayName: string;
  tenantId?: string;
  role?: string;
  authMethod?: string;
  sessionKind?: string;
}

export interface AuthContextValue {
  session: SessionSummary | null;
  login: (email: string) => Promise<void>;
  passwordLogin: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string, companyName: string) => Promise<string[]>;
  logout: () => Promise<void>;
  switchTenant: (tenantId: string) => Promise<void>;
  emergencyLogin: (tenantId: string, credential: string, purpose: string) => Promise<string>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
