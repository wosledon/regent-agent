import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Card,
  ConfirmDialog,
  ContentContainer,
  DetailList,
  FormField,
  Grid,
  PageHeader,
  SectionHeader,
  Select,
  Stack,
  StatusChip,
  Text,
  TextInput,
  resolveLocale,
  supportedLocales
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../features/common/LocalizedDataTable';
import { LocalizedErrorState } from '../features/common/LocalizedErrorState';
import { LocalizedLoadingState } from '../features/common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../features/common/LocalizedMutationFeedback';
import { api } from '../lib/api-client/api';
import { DEFAULT_TENANT_TIME_ZONE } from '../lib/formatting';
import { localizedCurrencyError, localizedLocaleError, localizedTimeZoneError } from '../lib/localizedValidation';
import { useDomainLabels } from '../lib/domainLabels';
import { useTenantFormatting } from '../hooks/useTenantFormatting';
import { useTenantLocalization } from '../hooks/useTenantLocalization';
import { useAuth } from '../app/useAuth';
import { AuthenticationPolicyPanel } from '../features/identity/AuthenticationPolicyPanel';
import { DataExportPanel } from '../features/billing/DataExportPanel';
import i18n from '../lib/i18n';

type LocalizationSettings = Awaited<ReturnType<typeof api.localization>>;

function normalizeLocalizationSettings(settings: LocalizationSettings): LocalizationSettings {
  const locale = settings.locale.trim();
  const canonicalLocale = supportedLocales.find(candidate => candidate.toLowerCase() === locale.toLowerCase()) ?? locale;
  return {
    ...settings,
    locale: canonicalLocale,
    timeZone: settings.timeZone.trim(),
    currency: settings.currency.trim().toUpperCase()
  };
}

export function SettingsPage() {
  const { t, i18n: translator } = useTranslation();
  const domainLabel = useDomainLabels();
  const { session, switchTenant, logout } = useAuth();
  const queryClient = useQueryClient();
  const tenantId = session?.tenantId ?? '';
  const restricted = session?.sessionKind === 'Emergency';
  const format = useTenantFormatting(Boolean(session) && !restricted);
  const canManage = session?.role === 'Owner' || session?.role === 'CoOwner';
  const ownerQueriesEnabled = canManage && Boolean(tenantId) && !restricted;
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants, enabled: !restricted });
  const sessions = useQuery({ queryKey: ['sessions'], queryFn: api.sessions, enabled: Boolean(session) });
  const invitations = useQuery({
    queryKey: ['invitations', tenantId], queryFn: () => api.invitations(tenantId), enabled: ownerQueriesEnabled
  });
  const delegations = useQuery({
    queryKey: ['ownership-delegations', tenantId], queryFn: () => api.ownershipDelegations(tenantId), enabled: ownerQueriesEnabled
  });
  const suffix = useQuery({ queryKey: ['display-suffix'], queryFn: api.displaySuffix, enabled: Boolean(session) && !restricted });
  const localization = useTenantLocalization(Boolean(session) && !restricted);
  const [localizationDraft, setLocalizationDraft] = useState<LocalizationSettings | null>(null);
  const [invitationEmail, setInvitationEmail] = useState('');
  const [invitationRole, setInvitationRole] = useState('Member');
  const [delegateUserId, setDelegateUserId] = useState('');
  const [suffixDraft, setSuffixDraft] = useState<string | null>(null);
  const [sessionToRevoke, setSessionToRevoke] = useState<string | null>(null);
  const suffixSaving = useRef(false);
  const serverSuffix = suffix.data?.suffix ?? '';
  const suffixValue = suffixDraft ?? serverSuffix;
  const suffixDirty = suffixDraft !== null && suffixDraft !== serverSuffix;
  const changeSuffixDraft = (value: string) => setSuffixDraft(value === serverSuffix ? null : value);
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
    mutationFn: () => api.createInvitation(tenantId, invitationEmail.trim(), invitationRole),
    onSuccess: async () => { setInvitationEmail(''); await invalidateIdentity(); }
  });
  const delegate = useMutation({
    mutationFn: () => api.createOwnershipDelegation(tenantId, delegateUserId.trim()),
    onSuccess: async () => { setDelegateUserId(''); await invalidateIdentity(); }
  });
  const saveSuffix = useMutation({
    mutationFn: (value: string) => api.updateDisplaySuffix(value),
    onSuccess: async result => {
      queryClient.setQueryData(['display-suffix'], result);
      setSuffixDraft(null);
      await invalidateIdentity();
    },
    onSettled: () => { suffixSaving.current = false; }
  });
  const submitSuffix = () => {
    if (suffixSaving.current || saveSuffix.isPending || !suffixDirty) return;
    suffixSaving.current = true;
    saveSuffix.mutate(suffixValue);
  };
  const saveLocalization = useMutation({
    mutationFn: () => {
      const current = localizationDraft ?? localization.data;
      if (!current) throw new Error(t('common.notAvailable'));
      return api.updateLocalization(normalizeLocalizationSettings(current));
    },
    onSuccess: async result => {
      queryClient.setQueryData(['localization'], result);
      setLocalizationDraft(null);
      await i18n.changeLanguage(resolveLocale(result.locale));
      await queryClient.invalidateQueries({ queryKey: ['localization'] });
    }
  });
  const localizationValues = localizationDraft ?? localization.data;
  const normalizedLocalization = localizationValues ? normalizeLocalizationSettings(localizationValues) : null;
  const localeError = normalizedLocalization ? localizedLocaleError(normalizedLocalization.locale, t) : null;
  const timeZoneError = normalizedLocalization ? localizedTimeZoneError(normalizedLocalization.timeZone ?? '', t) : null;
  const currencyError = normalizedLocalization ? localizedCurrencyError(normalizedLocalization.currency ?? '', t) : null;
  const preferencesInvalid = Boolean(localeError || timeZoneError || currencyError);
  const pendingRevocation = sessions.data?.find(row => row.id === sessionToRevoke);

  if (restricted) return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader title={t('settings')} description={t('settingsPage.emergencyRecoverySessionSSOConfigurationAndAccountAccessOnly')} />
        <Card title={t('settingsPage.restrictedEmergencySession')} tone="warning" action={<StatusChip label={t('identityPage.required')} tone="warning" />}>
          <Text>{t('settingsPage.thisSessionExpiresAutomaticallyAfter15MinutesOrdinaryTenantProjectAndAge')}</Text>
          <Button tone="danger" onClick={() => logout()}>{t('settingsPage.endEmergencySession')}</Button>
        </Card>
        <AuthenticationPolicyPanel tenantId={tenantId} canManage={canManage} localizationEnabled={false} />
      </Stack>
    </ContentContainer>
  );

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('settings')}
          description={t('settingsPage.languageTenantContextIdentityProvidersSessionsAndOwnership')}
          meta={<>
            <StatusChip label={tenantId || t('settingsPage.legacySession')} tone="info" />
            <StatusChip label={domainLabel(session?.role ?? t('common.unknown'))} tone="neutral" />
          </>}
        />

        <Grid columns={2}>
          <Stack spacing={1.5}>
            <SectionHeader title={t('settingsPage.preferences')} />
            <Card padding="small">
              {localization.isLoading ? <LocalizedLoadingState /> : localization.error ? <LocalizedErrorState error={localization.error} onRetry={() => void localization.refetch()} /> : localization.data ? <Stack spacing={2}>
                <Select label={t('common.language')} value={localizationValues?.locale ?? translator.language}
                  onChange={locale => setLocalizationDraft({ ...localizationValues!, locale })} options={[
                    { value: 'zh-CN', label: t('settingsPage.chinese') }, { value: 'en-US', label: t('settingsPage.english') }
                  ]} />
                {localeError ? <Text color="error">{localeError}</Text> : null}
                <Grid columns={2}>
                  <FormField label={t('settingsPage.iANATimeZone')} error={timeZoneError ?? undefined}><TextInput value={localizationValues?.timeZone ?? DEFAULT_TENANT_TIME_ZONE}
                    onChange={timeZone => setLocalizationDraft({ ...localizationValues!, timeZone })} /></FormField>
                  <FormField label={t('settingsPage.iSOCurrency')} error={currencyError ?? undefined}><TextInput value={localizationValues?.currency ?? 'CNY'}
                    onChange={currency => setLocalizationDraft({ ...localizationValues!, currency: currency.toUpperCase() })} /></FormField>
                </Grid>
                <Stack direction="row" spacing={1.5} alignItems="center" wrap>
                  <Button onClick={() => saveLocalization.mutate()} disabled={preferencesInvalid || saveLocalization.isPending} loading={saveLocalization.isPending}>{t('settingsPage.savePreferences')}</Button>
                  <LocalizedMutationFeedback mutation={saveLocalization} successMessage={t('settingsPage.preferencesSaved')} />
                </Stack>
              </Stack> : <Text>{t('common.notAvailable')}</Text>}
              <Stack spacing={2}>
                <FormField label={t('settingsPage.bossDisplaySuffix')}><TextInput value={suffixValue} disabled={saveSuffix.isPending} onChange={changeSuffixDraft} placeholder={t('settingsPage.boss')} /></FormField>
                <Stack direction="row" wrap spacing={1}>
                  <Button onClick={submitSuffix} disabled={!suffixDirty || saveSuffix.isPending} loading={saveSuffix.isPending}>{t('settingsPage.saveDisplaySuffix')}</Button>
                  <Button tone="neutral" onClick={() => changeSuffixDraft('')} disabled={!suffixValue || saveSuffix.isPending}>{t('common.clear')}</Button>
                </Stack>
                <LocalizedMutationFeedback mutation={saveSuffix} successMessage={t('settingsPage.displaySuffixSaved')} />
                <Text variant="caption">{t('settingsPage.notificationsDashboardOnly')}</Text>
              </Stack>
            </Card>
          </Stack>

          <Stack spacing={1.5}>
            <SectionHeader title={t('settingsPage.tenantOwnership')} />
            <Card padding="small">
              <DetailList dense items={[{ label: t('common.tenant'), value: tenantId || t('settingsPage.legacySession'), mono: true }]} />
              {tenants.isLoading ? <LocalizedLoadingState /> : tenants.error ? <LocalizedErrorState error={tenants.error} onRetry={() => void tenants.refetch()} /> : <LocalizedDataTable rows={tenants.data ?? []} getRowId={row => row.id} density="compact" minWidth={560} columns={[
                { id: 'name', header: t('common.tenant'), render: row => <Text variant="bodyStrong">{row.name}</Text> },
                { id: 'company', header: t('common.company'), render: row => row.companyName },
                { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Active' ? 'success' : 'warning'} /> },
                { id: 'actions', header: t('common.actions'), render: row => <Button size="small" tone="neutral" onClick={() => changeTenant.mutate(row.id)} disabled={row.id === tenantId || changeTenant.isPending} loading={changeTenant.isPending}>{t('settingsPage.switch')}</Button> }
              ]} />}
              <LocalizedMutationFeedback mutation={changeTenant} successMessage={t('settingsPage.tenantSwitched')} />
            </Card>
          </Stack>
        </Grid>

        <AuthenticationPolicyPanel tenantId={tenantId} canManage={canManage} />
        <DataExportPanel canManage={canManage} />

        <Grid columns={2}>
          <Stack spacing={1.5}>
            <SectionHeader title={t('settingsPage.sessions')} />
            <Card padding="small">
              {sessions.isLoading ? <LocalizedLoadingState /> : sessions.error ? <LocalizedErrorState error={sessions.error} onRetry={() => void sessions.refetch()} /> : <LocalizedDataTable rows={sessions.data ?? []} getRowId={row => row.id} density="compact" minWidth={620} columns={[
                { id: 'tenant', header: t('common.tenant'), render: row => <Text variant="mono">{row.tenantId}</Text> },
                { id: 'expires', header: t('common.expires'), render: row => format.dateTime(row.expiresAt) },
                { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Active' ? 'success' : 'neutral'} /> },
                { id: 'actions', header: t('common.actions'), render: row => <Button size="small" tone="danger" onClick={() => setSessionToRevoke(row.id)} disabled={row.status !== 'Active' || revokeSession.isPending} loading={revokeSession.isPending}>{t('settingsPage.revoke')}</Button> }
              ]} />}
              <LocalizedMutationFeedback mutation={revokeSession} successMessage={t('settingsPage.sessionRevoked')} />
            </Card>
          </Stack>

          <Stack spacing={1.5}>
            <SectionHeader title={t('settingsPage.ownerInvitations')} />
            <Card padding="small">
              {!canManage ? <LocalizedErrorState error={t('settingsPage.permissionDeniedOwnerAccessIsRequired')} /> : invitations.isLoading ? <LocalizedLoadingState /> : invitations.error ? <LocalizedErrorState error={invitations.error} onRetry={() => void invitations.refetch()} /> : <Stack spacing={2}>
                <Grid columns={2}>
                  <FormField label={t('common.email')}><TextInput value={invitationEmail} onChange={setInvitationEmail} /></FormField>
                  <Select label={t('common.role')} value={invitationRole} onChange={setInvitationRole} options={[{ value: 'Member', label: domainLabel('Member') }, { value: 'CoOwner', label: domainLabel('CoOwner') }]} />
                </Grid>
                <Button onClick={() => invite.mutate()} disabled={!tenantId || !invitationEmail.trim() || invite.isPending} loading={invite.isPending}>{t('settingsPage.createInvitation')}</Button>
                <LocalizedDataTable rows={invitations.data ?? []} getRowId={row => row.id} density="compact" minWidth={520} columns={[
                  { id: 'email', header: t('common.email'), render: row => row.email },
                  { id: 'role', header: t('common.role'), render: row => domainLabel(row.role) },
                  { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Pending' ? 'warning' : 'neutral'} /> }
                ]} />
                <LocalizedMutationFeedback mutation={invite} successMessage={t('settingsPage.invitationCreated')} />
              </Stack>}
            </Card>
          </Stack>
        </Grid>

        <Stack spacing={1.5}>
          <SectionHeader title={t('settingsPage.ownershipDelegation')} />
          <Card padding="small">
            {!canManage ? <LocalizedErrorState error={t('settingsPage.permissionDeniedOwnerAccessIsRequired')} /> : delegations.isLoading ? <LocalizedLoadingState /> : delegations.error ? <LocalizedErrorState error={delegations.error} onRetry={() => void delegations.refetch()} /> : <Stack spacing={2}>
              <Grid columns={2}>
                <FormField label={t('settingsPage.delegateUserID')}><TextInput value={delegateUserId} onChange={setDelegateUserId} /></FormField>
                <Stack sx={{ justifyContent: 'flex-end' }}><Button onClick={() => delegate.mutate()} disabled={!tenantId || !delegateUserId.trim() || delegate.isPending} loading={delegate.isPending}>{t('settingsPage.delegateFor24Hours')}</Button></Stack>
              </Grid>
              <LocalizedDataTable rows={delegations.data ?? []} getRowId={row => row.id} density="compact" minWidth={620} columns={[
                { id: 'delegate', header: t('settingsPage.delegate'), render: row => <Text variant="mono">{row.delegateUserId}</Text> },
                { id: 'expires', header: t('common.expires'), render: row => format.dateTime(row.expiresAt) },
                { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Active' ? 'success' : 'neutral'} /> }
              ]} />
              <LocalizedMutationFeedback mutation={delegate} successMessage={t('settingsPage.ownershipDelegatedFor24Hours')} />
            </Stack>}
          </Card>
        </Stack>

        <ConfirmDialog
          open={sessionToRevoke !== null}
          title={t('settingsPage.revoke')}
          description={pendingRevocation ? <Text component="span" sx={{ whiteSpace: 'pre-line' }}>{`${t('common.tenant')}: ${pendingRevocation.tenantId}\n${t('common.expires')}: ${format.dateTime(pendingRevocation.expiresAt)}`}</Text> : undefined}
          confirmLabel={t('settingsPage.revoke')}
          cancelLabel={t('common.no')}
          tone="danger"
          loading={revokeSession.isPending}
          onConfirm={() => { if (sessionToRevoke) revokeSession.mutate(sessionToRevoke); setSessionToRevoke(null); }}
          onClose={() => { if (!revokeSession.isPending) setSessionToRevoke(null); }}
        />
      </Stack>
    </ContentContainer>
  );
}
