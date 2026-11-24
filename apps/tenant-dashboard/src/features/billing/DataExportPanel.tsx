import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, ErrorState, LoadingState, SemanticLink, Stack, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function DataExportPanel({ canManage }: { canManage: boolean }) {
  const queryClient = useQueryClient();
  const exports = useQuery({ queryKey: ['tenant-exports'], queryFn: api.exports, enabled: canManage, retry: false });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['tenant-exports'] });
  const create = useMutation({ mutationFn: api.createExport, onSuccess: invalidate });
  const validate = useMutation({ mutationFn: api.validateExport, onSuccess: invalidate });
  if (!canManage) return null;
  return <Card title="Tenant data export and migration package">
    <Text>Create a checksummed ZIP containing tenant database rows and domain module state. Credentials, sessions, recovery codes, external login transactions and KeyVault material are excluded.</Text>
    <Button onClick={() => create.mutate()} loading={create.isPending}>Create export package</Button>
    {exports.isLoading ? <LoadingState /> : null}
    {exports.error || create.error || validate.error ? <ErrorState message={(exports.error ?? create.error ?? validate.error)!.message} /> : null}
    <DataTable rows={exports.data ?? []} getRowId={row => row.id} columns={[
      { id: 'created', header: 'Created', render: row => new Date(row.createdAt).toLocaleString() },
      { id: 'tables', header: 'Tables', render: row => row.tableCount },
      { id: 'modules', header: 'Modules', render: row => row.moduleCount },
      { id: 'status', header: 'Status', render: row => <StatusChip label={row.validatedAt ? 'Validated' : row.status} tone={row.validatedAt ? 'success' : 'info'} /> },
      { id: 'actions', header: 'Actions', render: row => <Stack direction="row" spacing={1}>
        <SemanticLink href={`/v1/exports/${row.id}/content`}>Download</SemanticLink>
        <Button tone="secondary" onClick={() => validate.mutate(row.id)} loading={validate.isPending}>Validate</Button>
      </Stack> }
    ]} />
  </Card>;
}
