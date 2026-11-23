import { createContext } from 'react';

export interface SessionSummary {
  email: string;
  displayName: string;
}

export interface AuthContextValue {
  session: SessionSummary | null;
  login: (email: string) => Promise<void>;
  logout: () => Promise<void>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
