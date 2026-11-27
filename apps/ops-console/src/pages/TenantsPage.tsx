import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Button, Card, ConfirmDialog, DataTable, EmptyState, FilterBar, Grid, Icon, MetricCard,
  PageHeader, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { LocalizedErrorState, LocalizedLoadingState } from '../components/LocalizedStates';
import { api, type TenantAccount } from '../lib/api';
import { useDomainLabels } from '../lib/domainLabels';
import { localizedOpsErrorMessage } from '../lib/errorMessages';
import { useOpsFormatter } from '../lib/formatting';

type TenantStatusFilter = '' | 'Active' | 'Suspended';

export function TenantsPage({ canWrite = false }: { canWrite?: boolean }) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useOpsFormatter();
  const queryClient = useQueryClient();
  const tenants = useQuery({ queryKey: ['ops-tenants'], queryFn: api.tenants });
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<TenantStatusFilter>('');
  const [statusChange, setStatusChange] = useState<{ tenant: TenantAccount; status: 'Active' | 'Suspended' } | null>(null);
  const update = useMutation({
    mutationFn: ({ id, status }: { id: string; status: 'Active' | 'Suspended' }) => api.setTenantStatus(id, status),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['ops-tenants'] }),
    onSettled: () => setStatusChange(null)
  });

  const rows = useMemo(() => tenants.data ?? [], [tenants.data]);
  const activeTenants = rows.filter(tenant => tenant.status === 'Active').length;
  const suspendedTenants = rows.length - activeTenants;
  const totalRunners = rows.reduce((total, tenant) => total + tenant.runnerCount, 0);
  const totalEmployees = rows.reduce((total, tenant) => total + tenant.employees, 0);
  const filteredTenants = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return rows.filter(tenant => {
      const matchesSearch = !query || tenant.name.toLocaleLowerCase().includes(query) || tenant.id.toLocaleLowerCase().includes(query);
      const matchesStatus = !statusFilter || tenant.status === statusFilter;
      return matchesSearch && matchesStatus;
    });
  }, [rows, search, statusFilter]);
  const statusOptions = [
    { value: '', label: t('tenantsPage.status') },
    { value: 'Active', label: domainLabel('Active') },
    { value: 'Suspended', label: domainLabel('Suspended') }
  ] as const;

  if (tenants.isLoading) return <LocalizedLoadingState />;
  if (tenants.error && !tenants.data) {
    return (
      <LocalizedErrorState
        message={localizedOpsErrorMessage(tenants.error, t)}
        onRetry={() => void tenants.refetch()}
      />
    );
  }

  const confirmTitle = statusChange?.status === 'Suspended' ? t('tenantsPage.suspend') : t('tenantsPage.activate');
  const confirmDescription = statusChange?.status === 'Suspended'
    ? t('tenantsPage.confirmSuspend', { name: statusChange.tenant.name })
    : statusChange ? `${statusChange.tenant.name} · ${t('tenantsPage.description')}` : undefined;

  return (
    <Stack spacing={2.5}>
      <PageHeader title={t('tenantsPage.title')} description={t('tenantsPage.description')} />
      {!canWrite ? <Text variant="caption">{t('tenantsPage.readOnly')}</Text> : null}
      {tenants.error ? (
        <LocalizedErrorState
          inline
          stale
          message={localizedOpsErrorMessage(tenants.error, t)}
          onRetry={() => void tenants.refetch()}
        />
      ) : null}
      {update.isSuccess ? <StatusChip label={t('tenantsPage.updated')} tone="success" /> : null}
      {update.error ? <LocalizedErrorState inline message={localizedOpsErrorMessage(update.error, t)} /> : null}

      <Grid columns={4} spacing={1.5}>
        <MetricCard label={t('tenantsPage.cardTitle')} value={format.number(rows.length)} icon="tenants" hint={`${t('tenantsPage.employees')} · ${format.number(totalEmployees)}`} />
        <MetricCard label={domainLabel('Active')} value={format.number(activeTenants)} icon="success" tone="success" />
        <MetricCard label={domainLabel('Suspended')} value={format.number(suspendedTenants)} icon="warning" tone="warning" />
        <MetricCard label={t('tenantsPage.runners')} value={format.number(totalRunners)} icon="agent" />
      </Grid>

      <FilterBar
        action={<Text variant="caption">{format.number(filteredTenants.length)} / {format.number(rows.length)}</Text>}
      >
        <TextInput
          value={search}
          onChange={setSearch}
          placeholder={t('tenantsPage.tenant')}
          startAdornment={<Icon name="search" />}
          inputProps={{ 'aria-label': t('tenantsPage.tenant') }}
          sx={{ width: { xs: '100%', sm: 280 } }}
        />
        <Select
          label={t('tenantsPage.status')}
          value={statusFilter}
          options={statusOptions}
          onChange={setStatusFilter}
        />
      </FilterBar>

      {!rows.length ? (
        <Card padding="small">
          <EmptyState title={t('tenantsPage.noTenantsTitle')} description={t('tenantsPage.noTenantsDescription')} compact />
        </Card>
      ) : (
        <Card title={t('tenantsPage.cardTitle')} padding="small">
          <DataTable
            rows={filteredTenants}
            getRowId={row => row.id}
            density="compact"
            ariaLabel={t('tenantsPage.cardTitle')}
            emptyTitle={t('common.noRecords')}
            columns={[
              {
                id: 'name',
                header: t('tenantsPage.tenant'),
                render: row => <Stack spacing={0.25}><Text variant="bodyStrong">{row.name}</Text><Text variant="caption" color="text.secondary">{row.id}</Text></Stack>
              },
              {
                id: 'status',
                header: t('tenantsPage.status'),
                render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Active' ? 'success' : 'warning'} />
              },
              { id: 'employees', header: t('tenantsPage.employees'), numeric: true, render: row => format.number(row.employees) },
              { id: 'runners', header: t('tenantsPage.runners'), numeric: true, render: row => format.number(row.runnerCount) },
              ...(canWrite ? [{
                id: 'actions',
                header: t('tenantsPage.actions'),
                align: 'right' as const,
                render: (row: TenantAccount) => {
                  const suspending = row.status === 'Active';
                  return (
                    <Button
                      tone={suspending ? 'danger' : 'secondary'}
                      size="small"
                      disabled={update.isPending}
                      loading={update.isPending && update.variables?.id === row.id}
                      onClick={() => setStatusChange({ tenant: row, status: suspending ? 'Suspended' : 'Active' })}
                    >
                      {suspending ? t('tenantsPage.suspend') : t('tenantsPage.activate')}
                    </Button>
                  );
                }
              }] : [])
            ]}
          />
        </Card>
      )}

      <ConfirmDialog
        open={Boolean(statusChange)}
        title={confirmTitle}
        description={confirmDescription}
        confirmLabel={confirmTitle}
        cancelLabel={t('common.domain.off')}
        tone={statusChange?.status === 'Suspended' ? 'danger' : 'primary'}
        loading={update.isPending}
        onClose={() => setStatusChange(null)}
        onConfirm={() => {
          if (statusChange) update.mutate({ id: statusChange.tenant.id, status: statusChange.status });
        }}
      />
    </Stack>
  );
}
