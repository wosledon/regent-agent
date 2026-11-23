import { useState } from 'react';
import { Navigate, NavLink, Outlet, Route, Routes, useNavigate } from 'react-router-dom';
import { AppBar, Box, Drawer, List, ListItemButton, ListItemText, Toolbar } from '@mui/material';
import { Button, Card, Icon, SemanticIconButton, Text } from '@regent/ui-shared';
import { AuthProvider } from './app/AuthProvider';
import { useAuth } from './app/useAuth';
import { AuditPage } from './pages/AuditPage';
import { BackupsPage } from './pages/BackupsPage';
import { OverviewPage } from './pages/OverviewPage';
import { StatusPage } from './pages/StatusPage';
import { SupportPage } from './pages/SupportPage';
import { TenantsPage } from './pages/TenantsPage';

export function App() {
  return <AuthProvider><Routes>
    <Route path="/login" element={<Login />} />
    <Route path="/status" element={<StatusPage />} />
    <Route element={<Protected />}>
      <Route element={<Shell />}>
        <Route index element={<OverviewPage />} />
        <Route path="tenants" element={<TenantsPage />} />
        <Route path="support" element={<SupportPage />} />
        <Route path="backups" element={<BackupsPage />} />
        <Route path="audit" element={<AuditPage />} />
        <Route path="platform" element={<StatusPage operational />} />
      </Route>
    </Route>
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></AuthProvider>;
}

function Protected() {
  const { user } = useAuth();
  return user ? <Outlet /> : <Navigate to="/login" replace />;
}

function Login() {
  const { login, user } = useAuth();
  const navigate = useNavigate();
  if (user) return <Navigate to="/" replace />;
  return <Card title="Operations sign in"><Button onClick={async () => { await login(); navigate('/'); }}>Sign in as Demo Operator</Button></Card>;
}

function Shell() {
  const [open, setOpen] = useState(false);
  const { user, logout } = useAuth();
  const links = [
    ['/', 'Overview'], ['/tenants', 'Tenants'], ['/support', 'Support grants'],
    ['/backups', 'Backups'], ['/audit', 'Audit'], ['/platform', 'Platform status'], ['/status', 'Public status']
  ];
  return <Box sx={{ display: 'flex' }}>
    <AppBar position="fixed"><Toolbar><SemanticIconButton label="Open navigation" color="inherit" onClick={() => setOpen(true)}><Icon name="dashboard" /></SemanticIconButton><Text variant="h2" sx={{ flexGrow: 1, color: 'inherit' }}>Regent Operations</Text><Button tone="neutral" onClick={logout}>Logout</Button></Toolbar></AppBar>
    <Drawer open={open} onClose={() => setOpen(false)}><Box sx={{ width: 260 }}><Toolbar /><List>{links.map(([to, label]) =>
      <ListItemButton key={to} component={NavLink} to={to}><ListItemText primary={label} /></ListItemButton>)}</List></Box></Drawer>
    <Box component="main" sx={{ p: 3, mt: 8, width: '100%' }}><Text variant="caption">{user}</Text><Outlet /></Box>
  </Box>;
}
