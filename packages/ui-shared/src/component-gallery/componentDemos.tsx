import { useState } from 'react';
import { Box } from '@mui/material';
import { Button } from '../components/Button';
import { Card } from '../components/Card';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { Text } from '../components/Text';
import { Stack } from '../components/Layout';
import { WorkspaceShell } from '../components/WorkspaceShell';

export function ConfirmDialogDemo() {
  const [open, setOpen] = useState(false);
  return (
    <Stack spacing={2}>
      <Button tone="danger" onClick={() => setOpen(true)}>Delete record</Button>
      <ConfirmDialog
        open={open}
        title="Delete record?"
        description="This action cannot be undone."
        confirmLabel="Delete"
        cancelLabel="Cancel"
        tone="danger"
        onClose={() => setOpen(false)}
        onConfirm={() => setOpen(false)}
      />
    </Stack>
  );
}

export function WorkspaceShellDemo() {
  const [path, setPath] = useState('/overview');
  return (
    <Box sx={{ height: 360, overflow: 'hidden', border: 1, borderColor: 'divider', borderRadius: 2 }}>
      <WorkspaceShell
        brand="Regent Agent"
        navigation={[
          { key: 'overview', items: [{ key: 'overview', label: 'Overview', path: '/overview', icon: 'overview' }] },
          { key: 'delivery', label: 'Delivery', items: [{ key: 'projects', label: 'Projects', path: '/projects', icon: 'projects' }] }
        ]}
        currentPath={path}
        onNavigate={setPath}
        navigationLabel="Demo navigation"
        openNavigationLabel="Open navigation"
        closeNavigationLabel="Close navigation"
        skipNavigationLabel="Skip to content"
        user={{ name: 'Demo Owner', secondary: 'Owner' }}
      >
        <Card title={path === '/projects' ? 'Projects' : 'Overview'}><Text>Workspace content</Text></Card>
      </WorkspaceShell>
    </Box>
  );
}
