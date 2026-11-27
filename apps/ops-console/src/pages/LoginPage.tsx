import { useState, type FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import {
  AuthLayout,
  Button,
  ErrorState,
  LoadingState,
  Stack,
  Text,
  resolveLocale,
  validateReturnTarget,
  type AppLocale
} from '@regent/ui-shared';
import { useAuth } from '../app/useAuth';
import { opsAuthErrorKey, type OpsAuthErrorKey } from '../lib/authErrors';
import i18n from '../lib/i18n';

export function LoginPage() {
  const { t } = useTranslation();
  const { login, status, user, retry } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const returnTarget = validateReturnTarget(new URLSearchParams(location.search).get('returnTo')) ?? '/';
  const locale = resolveLocale(i18n.resolvedLanguage ?? i18n.language);
  const [submitting, setSubmitting] = useState(false);
  const [errorKey, setErrorKey] = useState<OpsAuthErrorKey | null>(null);

  const layout = {
    audience: t('auth.audience'),
    headline: t('auth.headline'),
    description: t('auth.description'),
    highlights: [
      { icon: 'organization' as const, title: t('auth.highlightTenantsTitle'), description: t('auth.highlightTenantsDescription') },
      { icon: 'settings' as const, title: t('auth.highlightSupportTitle'), description: t('auth.highlightSupportDescription') },
      { icon: 'success' as const, title: t('auth.highlightRecoveryTitle'), description: t('auth.highlightRecoveryDescription') }
    ],
    footer: t('auth.footer'),
    locale,
    onLocaleChange: (next: AppLocale) => { void i18n.changeLanguage(next); },
    localeLabel: t('auth.localeLabel')
  };

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setErrorKey(null);
    try {
      await login();
      navigate(returnTarget, { replace: true });
    } catch (cause) {
      setErrorKey(opsAuthErrorKey(cause));
    } finally {
      setSubmitting(false);
    }
  }

  if (status === 'checking') {
    return <AuthLayout {...layout}><LoadingState label={t('auth.loading.checking')} /></AuthLayout>;
  }

  if (status === 'permission-denied' || status === 'error') {
    const message = status === 'permission-denied'
      ? t('common.errors.permissionDenied')
      : t('common.errors.sessionVerification');
    return <AuthLayout {...layout}><Stack spacing={2} sx={{ width: '100%', maxWidth: 360, mx: 'auto' }}>
      <ErrorState message={message} title={t('auth.title')} />
      <Button tone="neutral" onClick={() => void retry().catch(() => undefined)}>{t('common.retry')}</Button>
    </Stack></AuthLayout>;
  }

  if (user) return <Navigate to={returnTarget} replace />;

  return <AuthLayout {...layout}>
    <form onSubmit={submit}>
      <Stack spacing={2} sx={{ width: '100%', maxWidth: 360, mx: 'auto' }}>
        <Stack spacing={0.75}>
          <Text variant="overline" color="text.secondary">{t('auth.audience')}</Text>
          <Text variant="h2">{t('auth.title')}</Text>
          <Text variant="secondary">{t('auth.intro')}</Text>
        </Stack>
        <Stack spacing={0.5} sx={{ p: 1.5, border: 1, borderColor: 'divider', borderRadius: 1, bgcolor: 'background.default' }}>
          <Text variant="bodyStrong">{t('auth.noticeTitle')}</Text>
          <Text variant="secondary">{t('auth.notice')}</Text>
        </Stack>
        {errorKey ? <ErrorState message={t(errorKey)} title={t('auth.title')} /> : null}
        <Button type="submit" loading={submitting} aria-label={t('auth.action')} sx={{ width: '100%', minHeight: 40 }}>
          {t('auth.action')}
        </Button>
        {submitting ? <Text role="status" variant="caption" color="text.secondary" align="center">{t('auth.loading.submitting')}</Text> : null}
      </Stack>
    </form>
  </AuthLayout>;
}
