export type AuthMode = 'dev' | 'password' | 'register' | 'emergency';

export function defaultAuthMode(production: boolean): AuthMode {
  return production ? 'password' : 'dev';
}

export function authenticationModes(production: boolean): AuthMode[] {
  return [
    ...(production ? [] : ['dev' as const]),
    'password',
    'register',
    'emergency'
  ];
}
