import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Card,
  ConfirmDialog,
  FormField,
  Grid,
  Icon,
  Select,
  Stack,
  StatusChip,
  Text,
  TextInput,
  type ChipTone
} from '@regent/ui-shared';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import type { components } from '../../lib/api-client/schema';
import { localizedJsonError } from '../../lib/localizedValidation';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';

type Policy = components['schemas']['AuthenticationPolicyDto'];
type IdentityProvider = components['schemas']['IdentityProviderDto'];

function providerTone(status: string): ChipTone {
  if (status === 'Active') return 'success';
  if (status === 'Draft') return 'warning';
  if (status === 'Disabled') return 'error';
  return 'neutral';
}

export function AuthenticationPolicyPanel({ tenantId, canManage, localizationEnabled = true }: { tenantId: string; canManage: boolean; localizationEnabled?: boolean }) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting(localizationEnabled);
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
  const [savedProvider, setSavedProvider] = useState<IdentityProvider | null>(null);
  const [emergencyCredential, setEmergencyCredential] = useState('');
  const [refreshResult, setRefreshResult] = useState<Awaited<ReturnType<typeof api.refreshGroupMappings>> | null>(null);
  const [activationConfirmOpen, setActivationConfirmOpen] = useState(false);
  const [emergencyConfirmOpen, setEmergencyConfirmOpen] = useState(false);
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['authentication-policy', tenantId] });
  const providerDirty = !savedProvider
    || provider !== savedProvider.provider
    || issuer !== savedProvider.issuer
    || clientId !== savedProvider.clientId
    || secretRef !== savedProvider.clientSecretRef
    || redirectUri !== savedProvider.redirectUri
    || mapping !== savedProvider.groupMappingJson;
  const mappingError = localizedJsonError(mapping, t('identityPage.groupMapping'), t);
  const configure = useMutation({
    mutationFn: () => api.configureIdentityProvider(tenantId, provider, issuer, clientId, secretRef, redirectUri, mapping),
    onSuccess: result => {
      setSavedProvider(result);
      setProvider(result.provider);
      setIssuer(result.issuer);
      setClientId(result.clientId);
      setSecretRef(result.clientSecretRef);
      setRedirectUri(result.redirectUri);
      setMapping(result.groupMappingJson);
      return invalidate();
    }
  });
  const activate = useMutation({
    mutationFn: () => api.activateIdentityProvider(tenantId, savedProvider!.provider),
    onSuccess: result => { setSavedProvider(result); return invalidate(); }
  });
  const saveMapping = useMutation({
    mutationFn: () => api.updateGroupMapping(tenantId, mapping),
    onSuccess: () => {
      setSavedProvider(current => current && current.provider === provider ? { ...current, groupMappingJson: mapping } : current);
      return invalidate();
    }
  });
  const refresh = useMutation({
    mutationFn: () => api.refreshGroupMappings(tenantId),
    onSuccess: result => { setRefreshResult(result); return invalidate(); }
  });
  const emergency = useMutation({
    mutationFn: () => api.configureEmergencyAccess(tenantId),
    onSuccess: result => setEmergencyCredential(result.credential)
  });
  const providerPending = configure.isPending || activate.isPending || saveMapping.isPending || refresh.isPending;

  if (!canManage) return null;

  return (
    <Stack spacing={3}>
      <Card
        title={t('identityPage.enterpriseOIDCAndAuthenticationPolicy')}
        headingLevel="h2"
        padding="small"
        action={<StatusChip label={policy.data?.mode === 'required' ? t('identityPage.required') : t('identityPage.optional')} tone={policy.data?.mode === 'required' ? 'warning' : 'info'} />}
      >
        {policy.isLoading ? <LocalizedLoadingState /> : null}
        {policy.error ? <LocalizedErrorState error={policy.error} onRetry={() => void policy.refetch()} /> : null}
        {policy.data ? <PolicyForm tenantId={tenantId} initial={policy.data} /> : !policy.isLoading && !policy.error ? <Text>{t('common.notAvailable')}</Text> : null}
      </Card>

      <Card
        title={t('identityPage.oIDCProviderAndGroupCapabilities')}
        description={t('identityPage.groupMappingsGrantExplicitCapabilitiesOnlyOwnerModelKeyEscalationAndTerm')}
        headingLevel="h2"
        padding="small"
        action={<StatusChip label={savedProvider ? domainLabel(savedProvider.status) : t('common.unknown')} tone={savedProvider ? providerTone(savedProvider.status) : 'neutral'} />}
      >
        <Grid columns={2}>
          <Select label={t('common.provider')} value={provider} disabled={providerPending} onChange={setProvider} options={[{ value: 'Oidc', label: domainLabel('Oidc') }, { value: 'GitHub', label: domainLabel('GitHub') }]} />
          <FormField label={t('identityPage.issuer')}><TextInput value={issuer} disabled={providerPending} onChange={setIssuer} placeholder={t('identityPage.httpsIdpExampleTestTenant')} /></FormField>
          <FormField label={t('identityPage.clientID')}><TextInput value={clientId} disabled={providerPending} onChange={setClientId} /></FormField>
          <FormField label={t('identityPage.clientSecretReference')}><TextInput value={secretRef} disabled={providerPending} onChange={setSecretRef} placeholder={t('identityPage.keyvaultOidcClient')} /></FormField>
          <FormField label={t('identityPage.redirectURI')}><TextInput value={redirectUri} disabled={providerPending} onChange={setRedirectUri} placeholder={t('identityPage.httpsAppExampleTestAuthOidcCallback')} /></FormField>
        </Grid>
        <FormField label={t('identityPage.groupToCapabilityMappingJSON')} error={mappingError ?? undefined}>
          <TextInput value={mapping} disabled={providerPending} onChange={setMapping} multiline minRows={4} />
        </FormField>
        <Stack direction="row" wrap spacing={1}>
          <Button onClick={() => configure.mutate()} disabled={!issuer.trim() || !clientId.trim() || !!mappingError || providerPending} loading={configure.isPending}>{t('identityPage.saveDraft')}</Button>
          <Button tone="secondary" onClick={() => setActivationConfirmOpen(true)} disabled={!savedProvider || providerDirty || !!mappingError || providerPending} loading={activate.isPending}>{t('identityPage.activateProvider')}</Button>
          <Button tone="secondary" onClick={() => saveMapping.mutate()} disabled={!!mappingError || providerPending} loading={saveMapping.isPending}>{t('identityPage.saveMapping')}</Button>
          <Button tone="secondary" onClick={() => refresh.mutate()} disabled={providerPending} loading={refresh.isPending}>{t('identityPage.refreshAndRevokeStaleSessions')}</Button>
        </Stack>
        <Text variant="caption">{savedProvider
          ? t('identityPage.savedProviderVersion', { provider: savedProvider.provider, version: format.number(toFiniteNumber(savedProvider.version)) })
          : t('identityPage.noSavedProviderDraft')}</Text>
        {providerDirty ? <Text color="warning">{t('identityPage.unsavedProviderChangesAreShownActivationIsDisabledUntilTheyAreSaved')}</Text> : null}
        <Stack spacing={1}>
          <LocalizedMutationFeedback mutation={configure} successMessage={t('identityPage.providerDraftSaved')} />
          <LocalizedMutationFeedback mutation={activate} successMessage={t('identityPage.savedProviderActivated')} />
          <LocalizedMutationFeedback mutation={saveMapping} successMessage={t('identityPage.groupMappingSaved')} />
          <LocalizedMutationFeedback mutation={refresh} successMessage={t('identityPage.groupMappingsRefreshed')} />
        </Stack>
        {refreshResult ? <Text variant="caption">{t('identityPage.refreshResult', {
          evaluated: format.number(toFiniteNumber(refreshResult.evaluated)),
          updated: format.number(toFiniteNumber(refreshResult.updated)),
          denied: format.number(toFiniteNumber(refreshResult.denied))
        })}</Text> : null}
      </Card>

      <Card title={t('identityPage.emergencyAccess')} headingLevel="h2" tone="warning" padding="small" action={<Icon name="security" sx={{ color: 'warning.main' }} />}>
        <Text>{t('identityPage.configuringANewCredentialSupersedesThePreviousOneUseItOnlyWhenTheIdentit')}</Text>
        <Stack direction="row" wrap spacing={1.5} alignItems="center">
          <Button tone="danger" onClick={() => setEmergencyConfirmOpen(true)} loading={emergency.isPending}>{t('identityPage.configureEmergencyCredential')}</Button>
          <LocalizedMutationFeedback mutation={emergency} successMessage={t('identityPage.emergencyCredentialConfiguredStoreItNow')} successTone="warning" />
        </Stack>
        {emergencyCredential ? <Card title={t('identityPage.storeThisCredentialNow')} tone="warning" padding="small">
          <Text>{t('identityPage.itIsShownOnceAndExpiresIn90Days')}</Text>
          <Text component="pre" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{emergencyCredential}</Text>
          <Button tone="secondary" onClick={() => setEmergencyCredential('')}>{t('identityPage.iStoredIt')}</Button>
        </Card> : null}
      </Card>

      <ConfirmDialog
        open={activationConfirmOpen}
        title={t('identityPage.activateProvider')}
        description={<Text component="span" sx={{ whiteSpace: 'pre-line' }}>{savedProvider ? t('identityPage.confirmActivate', {
          provider: savedProvider.provider,
          version: format.number(toFiniteNumber(savedProvider.version))
        }) : ''}</Text>}
        confirmLabel={t('identityPage.activateProvider')}
        cancelLabel={t('common.no')}
        tone="danger"
        loading={activate.isPending}
        requireText={savedProvider?.provider}
        onConfirm={() => { setActivationConfirmOpen(false); activate.mutate(); }}
        onClose={() => { if (!activate.isPending) setActivationConfirmOpen(false); }}
      />
      <ConfirmDialog
        open={emergencyConfirmOpen}
        title={t('identityPage.configureEmergencyCredential')}
        description={t('identityPage.configuringANewCredentialSupersedesThePreviousOneUseItOnlyWhenTheIdentit')}
        confirmLabel={t('identityPage.configureEmergencyCredential')}
        cancelLabel={t('common.no')}
        tone="danger"
        loading={emergency.isPending}
        onConfirm={() => { setEmergencyConfirmOpen(false); emergency.mutate(); }}
        onClose={() => { if (!emergency.isPending) setEmergencyConfirmOpen(false); }}
      />
    </Stack>
  );
}

function PolicyForm({ tenantId, initial }: { tenantId: string; initial: Policy }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [saved, setSaved] = useState(initial);
  const [draft, setDraft] = useState(initial);
  const dirty = JSON.stringify(draft) !== JSON.stringify(saved);
  useEffect(() => {
    if (dirty) return;
    const hydrateTimer = setTimeout(() => {
      setSaved(initial);
      setDraft(initial);
    }, 0);
    return () => clearTimeout(hydrateTimer);
  }, [initial, dirty]);
  const save = useMutation({
    mutationFn: () => api.updateAuthenticationPolicy(tenantId, draft),
    onSuccess: async result => {
      await queryClient.invalidateQueries({ queryKey: ['authentication-policy', tenantId] });
      setSaved(result);
      setDraft(result);
    }
  });
  return (
    <Stack spacing={2}>
      <Grid columns={2}>
        <Select label={t('identityPage.authenticationMode')} value={draft.mode} disabled={save.isPending} onChange={mode => setDraft({ ...draft, mode })} options={[
          { value: 'optional', label: t('identityPage.optional') }, { value: 'required', label: t('identityPage.requiredOIDCSSO') }
        ]} />
        <Select label={t('identityPage.requireGroupClaim')} value={draft.requireGroupClaim ? 'yes' : 'no'} disabled={save.isPending} onChange={value => setDraft({ ...draft, requireGroupClaim: value === 'yes' })} options={[
          { value: 'no', label: t('common.no') }, { value: 'yes', label: t('identityPage.yesFailClosedWhenMissing') }
        ]} />
      </Grid>
      <Grid columns={2}>
        <FormField label={t('identityPage.allowedACRValuesCommaSeparated')}><TextInput value={draft.allowedAcrValues.join(',')} disabled={save.isPending} onChange={value => setDraft({ ...draft, allowedAcrValues: split(value) })} /></FormField>
        <FormField label={t('identityPage.requiredAMRValuesCommaSeparated')}><TextInput value={draft.requiredAmrValues.join(',')} disabled={save.isPending} onChange={value => setDraft({ ...draft, requiredAmrValues: split(value) })} /></FormField>
      </Grid>
      <Grid columns={3}>
        <FormField label={t('identityPage.maximumAuthenticationAgeMinutes')}><TextInput type="number" value={String(draft.maxAuthenticationAgeMinutes)} disabled={save.isPending} onChange={value => setDraft({ ...draft, maxAuthenticationAgeMinutes: Number(value) })} /></FormField>
        <FormField label={t('identityPage.maximumSessionMinutes')}><TextInput type="number" value={String(draft.maxSessionMinutes)} disabled={save.isPending} onChange={value => setDraft({ ...draft, maxSessionMinutes: Number(value) })} /></FormField>
        <FormField label={t('identityPage.idleTimeoutMinutes')}><TextInput type="number" value={String(draft.idleTimeoutMinutes)} disabled={save.isPending} onChange={value => setDraft({ ...draft, idleTimeoutMinutes: Number(value) })} /></FormField>
      </Grid>
      <Stack direction="row" wrap spacing={1.5} alignItems="center">
        <Button onClick={() => save.mutate()} disabled={!dirty || save.isPending} loading={save.isPending}>{t('identityPage.saveAuthenticationPolicy')}</Button>
        {dirty ? <Text color="warning">{t('identityPage.unsavedAuthenticationPolicyChanges')}</Text> : null}
        <LocalizedMutationFeedback mutation={save} successMessage={t('identityPage.authenticationPolicySaved')} />
      </Stack>
    </Stack>
  );
}

function split(value: string) {
  return value.split(',').map(item => item.trim()).filter(Boolean);
}
