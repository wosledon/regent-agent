import { useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { AppBar, Box, Drawer, List, ListItemButton, ListItemText, Toolbar } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { Button, Icon, SemanticIconButton, StatusChip, Text } from '@regent/ui-shared';
import { useAuth } from './useAuth';
import { useTenantHub } from '../hooks/useTenantHub';

const navigation = [
  { key: 'dashboard', path: '/', icon: 'dashboard' as const },
  { key: 'projects', path: '/projects', icon: 'evaluation' as const },
  { key: 'organization', path: '/organization', icon: 'organization' as const },
  { key: 'messages', path: '/messages', icon: 'notification' as const },
  { key: 'models', path: '/models', icon: 'evaluation' as const },
  { key: 'knowledge', path: '/knowledge', icon: 'evaluation' as const },
  { key: 'governance', path: '/governance/approvals', icon: 'notification' as const },
  { key: 'settings', path: '/settings', icon: 'settings' as const }
];

export function AppShell() {
  const { t } = useTranslation();
  const { session, logout } = useAuth();
  const hubState = useTenantHub(Boolean(session));
  const [open, setOpen] = useState(false);
  const location = useLocation();

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <AppBar position="fixed" sx={{ zIndex: theme => theme.zIndex.drawer + 1 }}>
        <Toolbar>
          <SemanticIconButton label="Open navigation" color="inherit" onClick={() => setOpen(true)}>
            <Icon name="dashboard" />
          </SemanticIconButton>
          <Text variant="h2" sx={{ flexGrow: 1, color: 'inherit' }}>Regent Agent</Text>
          <StatusChip tone={hubState === 'connected' ? 'success' : 'warning'} label={hubState} />
          <Button tone="neutral" onClick={() => void logout()}>Logout</Button>
        </Toolbar>
      </AppBar>
      <Drawer open={open} onClose={() => setOpen(false)}>
        <Box sx={{ width: 260 }} role="presentation" onClick={() => setOpen(false)}>
          <Toolbar />
          <List>
            {navigation.map(item => (
              <ListItemButton key={item.path} component={NavLink} to={item.path} selected={location.pathname === item.path}>
                <Icon name={item.icon} />
                <ListItemText primary={t(item.key)} sx={{ ml: 2 }} />
              </ListItemButton>
            ))}
          </List>
        </Box>
      </Drawer>
      <Box component="main" sx={{ flexGrow: 1, p: 3, mt: 8 }}>
        <Text variant="caption">Signed in as {session?.displayName}</Text>
        <Outlet />
      </Box>
    </Box>
  );
}
