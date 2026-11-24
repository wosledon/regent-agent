import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState, PageHeader, Select, Stack, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { api } from '../lib/api-client/api';
import { useAuth } from '../app/useAuth';
import { AuthenticationPolicyPanel } from '../features/identity/AuthenticationPolicyPanel';
import i18n from '../lib/i18n';

export function SettingsPage() {
  const { t, i18n: translator } = useTranslation();
  const { session, switchTenant, logout } = useAuth();
  const queryClient = useQueryClient();
  const tenantId = session?.tenantId ?? '';
  const restricted = session?.sessionKind === 'Emergency';
  const canManage = session?.role === 'Owner' || session?.role === 'CoOwner';
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants, enabled: !restricted });
  const sessions = useQuery({ queryKey: ['sessions'], queryFn: api.sessions, enabled: Boolean(session) });
  const invitations = useQuery({ queryKey: ['invitations', tenantId], queryFn: () => api.invitations(tenantId), enabled: Boolean(tenantId) && !restricted });
  const delegations = useQuery({ queryKey: ['ownership-delegations', tenantId], queryFn: () => api.ownershipDelegations(tenantId), enabled: Boolean(tenantId) && !restricted });
  const suffix = useQuery({ queryKey: ['display-suffix'], queryFn: api.displaySuffix, enabled: Boolean(session) && !restricted });
  const localization = useQuery({ queryKey: ['localization'], queryFn: api.localization, enabled: Boolean(session) && !restricted });
  const [localizationDraft, setLocalizationDraft] = useState<Awaited<ReturnType<typeof api.localization>> | null>(null);
  const [invitationEmail, setInvitationEmail] = useState('');
  const [invitationRole, setInvitationRole] = useState('Member');
  const [delegateUserId, setDelegateUserId] = useState('');
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
  const saveSuffix = useMutation({
    mutationFn: () => api.updateDisplaySuffix(suffixDraft), onSuccess: invalidateIdentity
  });
  const saveLocalization = useMutation({
    mutationFn: () => api.updateLocalization(localizationDraft ?? localization.data!),
    onSuccess: async result => {
      setLocalizationDraft(null);
      await i18n.changeLanguage(result.locale);
      await queryClient.invalidateQueries({ queryKey: ['localization'] });
    }
  });

  if (!restricted && (tenants.isLoading || sessions.isLoading || invitations.isLoading || delegations.isLoading || suffix.isLoading || localization.isLoading)) return <LoadingState />;
  const identityError = restricted ? sessions.error : tenants.error ?? sessions.error ?? invitations.error ?? delegations.error ?? suffix.error ?? localization.error;
  if (identityError) return <ErrorState message={identityError.message} />;
  if (restricted) return (
    <Stack spacing={3}>
      <PageHeader title={t('settings')} description="Emergency recovery session — SSO configuration and account access only." />
      <Card title="Restricted emergency session">
        <Text>This session expires automatically after 15 minutes. Ordinary tenant, project and Agent operations are blocked.</Text>
        <Button tone="danger" onClick={() => logout()}>End emergency session</Button>
      </Card>
      <AuthenticationPolicyPanel tenantId={tenantId} canManage={canManage} />
    </Stack>
  );

  return (
    <Stack spacing={3}>
      <PageHeader title={t('settings')} description="Language, tenant context, identity providers, sessions and ownership." />
      <AuthenticationPolicyPanel tenantId={tenantId} canManage={canManage} />
      <Grid columns={2}>
        <Card title="Preferences">
          <Select label="Language" value={localizationDraft?.locale ?? localization.data?.locale ?? translator.language}
            onChange={locale => setLocalizationDraft({ ...(localizationDraft ?? localization.data!), locale })} options={[
              { value: 'zh-CN', label: '简体中文' }, { value: 'en-US', label: 'English' }
            ]} />
          <FormField label="IANA time zone"><TextInput value={localizationDraft?.timeZone ?? localization.data?.timeZone ?? 'Asia/Shanghai'}
            onChange={timeZone => setLocalizationDraft({ ...(localizationDraft ?? localization.data!), timeZone })} /></FormField>
          <FormField label="ISO currency"><TextInput value={localizationDraft?.currency ?? localization.data?.currency ?? 'CNY'}
            onChange={currency => setLocalizationDraft({ ...(localizationDraft ?? localization.data!), currency: currency.toUpperCase() })} /></FormField>
          <Button onClick={() => saveLocalization.mutate()} disabled={!localization.data} loading={saveLocalization.isPending}>Save preferences</Button>
          <FormField label="Boss display suffix"><TextInput value={suffixDraft || suffix.data?.suffix || ''} onChange={setSuffixDraft} placeholder="[Boss]" /></FormField>
          <Button onClick={() => saveSuffix.mutate()} loading={saveSuffix.isPending}>Save display suffix</Button>
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

      </Grid>
    </Stack>
  );
}
