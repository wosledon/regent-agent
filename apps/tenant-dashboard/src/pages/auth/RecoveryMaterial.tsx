import { useState } from 'react';
import { Button, Card, Icon, Stack, Text } from '@regent/ui-shared';

interface RegistrationRecoveryProps {
  codes: ReadonlyArray<string>;
  title: string;
  description: string;
  continueLabel: string;
  onContinue: () => void;
}

export function RegistrationRecovery({ codes, title, description, continueLabel, onContinue }: RegistrationRecoveryProps) {
  const [acknowledged, setAcknowledged] = useState(false);
  return (
    <Stack spacing={2.5}>
      <Stack direction="row" spacing={1.5} alignItems="center">
        <Icon name="security" sx={{ color: 'primary.main' }} />
        <Text variant="h3">{title}</Text>
      </Stack>
      <Text color="text.secondary">{description}</Text>
      <Card tone="warning" padding="small">
        <Stack spacing={1}>
          {codes.map(code => <Text key={code} variant="mono" sx={{ overflowWrap: 'anywhere' }}>{code}</Text>)}
        </Stack>
      </Card>
      <label>
        <input type="checkbox" checked={acknowledged} onChange={event => setAcknowledged(event.target.checked)} />
        I saved these recovery codes in a secure location.
      </label>
      <Button type="button" tone="secondary" disabled={!acknowledged} onClick={onContinue}>{continueLabel}</Button>
    </Stack>
  );
}

interface EmergencyRecoveryProps {
  credential: string;
  title: string;
  description: string;
  continueLabel: string;
  onContinue: () => void;
}

export function EmergencyRecovery({ credential, title, description, continueLabel, onContinue }: EmergencyRecoveryProps) {
  const [acknowledged, setAcknowledged] = useState(false);
  return (
    <Stack spacing={2.5}>
      <Stack direction="row" spacing={1.5} alignItems="center">
        <Icon name="warning" sx={{ color: 'warning.main' }} />
        <Text variant="h3">{title}</Text>
      </Stack>
      <Text color="text.secondary">{description}</Text>
      <Card tone="warning" padding="small">
        <Text component="code" sx={{ display: 'block', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{credential}</Text>
      </Card>
      <label>
        <input type="checkbox" checked={acknowledged} onChange={event => setAcknowledged(event.target.checked)} />
        I stored the rotated emergency credential securely.
      </label>
      <Button type="button" disabled={!acknowledged} onClick={onContinue}>{continueLabel}</Button>
    </Stack>
  );
}
