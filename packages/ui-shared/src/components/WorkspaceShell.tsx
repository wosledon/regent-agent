import { useState, type PropsWithChildren, type ReactNode } from 'react';
import { useMediaQuery, useTheme } from '@mui/material';
import {
  AppBar,
  Box,
  Divider,
  Drawer,
  List,
  ListItemButton,
  ListItemText,
  Stack,
  Toolbar,
  Typography
} from '@mui/material';
import { designTokens } from '../theme/theme';
import { Avatar } from './Avatar';
import { Icon, type IconName } from './Icon';
import { SemanticIconButton } from './IconButton';
import { StatusChip, type ChipTone } from './StatusChip';
import { Text } from './Text';
import { ContentContainer } from './Layout';

export interface WorkspaceNavItem {
  key: string;
  label: string;
  path: string;
  icon?: IconName;
  badge?: ReactNode;
}

export interface WorkspaceNavGroup {
  key: string;
  label?: string;
  items: WorkspaceNavItem[];
}

export interface WorkspaceUser {
  name: string;
  secondary?: string;
}

export interface WorkspaceShellProps extends PropsWithChildren {
  brand: string;
  navigation: WorkspaceNavGroup[];
  currentPath: string;
  onNavigate: (path: string) => void;
  user?: WorkspaceUser | null;
  userActions?: ReactNode;
  context?: ReactNode;
  headerActions?: ReactNode;
  connection?: { label: string; tone: ChipTone };
  footer?: ReactNode;
  navigationLabel: string;
  openNavigationLabel: string;
  closeNavigationLabel: string;
  skipNavigationLabel: string;
  contentMaxWidth?: number | string;
}

function isActivePath(itemPath: string, currentPath: string) {
  if (itemPath === '/') return currentPath === '/';
  return currentPath === itemPath || currentPath.startsWith(`${itemPath}/`);
}

export function WorkspaceShell({
  brand,
  navigation,
  currentPath,
  onNavigate,
  user,
  userActions,
  context,
  headerActions,
  connection,
  footer,
  navigationLabel,
  openNavigationLabel,
  closeNavigationLabel,
  skipNavigationLabel,
  contentMaxWidth,
  children
}: WorkspaceShellProps) {
  const theme = useTheme();
  const desktop = useMediaQuery(theme.breakpoints.up('lg'));
  const [mobileNavigation, setMobileNavigation] = useState({ open: false, path: currentPath });
  const mobileOpen = mobileNavigation.open && mobileNavigation.path === currentPath;
  const setMobileOpen = (open: boolean) => setMobileNavigation({ open, path: currentPath });
  const allItems = navigation.flatMap(group => group.items);
  const activePath = allItems
    .filter(item => isActivePath(item.path, currentPath))
    .sort((left, right) => right.path.length - left.path.length)[0]?.path;

  const navigate = (path: string) => {
    setMobileOpen(false);
    if (path !== currentPath) onNavigate(path);
  };

  const sidebar = (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', bgcolor: designTokens.color.navBackground, color: designTokens.color.navText }}>
      <Toolbar sx={{ px: 2.5, minHeight: `${designTokens.layout.topbarHeight}px !important` }}>
        <Stack direction="row" spacing={1.25} alignItems="center" sx={{ minWidth: 0 }}>
          <Box sx={{ width: 30, height: 30, borderRadius: 1.5, display: 'grid', placeItems: 'center', bgcolor: designTokens.color.primary, flexShrink: 0 }}>
            <Icon name="agent" sx={{ color: 'common.white', fontSize: 18 }} />
          </Box>
          <Typography component="div" sx={{ fontSize: '.9375rem', fontWeight: 700, letterSpacing: '-0.01em', whiteSpace: 'nowrap' }}>{brand}</Typography>
        </Stack>
      </Toolbar>
      <Divider sx={{ borderColor: designTokens.color.navBorder }} />
      <Box component="nav" aria-label={navigationLabel} sx={{ flex: 1, overflowY: 'auto', px: 1.5, py: 2 }}>
        <Stack spacing={2.5}>
          {navigation.map(group => (
            <Stack key={group.key} spacing={0.5}>
              {group.label ? <Text variant="overline" sx={{ px: 1.25, color: designTokens.color.navTextMuted }}>{group.label}</Text> : null}
              <List disablePadding>
                {group.items.map(item => {
                  const selected = item.path === activePath;
                  return (
                    <ListItemButton
                      key={item.key}
                      component="a"
                      href={item.path}
                      selected={selected}
                      aria-current={selected ? 'page' : undefined}
                      onClick={event => { event.preventDefault(); navigate(item.path); }}
                      sx={{
                        minHeight: 36,
                        mb: 0.25,
                        px: 1.25,
                        gap: 1.25,
                        color: designTokens.color.navTextMuted,
                        borderRadius: 1,
                        position: 'relative',
                        '&:hover': { bgcolor: designTokens.color.navSurface, color: designTokens.color.navText },
                        '&.Mui-selected': { bgcolor: designTokens.color.navActive, color: designTokens.color.navText, '&:hover': { bgcolor: designTokens.color.navActive } },
                        '&.Mui-selected::before': { content: '""', position: 'absolute', left: -6, top: 8, bottom: 8, width: 3, borderRadius: 2, bgcolor: designTokens.color.navIndicator },
                        '& .MuiListItemIcon-root': { color: 'inherit', minWidth: 20 },
                        '& .MuiListItemText-primary': { color: 'inherit', fontSize: '.8125rem', fontWeight: selected ? 600 : 500 }
                      }}
                    >
                      {item.icon ? <Icon name={item.icon} sx={{ fontSize: 18 }} /> : null}
                      <ListItemText primary={item.label} />
                      {item.badge}
                    </ListItemButton>
                  );
                })}
              </List>
            </Stack>
          ))}
        </Stack>
      </Box>
      {(footer || user) ? (
        <>
          <Divider sx={{ borderColor: designTokens.color.navBorder }} />
          <Stack sx={{ p: 2, gap: 1.5 }}>
            {footer}
            {user ? (
              <Stack direction="row" spacing={1.25} alignItems="center" sx={{ minWidth: 0 }}>
                <Avatar name={user.name} size="small" status="Active" />
                <Stack sx={{ minWidth: 0 }}>
                  <Text sx={{ color: designTokens.color.navText, fontSize: '.8125rem', fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{user.name}</Text>
                  {user.secondary ? <Text variant="caption" sx={{ color: designTokens.color.navTextMuted, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{user.secondary}</Text> : null}
                </Stack>
              </Stack>
            ) : null}
          </Stack>
        </>
      ) : null}
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Box component="a" href="#main-content" sx={{ position: 'fixed', left: 16, top: -80, zIndex: theme.zIndex.modal + 1, bgcolor: 'primary.main', color: 'primary.contrastText', px: 2, py: 1, borderRadius: 1, '&:focus': { top: 12 } }}>
        {skipNavigationLabel}
      </Box>
      {desktop ? (
        <Drawer variant="permanent" sx={{ width: designTokens.layout.sidebarWidth, flexShrink: 0, '& .MuiDrawer-paper': { width: designTokens.layout.sidebarWidth, boxSizing: 'border-box', borderRight: 0 } }}>
          {sidebar}
        </Drawer>
      ) : (
        <Drawer
          variant="temporary"
          open={mobileOpen}
          onClose={() => setMobileOpen(false)}
          ModalProps={{ keepMounted: true }}
          PaperProps={{ sx: { width: 280, bgcolor: designTokens.color.navBackground, borderRight: 0 } }}
        >
          <Box sx={{ display: 'flex', justifyContent: 'flex-end', p: 1 }}>
            <SemanticIconButton label={closeNavigationLabel} onClick={() => setMobileOpen(false)} sx={{ color: designTokens.color.navTextMuted, '&:hover': { bgcolor: designTokens.color.navSurface, color: designTokens.color.navText } }}>
              <Icon name="close" />
            </SemanticIconButton>
          </Box>
          {sidebar}
        </Drawer>
      )}
      <AppBar position="fixed" color="inherit" elevation={0} sx={{ left: { lg: designTokens.layout.sidebarWidth }, width: { lg: `calc(100% - ${designTokens.layout.sidebarWidth}px)` } }}>
        <Toolbar sx={{ gap: 1.5 }}>
          {!desktop ? (
            <SemanticIconButton label={openNavigationLabel} edge="start" onClick={() => setMobileOpen(true)}>
              <Icon name="menu" />
            </SemanticIconButton>
          ) : null}
          {!desktop ? <Text sx={{ fontWeight: 700, fontSize: '.9375rem' }}>{brand}</Text> : null}
          <Box sx={{ flexGrow: 1, minWidth: 0, display: { xs: 'none', sm: 'block' } }}>{context}</Box>
          {connection ? <StatusChip tone={connection.tone} label={connection.label} sx={{ mr: 0.5, display: { xs: 'none', sm: 'inline-flex' } }} /> : null}
          {headerActions ? <Stack direction="row" spacing={ 1} alignItems="center">{headerActions}</Stack> : null}
          {userActions ? <Stack direction="row" spacing={1} alignItems="center">{userActions}</Stack> : null}
        </Toolbar>
      </AppBar>
      <Box component="main" id="main-content" sx={{ flexGrow: 1, minWidth: 0, mt: `${designTokens.layout.topbarHeight}px`, ml: { lg: `${designTokens.layout.sidebarWidth}px` } }}>
        <ContentContainer maxWidth={contentMaxWidth}>{children}</ContentContainer>
      </Box>
    </Box>
  );
}
