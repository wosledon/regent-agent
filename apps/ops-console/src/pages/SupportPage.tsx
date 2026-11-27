import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Button, Card, ConfirmDialog, DataTable, DetailList, EmptyState, FormField, Grid, Icon,
  MetricCard, PageHeader, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { LocalizedErrorState, LocalizedLoadingState } from '../components/LocalizedStates';
import { api, type SupportGrant, type TenantAccount } from '../lib/api';
import { useDomainLabels } from '../lib/domainLabels';
import { localizedOpsErrorMessage } from '../lib/errorMessages';
import { useOpsFormatter } from '../lib/formatting';
import { localizedCount } from '../lib/localizedCount';

type SupportConfirmation = { type: 'approve' | 'revoke'; grant: SupportGrant } | null;

function tenantName(grant: SupportGrant, tenants: readonly TenantAccount[]) {
  return tenants.find(tenant => tenant.id === grant.tenantId)?.name ?? grant.tenantId;
}

function isExpired(grant: SupportGrant) {
  const expiry = Date.parse(grant.expiresAt);
  return Number.isFinite(expiry) && expiry <= Date.now();
}

export function SupportPage({ canWrite = false }: { canWrite?: boolean }) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useOpsFormatter();
  const queryClient = useQueryClient();
  const grants = useQuery({ queryKey: ['ops-grants'], queryFn: api.grants });
  const tenants = useQuery({ queryKey: ['ops-tenants'], queryFn: api.tenants });
  const [tenantId, setTenantId] = useState('');
  const [approvalRef, setApprovalRef] = useState('');
  const [confirmation, setConfirmation] = useState<SupportConfirmation>(null);
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['ops-grants'] });
  const create = useMutation({
    mutationFn: (id: string) => api.createGrant(id, approvalRef.trim()),
    onSuccess: () => { setTenantId(''); setApprovalRef(''); return invalidate(); }
  });
  const approve = useMutation({
    mutationFn: (id: string) => api.approveGrant(id),
    onSuccess: invalidate,
    onSettled: () => setConfirmation(null)
  });
  const revoke = useMutation({
    mutationFn: (id: string) => api.revokeGrant(id),
    onSuccess: invalidate,
    onSettled: () => setConfirmation(null)
  });

  const grantRows = useMemo(() => grants.data ?? [], [grants.data]);
  const tenantRows = tenants.data ?? [];
  const pendingGrants = useMemo(
    () => grantRows.filter(grant => grant.status === 'Pending').sort((left, right) => left.expiresAt.localeCompare(right.expiresAt)),
    [grantRows]
  );
  const otherGrants = useMemo(
    () => grantRows.filter(grant => grant.status !== 'Pending').sort((left, right) => {
      const statusOrder = Number(right.status === 'Active') - Number(left.status === 'Active');
      return statusOrder || left.expiresAt.localeCompare(right.expiresAt);
    }),
    [grantRows]
  );
  const activeGrants = grantRows.filter(grant => grant.status === 'Active').length;

  if (grants.isLoading) return <LocalizedLoadingState />;
  if (grants.error && !grants.data) {
    return (
      <LocalizedErrorState
        message={localizedOpsErrorMessage(grants.error, t)}
        onRetry={() => void grants.refetch()}
      />
    );
  }

  const mutationError = create.error ?? approve.error ?? revoke.error;
  const successMessage = revoke.isSuccess ? t('supportPage.revoked')
    : approve.isSuccess ? t('supportPage.approved')
      : create.isSuccess ? t('supportPage.requested') : null;
  const confirmationGrant = confirmation?.grant;
  const confirmationTenant = confirmationGrant ? tenantName(confirmationGrant, tenantRows) : '';
  const confirmTitle = confirmation?.type === 'revoke' ? t('supportPage.revoke') : t('supportPage.approve');
  const confirmDescription = confirmation?.type === 'approve' && confirmationGrant
    ? `${t('supportPage.confirmApprove', {
      scope: domainLabel(confirmationGrant.scope),
      tenant: confirmationTenant,
      expires: format.dateTime(confirmationGrant.expiresAt)
    })}\n${t('supportPage.approvalRefLabel')}: ${confirmationGrant.approvalRef ?? t('common.notProvided')}`
    : confirmation?.type === 'revoke'
      ? t('supportPage.confirmRevoke', { tenant: confirmationTenant })
      : undefined;

  return (
    <Stack spacing={2.5}>
      <PageHeader title={t('supportPage.title')} description={t('supportPage.description')} />
      {!canWrite ? <Text variant="caption">{t('supportPage.readOnly')}</Text> : null}
      {successMessage ? <StatusChip label={successMessage} tone="success" /> : null}
      {mutationError ? <LocalizedErrorState inline message={localizedOpsErrorMessage(mutationError, t)} /> : null}
      {grants.error ? (
        <LocalizedErrorState
          inline
          stale
          message={localizedOpsErrorMessage(grants.error, t)}
          onRetry={() => void grants.refetch()}
        />
      ) : null}

      <Grid columns={3} spacing={1.5}>
        <MetricCard
          label={t('overviewPage.pendingApprovalsTitle')}
          value={localizedCount(t, format, 'overviewPage.pendingApprovals', pendingGrants.length)}
          icon="approvals"
          tone={pendingGrants.length ? 'warning' : 'success'}
          hint={t('supportPage.auditHint')}
        />
        <MetricCard
          label={t('overviewPage.activeGrantsTitle')}
          value={localizedCount(t, format, 'overviewPage.activeGrants', activeGrants)}
          icon="support"
          tone="success"
          hint={t('supportPage.auditHint')}
        />
        <MetricCard
          label={t('supportPage.accessRequests')}
          value={localizedCount(t, format, 'supportPage.grantCount', grantRows.length)}
          icon="journal"
          hint={t('supportPage.description')}
        />
      </Grid>

      {pendingGrants.length ? (
        <Card title={t('overviewPage.pendingApprovalsTitle')} description={t('supportPage.description')} padding="small">
          <DataTable
            rows={pendingGrants}
            getRowId={row => row.id}
            density="compact"
            ariaLabel={t('overviewPage.pendingApprovalsTitle')}
            emptyTitle={t('common.noRecords')}
            columns={[
              {
                id: 'tenant',
                header: t('supportPage.tenant'),
                render: row => <Stack spacing={0.25}><Text variant="bodyStrong">{tenantName(row, tenantRows)}</Text><Text variant="caption" color="text.secondary">{row.operatorId}</Text></Stack>
              },
              { id: 'scope', header: t('supportPage.scope'), render: row => domainLabel(row.scope) },
              {
                id: 'status',
                header: t('supportPage.status'),
                render: row => <StatusChip label={domainLabel(row.status)} tone="warning" />
              },
              {
                id: 'expires',
                header: t('supportPage.expires'),
                render: row => <Stack spacing={0.5}><Text>{format.dateTime(row.expiresAt)}</Text>{isExpired(row) ? <StatusChip label={t('common.domain.stale')} tone="warning" /> : null}</Stack>
              },
              {
                id: 'evidence',
                header: t('supportPage.approvalRefLabel'),
                render: row => <Text variant="mono" color={row.approvalRef ? 'text.secondary' : 'error'}>{row.approvalRef ?? t('common.notProvided')}</Text>
              },
              ...(canWrite ? [{
                id: 'actions',
                header: t('supportPage.actions'),
                align: 'right' as const,
                render: (row: SupportGrant) => (
                  <Button
                    size="small"
                    disabled={approve.isPending || revoke.isPending || !row.approvalRef}
                    loading={approve.isPending && approve.variables === row.id}
                    onClick={() => setConfirmation({ type: 'approve', grant: row })}
                  >
                    {t('supportPage.approve')}
                  </Button>
                )
              }] : [])
            ]}
          />
        </Card>
      ) : null}

      {!grantRows.length ? (
        <Card padding="small">
          <EmptyState title={t('supportPage.noGrantsTitle')} description={t('supportPage.noGrantsDescription')} compact />
        </Card>
      ) : null}

      {canWrite || otherGrants.length ? (
        <Grid columns={canWrite && otherGrants.length ? 2 : 1} spacing={1.5}>
          {canWrite ? (
            <Card title={t('supportPage.requestTitle')} padding="small" sx={{ height: '100%' }}>
              {tenants.error ? (
                <LocalizedErrorState
                  inline
                  stale={Boolean(tenants.data)}
                  message={localizedOpsErrorMessage(tenants.error, t)}
                  onRetry={() => void tenants.refetch()}
                />
              ) : null}
              {tenants.isLoading ? <LocalizedLoadingState inline /> : null}
              {!tenants.isLoading && !tenants.error && !tenantRows.length ? (
                <EmptyState title={t('supportPage.noTenantsTitle')} description={t('supportPage.noTenantsDescription')} compact />
              ) : null}
              {tenantRows.length ? (
                <Stack spacing={2}>
                  <FormField label={t('supportPage.tenantLabel')} hint={t('supportPage.tenantHint')}>
                    <Select
                      label={t('supportPage.tenantLabel')}
                      value={tenantId}
                      onChange={setTenantId}
                      disabled={create.isPending}
                      options={[
                        { value: '', label: t('supportPage.selectTenant') },
                        ...tenantRows.map(tenant => ({ value: tenant.id, label: tenant.name }))
                      ]}
                    />
                  </FormField>
                  <FormField label={t('supportPage.approvalRefLabel')} hint={t('supportPage.approvalRefHint')}>
                    <TextInput
                      value={approvalRef}
                      onChange={setApprovalRef}
                      disabled={create.isPending}
                      inputProps={{ 'aria-label': t('supportPage.approvalRefAria') }}
                    />
                  </FormField>
                  <DetailList
                    dense
                    columns={2}
                    items={[
                      { label: t('supportPage.scope'), value: domainLabel('ReadOnly') },
                      { label: t('supportPage.expires'), value: format.duration(30, 'minute') }
                    ]}
                  />
                  <Button
                    startIcon={<Icon name="plus" />}
                    onClick={() => create.mutate(tenantId)}
                    disabled={!tenantId || !approvalRef.trim()}
                    loading={create.isPending}
                  >
                    {t('supportPage.requestButton')}
                  </Button>
                </Stack>
              ) : null}
            </Card>
          ) : null}

          {otherGrants.length ? (
            <Card title={t('supportPage.accessRequests')} padding="small" sx={{ height: '100%' }}>
              <DataTable
                rows={otherGrants}
                getRowId={row => row.id}
                density="compact"
                ariaLabel={t('supportPage.accessRequests')}
                emptyTitle={t('common.noRecords')}
                columns={[
                  {
                    id: 'tenant',
                    header: t('supportPage.tenant'),
                    render: row => <Stack spacing={0.25}><Text variant="bodyStrong">{tenantName(row, tenantRows)}</Text><Text variant="caption" color="text.secondary">{row.operatorId}</Text></Stack>
                  },
                  { id: 'scope', header: t('supportPage.scope'), render: row => domainLabel(row.scope) },
                  {
                    id: 'status',
                    header: t('supportPage.status'),
                    render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Active' ? 'success' : 'warning'} />
                  },
                  { id: 'expires', header: t('supportPage.expires'), render: row => format.dateTime(row.expiresAt) },
                  {
                    id: 'evidence',
                    header: t('supportPage.approvalRefLabel'),
                    render: row => <Text variant="mono" color={row.approvalRef ? 'text.secondary' : 'error'}>{row.approvalRef ?? t('common.notProvided')}</Text>
                  },
                  ...(canWrite ? [{
                    id: 'actions',
                    header: t('supportPage.actions'),
                    align: 'right' as const,
                    render: (row: SupportGrant) => row.status === 'Active' ? (
                      <Button
                        tone="danger"
                        size="small"
                        disabled={approve.isPending || revoke.isPending}
                        loading={revoke.isPending && revoke.variables === row.id}
                        onClick={() => setConfirmation({ type: 'revoke', grant: row })}
                      >
                        {t('supportPage.revoke')}
                      </Button>
                    ) : '—'
                  }] : [])
                ]}
              />
              <Text variant="caption">{t('supportPage.auditHint')}</Text>
            </Card>
          ) : null}
        </Grid>
      ) : null}

      <ConfirmDialog
        open={Boolean(confirmation)}
        title={confirmTitle}
        description={<Text component="span" sx={{ whiteSpace: 'pre-line' }}>{confirmDescription}</Text>}
        confirmLabel={confirmTitle}
        cancelLabel={t('common.domain.off')}
        tone={confirmation?.type === 'revoke' ? 'danger' : 'primary'}
        loading={confirmation?.type === 'revoke' ? revoke.isPending : approve.isPending}
        onClose={() => setConfirmation(null)}
        onConfirm={() => {
          if (!confirmationGrant) return;
          if (confirmation?.type === 'approve') approve.mutate(confirmationGrant.id);
          if (confirmation?.type === 'revoke') revoke.mutate(confirmationGrant.id);
        }}
      />
    </Stack>
  );
}
