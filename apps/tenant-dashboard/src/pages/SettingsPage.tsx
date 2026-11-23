import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState, PageHeader, Select, Stack, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { api } from '../lib/api-client/api';
import { useAuth } from '../app/useAuth';
import i18n from '../lib/i18n';

export function SettingsPage() {
  const { t, i18n: translator } = useTranslation();
  const { session, switchTenant } = useAuth();
  const queryClient = useQueryClient();
  const tenantId = session?.tenantId ?? '';
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants });
  const sessions = useQuery({ queryKey: ['sessions'], queryFn: api.sessions, enabled: Boolean(session) });
  const invitations = useQuery({ queryKey: ['invitations', tenantId], queryFn: () => api.invitations(tenantId), enabled: Boolean(tenantId) });
  const delegations = useQuery({ queryKey: ['ownership-delegations', tenantId], queryFn: () => api.ownershipDelegations(tenantId), enabled: Boolean(tenantId) });
  const suffix = useQuery({ queryKey: ['display-suffix'], queryFn: api.displaySuffix, enabled: Boolean(session) });
  const [invitationEmail, setInvitationEmail] = useState('');
  const [invitationRole, setInvitationRole] = useState('Member');
  const [delegateUserId, setDelegateUserId] = useState('');
  const [provider, setProvider] = useState('Oidc');
  const [issuer, setIssuer] = useState('');
  const [clientId, setClientId] = useState('');
  const [suffixDraft, setSuffixDraft] = useState('');
  const invalidateIdentity = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['sessions'] }),
    queryClient.invalidateQueries({ queryKey: ['invitations'] }),
    queryClient.invalidateQueries({ queryKey: ['ownership-delegations'] }),
    queryClient.invalidateQueries({ queryKey: ['display-suffix'] })
  ]);
  const changeTenant = useMutation({
    mutationFn: switchTenant,
    onSuccess: async () => { await queryClient.invalidateQueries(); window.location.assign('/'); }
  });
  const revokeSession = useMutation({ mutationFn: api.revokeSession, onSuccess: invalidateIdentity });
  const invite = useMutation({
    mutationFn: () => api.createInvitation(tenantId, invitationEmail, invitationRole),
    onSuccess: async () => { setInvitationEmail(''); await invalidateIdentity(); }
  });
  const delegate = useMutation({
    mutationFn: () => api.createOwnershipDelegation(tenantId, delegateUserId),
    onSuccess: async () => { setDelegateUserId(''); await invalidateIdentity(); }
  });
  const configureProvider = useMutation({
    mutationFn: () => api.configureIdentityProvider(tenantId, provider, issuer, clientId), onSuccess: invalidateIdentity
  });
  const saveSuffix = useMutation({
    mutationFn: () => api.updateDisplaySuffix(suffixDraft), onSuccess: invalidateIdentity
  });

  if (tenants.isLoading || sessions.isLoading || invitations.isLoading || delegations.isLoading || suffix.isLoading) return <LoadingState />;
  const identityError = tenants.error ?? sessions.error ?? invitations.error ?? delegations.error ?? suffix.error;
  if (identityError) return <ErrorState message={identityError.message} />;

  return (
    <Stack spacing={3}>
      <PageHeader title={t('settings')} description="Language, tenant context, identity providers, sessions and ownership." />
      <Grid columns={2}>
        <Card title="Preferences">
          <Select label="Language" value={translator.language} onChange={language => void i18n.changeLanguage(language)} options={[
            { value: 'zh-CN', label: '简体中文' }, { value: 'en-US', label: 'English' }
          ]} />
          <FormField label="Boss display suffix"><TextInput value={suffixDraft || suffix.data?.suffix || ''} onChange={setSuffixDraft} placeholder="[Boss]" /></FormField>
          <Button onClick={() => saveSuffix.mutate()} loading={saveSuffix.isPending}>Save display suffix</Button>
          <Text>Timezone: Asia/Shanghai</Text>
          <Text>Notifications: dashboard only</Text>
        </Card>
        <Card title="Tenant ownership">
          <Text variant="caption">Current tenant: {tenantId || 'legacy session — sign in again'}</Text>
          <DataTable rows={tenants.data ?? []} getRowId={row => row.id} columns={[
            { id: 'name', header: 'Tenant', render: row => row.name },
            { id: 'company', header: 'Company', render: row => row.companyName },
            { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone={row.status === 'Active' ? 'success' : 'warning'} /> },
            { id: 'actions', header: 'Actions', render: row => <Button onClick={() => changeTenant.mutate(row.id)} disabled={row.id === tenantId} loading={changeTenant.isPending}>Switch</Button> }
          ]} />
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card title="Sessions">
          <DataTable rows={sessions.data ?? []} getRowId={row => row.id} columns={[
            { id: 'tenant', header: 'Tenant', render: row => row.tenantId },
            { id: 'expires', header: 'Expires', render: row => new Date(row.expiresAt).toLocaleString() },
            { id: 'status', header: 'Status', render: row => row.status },
            { id: 'actions', header: 'Actions', render: row => <Button tone="danger" onClick={() => revokeSession.mutate(row.id)} disabled={row.status !== 'Active'}>Revoke</Button> }
          ]} />
        </Card>
        <Card title="Owner invitations">
          <FormField label="Email"><TextInput value={invitationEmail} onChange={setInvitationEmail} /></FormField>
          <Select label="Role" value={invitationRole} onChange={setInvitationRole} options={[{ value: 'Member', label: 'Member' }, { value: 'CoOwner', label: 'Co-owner' }]} />
          <Button onClick={() => invite.mutate()} disabled={!tenantId || !invitationEmail} loading={invite.isPending}>Create invitation</Button>
          <DataTable rows={invitations.data ?? []} getRowId={row => row.id} columns={[
            { id: 'email', header: 'Email', render: row => row.email },
            { id: 'role', header: 'Role', render: row => row.role },
            { id: 'status', header: 'Status', render: row => row.status }
          ]} />
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card title="Ownership delegation">
          <FormField label="Delegate user ID"><TextInput value={delegateUserId} onChange={setDelegateUserId} /></FormField>
          <Button onClick={() => delegate.mutate()} disabled={!tenantId || !delegateUserId} loading={delegate.isPending}>Delegate for 24 hours</Button>
          <DataTable rows={delegations.data ?? []} getRowId={row => row.id} columns={[
            { id: 'delegate', header: 'Delegate', render: row => row.delegateUserId },
            { id: 'expires', header: 'Expires', render: row => new Date(row.expiresAt).toLocaleString() },
            { id: 'status', header: 'Status', render: row => row.status }
          ]} />
        </Card>
        <Card title="Enterprise identity">
          <Select label="Provider" value={provider} onChange={setProvider} options={[{ value: 'Oidc', label: 'OIDC' }, { value: 'GitHub', label: 'GitHub local adapter' }]} />
          <FormField label="Issuer"><TextInput value={issuer} onChange={setIssuer} placeholder="https://idp.local/tenant" /></FormField>
          <FormField label="Client ID"><TextInput value={clientId} onChange={setClientId} /></FormField>
          <Button onClick={() => configureProvider.mutate()} disabled={!tenantId || !issuer || !clientId} loading={configureProvider.isPending}>Enable local identity adapter</Button>
          <Text variant="caption">Remote OIDC/GitHub traffic is not connected in local mode.</Text>
        </Card>
      </Grid>
    </Stack>
  );
}
