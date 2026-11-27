import { useState, type FormEvent } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import {
  AuthLayout,
  Button,
  Card,
  ErrorState,
  FormField,
  Grid,
  Icon,
  LoadingState,
  Select,
  Stack,
  Text,
  TextInput,
  resolveLocale,
  validateReturnTarget,
  type AppLocale
} from '@regent/ui-shared';
import { useAuth } from '../app/useAuth';
import { authErrorKey, type AuthErrorKey } from '../lib/authErrors';
import i18n from '../lib/i18n';
import { authenticationModes, defaultAuthMode, type AuthMode } from './authModes';
import { EmergencyRecovery, RegistrationRecovery } from './auth/RecoveryMaterial';

const submitKeyByMode: Record<AuthMode, string> = {
  dev: 'auth.submit.signIn',
  password: 'auth.submit.signIn',
  register: 'auth.submit.createAccount',
  emergency: 'auth.submit.emergency'
};

export function LoginPage() {
  const { t } = useTranslation();
  const { login, passwordLogin, register, emergencyLogin, logout, status, session, error, retry } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const returnTarget = validateReturnTarget(new URLSearchParams(location.search).get('returnTo')) ?? '/';
  const locale = resolveLocale(i18n.resolvedLanguage ?? i18n.language);
  const production = import.meta.env.PROD;
  const [mode, setMode] = useState<AuthMode>(() => defaultAuthMode(production));
  const [email, setEmail] = useState(() => t('auth.emailPlaceholder'));
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [companyName, setCompanyName] = useState('');
  const [tenantId, setTenantId] = useState('');
  const [credential, setCredential] = useState('');
  const [purpose, setPurpose] = useState('');
  const [rotatedCredential, setRotatedCredential] = useState('');
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [errorKey, setErrorKey] = useState<AuthErrorKey | null>(null);
  const [loading, setLoading] = useState(false);
  const [logoutLoading, setLogoutLoading] = useState(false);

  const changeLocale = (next: AppLocale) => { void i18n.changeLanguage(next); };
  const modeOptions = authenticationModes(production).map(value => ({ value, label: t(`auth.modes.${value}`) }));
  const submitLabel = t(submitKeyByMode[mode]);
  const layout = {
    audience: t('auth.audience'),
    headline: t('auth.headline'),
    description: t('auth.description'),
    highlights: [
      { icon: 'organization' as const, title: t('auth.highlightDeliveryTitle'), description: t('auth.highlightDeliveryDescription') },
      { icon: 'success' as const, title: t('auth.highlightGovernanceTitle'), description: t('auth.highlightGovernanceDescription') },
      { icon: 'evaluation' as const, title: t('auth.highlightKnowledgeTitle'), description: t('auth.highlightKnowledgeDescription') }
    ],
    footer: t('auth.footer'),
    locale,
    onLocaleChange: changeLocale,
    localeLabel: t('auth.localeLabel')
  };

  function changeMode(next: AuthMode) {
    setMode(next);
    setErrorKey(null);
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (loading) return;
    setLoading(true);
    setErrorKey(null);
    try {
      if (mode === 'dev') await login(email);
      else if (mode === 'password') await passwordLogin(email, password);
      else if (mode === 'emergency') setRotatedCredential(await emergencyLogin(tenantId, credential, purpose));
      else setRecoveryCodes(await register(email, password, displayName, companyName));
      if (mode === 'dev' || mode === 'password') navigate(returnTarget, { replace: true });
    } catch (cause) {
      setErrorKey(authErrorKey(cause, mode));
    } finally {
      setLoading(false);
    }
  }

  async function signOut() {
    if (logoutLoading) return;
    setLogoutLoading(true);
    setErrorKey(null);
    try {
      await logout();
    } catch {
      setErrorKey('auth.errors.unexpected');
    } finally {
      setLogoutLoading(false);
    }
  }

  if (recoveryCodes.length > 0) {
    return <AuthLayout {...layout}>
      <RegistrationRecovery
        codes={recoveryCodes}
        title={t('auth.recovery.title')}
        description={t('auth.recovery.description')}
        continueLabel={t('auth.recovery.continue')}
        onContinue={() => navigate(returnTarget, { replace: true })}
      />
    </AuthLayout>;
  }

  if (rotatedCredential) {
    return <AuthLayout {...layout}>
      <EmergencyRecovery
        credential={rotatedCredential}
        title={t('auth.emergencyResult.title')}
        description={t('auth.emergencyResult.description')}
        continueLabel={t('auth.emergencyResult.continue')}
        onContinue={() => navigate('/settings')}
      />
    </AuthLayout>;
  }

  if (status === 'checking') {
    return <AuthLayout {...layout}><LoadingState label={t('auth.loading.checking')} /></AuthLayout>;
  }

  if (status === 'permission-denied' || status === 'error') {
    return <AuthLayout {...layout}><Stack spacing={2}>
      <ErrorState message={error ?? t('auth.errors.unexpected')} />
      <Button tone="neutral" onClick={() => void retry().catch(() => undefined)}>{t('common.retry')}</Button>
    </Stack></AuthLayout>;
  }

  if (session) {
    return <AuthLayout {...layout}>
      <Stack spacing={2.5}>
        <Stack direction="row" spacing={1.5} alignItems="center">
          <Icon name="success" sx={{ color: 'success.main' }} />
          <Text variant="h2">{t('auth.signedIn.title', { name: session.displayName })}</Text>
        </Stack>
        <Text color="text.secondary">{t('auth.signedIn.description')}</Text>
        {errorKey ? <ErrorState message={t(errorKey)} /> : null}
        <Stack direction="row" spacing={1.5} wrap>
          <Button onClick={() => navigate(returnTarget, { replace: true })}>{t('auth.signedIn.continue')}</Button>
          <Button tone="neutral" loading={logoutLoading} aria-label={t('auth.signedIn.logout')} onClick={signOut}>{t('auth.signedIn.logout')}</Button>
        </Stack>
        {logoutLoading ? <Text role="status">{t('auth.loading.loggingOut')}</Text> : null}
      </Stack>
    </AuthLayout>;
  }

  return <AuthLayout {...layout}>
    <form onSubmit={submit}>
      <Stack spacing={2.5}>
        <Stack spacing={0.75}>
          <Stack direction="row" spacing={1} alignItems="center">
            <Icon name="security" sx={{ color: 'primary.main' }} />
            <Text variant="h2">{t('auth.title')}</Text>
          </Stack>
          <Text color="text.secondary">{t('auth.intro')}</Text>
        </Stack>
        <Select
          label={t('auth.modeLabel')}
          value={mode}
          options={modeOptions}
          onChange={changeMode}
          disabled={loading}
        />
        {mode === 'dev' ? <Card tone="warning" padding="small"><Text color="warning.main">{t('auth.devNotice')}</Text></Card> : null}
        {mode === 'emergency' ? <Card tone="warning" padding="small">
          <Stack spacing={2}>
            <FormField label={t('auth.tenantId')} required>
              <TextInput
                value={tenantId}
                onChange={setTenantId}
                placeholder={t('auth.tenantIdPlaceholder')}
                disabled={loading}
                required
                inputProps={{ 'aria-label': t('auth.tenantId'), autoComplete: 'off' }}
              />
            </FormField>
            <FormField label={t('auth.emergencyCredential')} required>
              <TextInput
                value={credential}
                onChange={setCredential}
                type="password"
                disabled={loading}
                required
                inputProps={{ 'aria-label': t('auth.emergencyCredential'), autoComplete: 'off' }}
              />
            </FormField>
            <FormField label={t('auth.purpose')} required>
              <TextInput
                value={purpose}
                onChange={setPurpose}
                placeholder={t('auth.purposePlaceholder')}
                disabled={loading}
                required
                multiline
                minRows={2}
                inputProps={{ 'aria-label': t('auth.purpose'), autoComplete: 'off' }}
              />
            </FormField>
          </Stack>
        </Card> : <>
          <FormField label={t('auth.email')} required>
            <TextInput
              value={email}
              onChange={setEmail}
              type="email"
              placeholder={t('auth.emailPlaceholder')}
              disabled={loading}
              required
              inputProps={{ 'aria-label': t('auth.email'), autoComplete: 'email' }}
            />
          </FormField>
          {mode !== 'dev' ? <FormField label={t('auth.password')} hint={mode === 'register' ? t('auth.passwordPlaceholder') : undefined} required>
            <TextInput
              value={password}
              onChange={setPassword}
              type="password"
              placeholder={t('auth.passwordPlaceholder')}
              disabled={loading}
              required
              inputProps={{
                'aria-label': t('auth.password'),
                autoComplete: mode === 'register' ? 'new-password' : 'current-password',
                minLength: mode === 'register' ? 10 : undefined
              }}
            />
          </FormField> : null}
        </>}
        {mode === 'register' ? <Grid columns={2}>
          <FormField label={t('auth.displayName')} required>
            <TextInput
              value={displayName}
              onChange={setDisplayName}
              placeholder={t('auth.displayNamePlaceholder')}
              disabled={loading}
              required
              inputProps={{ 'aria-label': t('auth.displayName'), autoComplete: 'name' }}
            />
          </FormField>
          <FormField label={t('auth.companyName')} required>
            <TextInput
              value={companyName}
              onChange={setCompanyName}
              placeholder={t('auth.companyNamePlaceholder')}
              disabled={loading}
              required
              inputProps={{ 'aria-label': t('auth.companyName'), autoComplete: 'organization' }}
            />
          </FormField>
        </Grid> : null}
        {errorKey ? <ErrorState message={t(errorKey)} /> : null}
        <Button
          type="submit"
          size="large"
          loading={loading}
          aria-label={submitLabel}
          sx={{ width: '100%' }}
          disabled={
            (mode === 'register' && (!displayName || !companyName || password.length < 10))
            || (mode === 'emergency' && (!tenantId || !credential || !purpose))
          }
        >
          {submitLabel}
        </Button>
        {loading ? <Text role="status">{t('auth.loading.submitting')}</Text> : null}
      </Stack>
    </form>
  </AuthLayout>;
}
