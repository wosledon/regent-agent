import { useEffect } from 'react';
import { Box } from '@mui/material';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button, Icon, LocaleSwitch, Text, WorkspaceShell, resolveLocale, trackBrowserPageView } from '@regent/ui-shared';
import { useAuth } from './useAuth';
import { buildOperationsNavigation } from './navigation';

export function AppShell() {
  const { t, i18n } = useTranslation();
  const { user, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  useEffect(() => { trackBrowserPageView(location.pathname); }, [location.pathname]);
  const navigation = buildOperationsNavigation({ translate: key => t(key) });

  return (
    <WorkspaceShell
      brand={t('common.productName')}
      navigation={navigation}
      currentPath={location.pathname}
      onNavigate={navigate}
      navigationLabel={t('shellLogin.navigationLabel')}
      openNavigationLabel={t('shellLogin.openNavigation')}
      closeNavigationLabel={t('shellLogin.closeNavigation')}
      skipNavigationLabel={t('shellLogin.skipNavigation')}
      context={<Text variant="caption" color="text.secondary">{user?.display_name}</Text>}
      headerActions={<LocaleSwitch value={resolveLocale(i18n.resolvedLanguage ?? i18n.language)} onChange={language => void i18n.changeLanguage(language)} label={t('auth.localeLabel')} />}
      user={{ name: user?.display_name ?? '', secondary: user?.role }}
      userActions={<Button tone="neutral" startIcon={<Icon name="logout" />} aria-label={t('shellLogin.logout')} onClick={() => void logout()} sx={{ minWidth: { xs: 36, sm: 96 }, px: { xs: 1, sm: 1.75 } }}><Box component="span" sx={{ display: { xs: 'none', sm: 'inline' } }}>{t('shellLogin.logout')}</Box></Button>}
    >
      <Outlet />
    </WorkspaceShell>
  );
}
