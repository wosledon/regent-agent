import { useMemo, useState, type PropsWithChildren } from 'react';
import { api } from '../lib/api';
import { AuthContext } from './AuthContext';

export function AuthProvider({ children }: PropsWithChildren) {
  const [user, setUser] = useState<string | null>(sessionStorage.getItem('regent.ops.user'));
  const value = useMemo(() => ({
    user,
    login: async () => {
      const result = await api.login();
      sessionStorage.setItem('regent.ops.user', result.display_name);
      setUser(result.display_name);
    },
    logout: async () => {
      await api.logout();
      sessionStorage.removeItem('regent.ops.user');
      setUser(null);
    }
  }), [user]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
