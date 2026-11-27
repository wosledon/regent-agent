import { createContext } from 'react';

export type AuthStatus = 'checking' | 'authenticated' | 'anonymous' | 'permission-denied' | 'error';
export type CollaborationScope = 'none' | 'participant' | 'observer';

export interface EffectiveAccess {
  canReadChat: boolean;
  canReadCollaboration: boolean;
  collaborationScope: CollaborationScope;
}

export const deniedEffectiveAccess: EffectiveAccess = {
  canReadChat: false,
  canReadCollaboration: false,
  collaborationScope: 'none'
};

export function isRestrictedSessionKind(value: unknown): boolean {
  return typeof value !== 'string' || value.trim().toLowerCase() !== 'standard';
}

export interface SessionSummary {
  email: string;
  displayName: string;
  tenantId: string;
  role: string;
  authMethod: string;
  sessionKind: string;
  capabilities?: string[];
  effectiveAccess: EffectiveAccess;
}

export interface AuthContextValue {
  status: AuthStatus;
  session: SessionSummary | null;
  error: string | null;
  retry: () => Promise<void>;
  login: (email: string) => Promise<void>;
  passwordLogin: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string, companyName: string) => Promise<string[]>;
  logout: () => Promise<void>;
  switchTenant: (tenantId: string) => Promise<void>;
  emergencyLogin: (tenantId: string, credential: string, purpose: string) => Promise<string>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
