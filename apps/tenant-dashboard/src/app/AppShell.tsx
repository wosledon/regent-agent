import { useEffect } from 'react';
import { Box } from '@mui/material';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button, Icon, LocaleSwitch, Text, WorkspaceShell, resolveLocale, trackBrowserPageView } from '@regent/ui-shared';
import { useAuth } from './useAuth';
import { isRestrictedSessionKind } from './AuthContext';
import { useTenantHub } from '../hooks/useTenantHub';
import { buildTenantNavigation } from './navigation';

export function AppShell() {
  const { t, i18n } = useTranslation();
  const { session, logout } = useAuth();
  const restricted = isRestrictedSessionKind(session?.sessionKind);
  const hubState = useTenantHub(Boolean(session) && !restricted);
  const location = useLocation();
  const navigate = useNavigate();
  useEffect(() => { trackBrowserPageView(location.pathname); }, [location.pathname]);

  const canReadChat = !restricted && session?.effectiveAccess?.canReadChat === true;
  const canReadCollaboration = !restricted && session?.effectiveAccess?.canReadCollaboration === true;
  const navigation = buildTenantNavigation({
    translate: key => t(key),
    canReadChat,
    canReadCollaboration,
    development: import.meta.env.DEV
  });
  const connectionTone = hubState === 'connected' ? 'success' : hubState === 'reconnecting' ? 'warning' : 'error';
  const connectionKey = hubState;

  return (
    <WorkspaceShell
      brand="Regent Agent"
      navigation={navigation}
      currentPath={location.pathname}
      onNavigate={navigate}
      navigationLabel={t('shell.navigationLabel')}
      openNavigationLabel={t('shell.openNavigation')}
      closeNavigationLabel={t('shell.closeNavigation')}
      skipNavigationLabel={t('shell.skipNavigation')}
      connection={{ label: t(`shell.connection.${connectionKey}`), tone: connectionTone }}
      context={<Text variant="caption" color="text.secondary" sx={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{t('shell.signedInAs', { name: session?.displayName ?? '' })}</Text>}
      headerActions={<LocaleSwitch value={resolveLocale(i18n.resolvedLanguage ?? i18n.language)} onChange={language => void i18n.changeLanguage(language)} label={t('auth.localeLabel')} />}
      user={{ name: session?.displayName ?? '', secondary: session?.role }}
      userActions={<Button tone="neutral" startIcon={<Icon name="logout" />} aria-label={t('shell.logout')} onClick={() => void logout()} sx={{ minWidth: { xs: 36, sm: 96 }, px: { xs: 1, sm: 1.75 } }}><Box component="span" sx={{ display: { xs: 'none', sm: 'inline' } }}>{t('shell.logout')}</Box></Button>}
    >
      <Outlet />
    </WorkspaceShell>
  );
}
