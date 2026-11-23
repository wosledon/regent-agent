import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, Card, FormField, Select, Stack, Text, TextInput } from '@regent/ui-shared';
import { useAuth } from '../app/useAuth';

type AuthMode = 'dev' | 'password' | 'register';

export function LoginPage() {
  const { login, passwordLogin, register, session } = useAuth();
  const navigate = useNavigate();
  const [mode, setMode] = useState<AuthMode>('dev');
  const [email, setEmail] = useState('owner@example.test');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [companyName, setCompanyName] = useState('');
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setLoading(true);
    setError(null);
    try {
      if (mode === 'dev') await login(email);
      else if (mode === 'password') await passwordLogin(email, password);
      else setRecoveryCodes(await register(email, password, displayName, companyName));
      if (mode !== 'register') navigate('/');
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Authentication failed.');
    } finally {
      setLoading(false);
    }
  }

  if (session && recoveryCodes.length === 0) return <Text variant="h2">Already signed in as {session.displayName}</Text>;
  return (
    <Card title="Sign in to Regent Agent">
      <form onSubmit={submit}>
        <Stack spacing={2}>
          <Select label="Authentication mode" value={mode} onChange={value => setMode(value as AuthMode)} options={[
            { value: 'dev', label: 'Local development' },
            { value: 'password', label: 'Password' },
            { value: 'register', label: 'Create account' }
          ]} />
          <FormField label="Email"><TextInput value={email} onChange={setEmail} placeholder="owner@example.test" /></FormField>
          {mode !== 'dev' ? <FormField label="Password"><TextInput value={password} onChange={setPassword} type="password" placeholder="At least 10 characters" /></FormField> : null}
          {mode === 'register' ? <>
            <FormField label="Display name"><TextInput value={displayName} onChange={setDisplayName} placeholder="Display name" /></FormField>
            <FormField label="Company name"><TextInput value={companyName} onChange={setCompanyName} placeholder="Company name" /></FormField>
          </> : null}
          {error ? <Text color="error">{error}</Text> : null}
          <Button type="submit" loading={loading} disabled={mode === 'register' && (!displayName || !companyName || password.length < 10)}>
            {mode === 'register' ? 'Create account' : 'Sign in'}
          </Button>
          {recoveryCodes.length > 0 ? <Card title="Save recovery codes">
            <Text>These codes are shown once. Store them in a secure password manager.</Text>
            {recoveryCodes.map(code => <Text key={code} variant="caption">{code}</Text>)}
            <Button type="button" tone="secondary" onClick={() => navigate('/')}>Continue</Button>
          </Card> : null}
        </Stack>
      </form>
    </Card>
  );
}
