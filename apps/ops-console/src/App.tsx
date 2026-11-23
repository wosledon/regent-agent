import { useState } from 'react';
import { Navigate, NavLink, Route, Routes, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AppBar, Box, Drawer, List, ListItemButton, ListItemText, Toolbar } from '@mui/material';
import { Button, Card, DataTable, EmptyState, ErrorState, LoadingState, SemanticIconButton, StatusChip, Text, Icon } from '@regent/ui-shared';
import { AuthProvider } from './app/AuthProvider';
import { useAuth } from './app/useAuth';
import { api } from './lib/api';

export function App() {
  return <AuthProvider><Routes>
    <Route path="/login" element={<Login />} />
    <Route element={<Protected />}>
      <Route index element={<Overview />} />
      <Route path="tenants" element={<Tenants />} />
      <Route path="support" element={<Support />} />
      <Route path="backups" element={<Backups />} />
      <Route path="audit" element={<Audit />} />
    </Route>
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></AuthProvider>;
}

function Protected() {
  const { user } = useAuth();
  return user ? <Shell /> : <Navigate to="/login" replace />;
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
    ['/','Overview'], ['/tenants','Tenants'], ['/support','Support grants'], ['/backups','Backups'], ['/audit','Audit']
  ];
  return <Box sx={{ display: 'flex' }}>
    <AppBar position="fixed"><Toolbar><SemanticIconButton label="Open navigation" color="inherit" onClick={() => setOpen(true)}><Icon name="dashboard" /></SemanticIconButton><Text variant="h2" sx={{ flexGrow: 1, color: 'inherit' }}>Regent Operations</Text><Button tone="neutral" onClick={logout}>Logout</Button></Toolbar></AppBar>
    <Drawer open={open} onClose={() => setOpen(false)}><Box sx={{ width: 260 }}><Toolbar /><List>{links.map(([to,label]) => <ListItemButton key={to} component={NavLink} to={to}><ListItemText primary={label} /></ListItemButton>)}</List></Box></Drawer>
    <Box component="main" sx={{ p: 3, mt: 8, width: '100%' }}><Text variant="caption">{user}</Text><Routes>
      <Route index element={<Overview />} /><Route path="tenants" element={<Tenants />} /><Route path="support" element={<Support />} /><Route path="backups" element={<Backups />} /><Route path="audit" element={<Audit />} />
    </Routes></Box>
  </Box>;
}

function Overview() {
  const tenants = useQuery({ queryKey: ['ops-tenants'], queryFn: api.tenants });
  const grants = useQuery({ queryKey: ['ops-grants'], queryFn: api.grants });
  if (tenants.isLoading || grants.isLoading) return <LoadingState />;
  return <div><Text variant="h1">Operations Overview</Text><Card title="Tenants"><Text variant="h2">{tenants.data?.length ?? 0}</Text></Card><Card title="Active support grants"><Text variant="h2">{grants.data?.filter(x => x.status === 'Active').length ?? 0}</Text></Card><Card title="Local mode"><Text>SQLite · local files · no containers</Text></Card></div>;
}

function Tenants() {
  const { data, isLoading, error } = useQuery({ queryKey: ['ops-tenants'], queryFn: api.tenants });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  return <div><Text variant="h1">Tenants</Text><DataTable rows={data ?? []} getRowId={row => row.id} columns={[
    { id: 'name', header: 'Tenant', render: row => row.name },
    { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone="success" /> },
    { id: 'employees', header: 'Employees', render: row => row.employees },
    { id: 'runners', header: 'Runners', render: row => row.runnerCount }
  ]} /></div>;
}

function Support() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['ops-grants'], queryFn: api.grants });
  const create = useMutation({ mutationFn: () => api.createGrant('demo'), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['ops-grants'] }) });
  const revoke = useMutation({ mutationFn: (id: string) => api.revokeGrant(id), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['ops-grants'] }) });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  return <div><Text variant="h1">Support Grants</Text><Button onClick={() => create.mutate()} loading={create.isPending}>Create 30-minute read-only grant</Button>{!data?.length ? <EmptyState title="No grants" /> : null}{data?.map(grant => <Card key={grant.id} title={`${grant.tenantId} · ${grant.scope}`} action={<StatusChip label={grant.status} tone="info" />}><Text>Expires: {new Date(grant.expiresAt).toLocaleString()}</Text><Button tone="danger" onClick={() => revoke.mutate(grant.id)}>Revoke</Button></Card>)}</div>;
}

function Backups() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['ops-backups'], queryFn: api.backups });
  const save = useMutation({ mutationFn: (enabled: boolean) => api.setBackup(enabled), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['ops-backups'] }) });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  const profile = data?.[0];
  return <div><Text variant="h1">Backups</Text><Card title="Local backup profile"><Text>Status: {profile?.enabled ? 'Enabled' : 'Disabled'}</Text><Text>Components: {profile?.components}</Text><Button onClick={() => save.mutate(!profile?.enabled)} loading={save.isPending}>{profile?.enabled ? 'Disable' : 'Enable'}</Button></Card></div>;
}

function Audit() {
  const { data, isLoading, error } = useQuery({ queryKey: ['ops-audit'], queryFn: api.audit });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  return <div><Text variant="h1">Operations Audit</Text><DataTable rows={data ?? []} getRowId={row => row.id} columns={[
    { id: 'operator', header: 'Operator', render: row => row.operatorId },
    { id: 'action', header: 'Action', render: row => row.action },
    { id: 'target', header: 'Target', render: row => row.target },
    { id: 'time', header: 'Time', render: row => new Date(row.createdAt).toLocaleString() }
  ]} /></div>;
}
