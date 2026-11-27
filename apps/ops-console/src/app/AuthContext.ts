import { createContext } from 'react';
import type { OperatorSession } from '../lib/api';

export type AuthStatus = 'checking' | 'authenticated' | 'anonymous' | 'permission-denied' | 'error';

export interface AuthValue {
  status: AuthStatus;
  user: OperatorSession | null;
  error: string | null;
  retry: () => Promise<void>;
  login: () => Promise<void>;
  logout: () => Promise<void>;
}

export const AuthContext = createContext<AuthValue | null>(null);
