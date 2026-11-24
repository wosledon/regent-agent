import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, ErrorState, FormField, LoadingState, Select, Stack, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import type { components } from '../../lib/api-client/schema';

type Policy = components['schemas']['AuthenticationPolicyDto'];

export function AuthenticationPolicyPanel({ tenantId, canManage }: { tenantId: string; canManage: boolean }) {
  const queryClient = useQueryClient();
  const policy = useQuery({
    queryKey: ['authentication-policy', tenantId],
    queryFn: () => api.authenticationPolicy(tenantId),
    enabled: canManage && Boolean(tenantId),
    retry: false
  });
  const [provider, setProvider] = useState('Oidc');
  const [issuer, setIssuer] = useState('');
  const [clientId, setClientId] = useState('');
  const [secretRef, setSecretRef] = useState('');
  const [redirectUri, setRedirectUri] = useState('');
  const [mapping, setMapping] = useState('{"engineering":"dashboard.read"}');
  const [emergencyCredential, setEmergencyCredential] = useState('');
  const [refreshResult, setRefreshResult] = useState('');
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['authentication-policy', tenantId] });
  const configure = useMutation({
    mutationFn: () => api.configureIdentityProvider(tenantId, provider, issuer, clientId, secretRef, redirectUri, mapping),
    onSuccess: invalidate
  });
  const activate = useMutation({
    mutationFn: () => api.activateIdentityProvider(tenantId, provider), onSuccess: invalidate
  });
  const saveMapping = useMutation({ mutationFn: () => api.updateGroupMapping(tenantId, mapping), onSuccess: invalidate });
  const refresh = useMutation({
    mutationFn: () => api.refreshGroupMappings(tenantId),
    onSuccess: result => { setRefreshResult(`${result.evaluated} evaluated · ${result.updated} updated · ${result.denied} denied`); return invalidate(); }
  });
  const emergency = useMutation({
    mutationFn: () => api.configureEmergencyAccess(tenantId),
    onSuccess: result => setEmergencyCredential(result.credential)
  });
  const actionError = configure.error ?? activate.error ?? saveMapping.error ?? refresh.error ?? emergency.error;

  if (!canManage) return null;
  return (
    <Stack spacing={3}>
      <Card title="Enterprise OIDC and authentication policy" action={<StatusChip label={policy.data?.mode ?? 'optional'} tone={policy.data?.mode === 'required' ? 'warning' : 'info'} />}>
        {policy.isLoading ? <LoadingState /> : null}
        {policy.error ? <ErrorState message={policy.error.message} /> : null}
        {policy.data ? <PolicyForm key={policy.data.version} tenantId={tenantId} initial={policy.data} /> : null}
      </Card>
      <Card title="OIDC provider and group capabilities">
        <Select label="Provider" value={provider} onChange={setProvider} options={[{ value: 'Oidc', label: 'OIDC' }, { value: 'GitHub', label: 'GitHub' }]} />
        <FormField label="Issuer"><TextInput value={issuer} onChange={setIssuer} placeholder="https://idp.example.test/tenant" /></FormField>
        <FormField label="Client ID"><TextInput value={clientId} onChange={setClientId} /></FormField>
        <FormField label="Client secret reference"><TextInput value={secretRef} onChange={setSecretRef} placeholder="keyvault://oidc/client" /></FormField>
        <FormField label="Redirect URI"><TextInput value={redirectUri} onChange={setRedirectUri} placeholder="https://app.example.test/auth/oidc/callback" /></FormField>
        <FormField label="Group to capability mapping (JSON)">
          <TextInput value={mapping} onChange={setMapping} multiline rows={4} />
        </FormField>
        <Stack direction="row" wrap spacing={1}>
          <Button onClick={() => configure.mutate()} disabled={!issuer || !clientId} loading={configure.isPending}>Save draft</Button>
          <Button tone="secondary" onClick={() => activate.mutate()} disabled={!issuer || !clientId} loading={activate.isPending}>Activate provider</Button>
          <Button tone="secondary" onClick={() => saveMapping.mutate()} loading={saveMapping.isPending}>Save mapping</Button>
          <Button tone="secondary" onClick={() => refresh.mutate()} loading={refresh.isPending}>Refresh and revoke stale sessions</Button>
        </Stack>
        {refreshResult ? <Text variant="caption">{refreshResult}</Text> : null}
        <Text variant="caption">Group mappings grant explicit capabilities only. Owner, model-key, escalation and termination capabilities cannot be granted by a group.</Text>
      </Card>
      <Card title="Emergency access">
        <Text>Configuring a new credential supersedes the previous one. Use it only when the identity provider is unavailable. Each use creates a 15-minute restricted session and rotates the credential.</Text>
        <Button tone="danger" onClick={() => emergency.mutate()} loading={emergency.isPending}>Configure emergency credential</Button>
        {emergencyCredential ? <Card title="Store this credential now">
          <Text>It is shown once and expires in 90 days.</Text>
          <Text component="pre" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{emergencyCredential}</Text>
          <Button tone="secondary" onClick={() => setEmergencyCredential('')}>I stored it</Button>
        </Card> : null}
      </Card>
      {actionError ? <ErrorState message={actionError.message} /> : null}
    </Stack>
  );
}

function PolicyForm({ tenantId, initial }: { tenantId: string; initial: Policy }) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<Policy>(initial);
  const save = useMutation({
    mutationFn: () => api.updateAuthenticationPolicy(tenantId, draft),
    onSuccess: async result => {
      setDraft(result);
      await queryClient.invalidateQueries({ queryKey: ['authentication-policy', tenantId] });
    }
  });
  return (
    <Stack spacing={2}>
      <Select label="Authentication mode" value={draft.mode} onChange={mode => setDraft({ ...draft, mode })} options={[
        { value: 'optional', label: 'Optional' }, { value: 'required', label: 'Required OIDC SSO' }
      ]} />
      <FormField label="Allowed ACR values (comma separated)"><TextInput value={draft.allowedAcrValues.join(',')} onChange={value => setDraft({ ...draft, allowedAcrValues: split(value) })} /></FormField>
      <FormField label="Required AMR values (comma separated)"><TextInput value={draft.requiredAmrValues.join(',')} onChange={value => setDraft({ ...draft, requiredAmrValues: split(value) })} /></FormField>
      <FormField label="Maximum authentication age (minutes)"><TextInput type="number" value={String(draft.maxAuthenticationAgeMinutes)} onChange={value => setDraft({ ...draft, maxAuthenticationAgeMinutes: Number(value) })} /></FormField>
      <FormField label="Maximum session (minutes)"><TextInput type="number" value={String(draft.maxSessionMinutes)} onChange={value => setDraft({ ...draft, maxSessionMinutes: Number(value) })} /></FormField>
      <FormField label="Idle timeout (minutes)"><TextInput type="number" value={String(draft.idleTimeoutMinutes)} onChange={value => setDraft({ ...draft, idleTimeoutMinutes: Number(value) })} /></FormField>
      <Select label="Require group claim" value={draft.requireGroupClaim ? 'yes' : 'no'} onChange={value => setDraft({ ...draft, requireGroupClaim: value === 'yes' })} options={[
        { value: 'no', label: 'No' }, { value: 'yes', label: 'Yes — fail closed when missing' }
      ]} />
      <Button onClick={() => save.mutate()} loading={save.isPending}>Save authentication policy</Button>
      {save.error ? <ErrorState message={save.error.message} /> : null}
    </Stack>
  );
}

function split(value: string) {
  return value.split(',').map(item => item.trim()).filter(Boolean);
}
