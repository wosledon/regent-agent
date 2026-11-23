import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Card, Text, TextInput, Button } from '@regent/ui-shared';
import { useAuth } from '../app/useAuth';

export function LoginPage() {
  const { login, session } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('owner@example.test');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setLoading(true);
    setError(null);
    try {
      await login(email);
      navigate('/');
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Login failed.');
    } finally {
      setLoading(false);
    }
  }

  if (session) return <Text variant="h2">Already signed in as {session.displayName}</Text>;
  return (
    <Card title="Sign in to Regent Agent">
      <form onSubmit={submit}>
        <TextInput value={email} onChange={setEmail} placeholder="owner@example.test" />
        {error ? <Text color="error">{error}</Text> : null}
        <Button type="submit" loading={loading}>Sign in</Button>
      </form>
    </Card>
  );
}
