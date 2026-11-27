import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, Grid, MetricCard, SemanticLink, Stack, StatusChip, type ChipTone
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';
import type { components } from '../../lib/api-client/schema';

type DataExport = components['schemas']['DataExportDto'];

function exportStatus(row: DataExport, domainLabel: (value: string) => string): { label: string; tone: ChipTone } {
  if (row.validationError?.trim()) return { label: domainLabel('Invalid'), tone: 'error' };
  if (row.validatedAt) return { label: domainLabel('Validated'), tone: 'success' };
  return { label: domainLabel(row.status), tone: 'info' };
}

export function DataExportPanel({ canManage }: { canManage: boolean }) {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const exports = useQuery({ queryKey: ['tenant-exports'], queryFn: api.exports, enabled: canManage, retry: false });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['tenant-exports'] });
  const create = useMutation({ mutationFn: () => api.createExport(), onSuccess: invalidate });
  const validate = useMutation({ mutationFn: (id: string) => api.validateExport(id), onSuccess: invalidate });
  if (!canManage) return null;
  const rows = exports.data ?? [];
  const tableCount = rows.reduce((sum, row) => sum + (toFiniteNumber(row.tableCount) ?? 0), 0);
  const moduleCount = rows.reduce((sum, row) => sum + (toFiniteNumber(row.moduleCount) ?? 0), 0);
  const validatedCount = rows.filter(row => row.validatedAt && !row.validationError?.trim()).length;

  return (
    <Card
      title={t('billingPage.tenantDataExportAndMigrationPackage')}
      description={t('billingPage.createAChecksummedZIPContainingTenantDatabaseRowsAndDomainModuleStateCre')}
      padding="small"
      action={
        <Button size="small" onClick={() => create.mutate()} loading={create.isPending}>
          {t('billingPage.createExportPackage')}
        </Button>
      }
    >
      <LocalizedMutationFeedback mutation={create} successMessage={t('billingPage.exportPackageCreated')} />
      {exports.isLoading ? <LocalizedLoadingState /> : null}
      {exports.error ? <LocalizedErrorState error={exports.error} /> : null}
      <Grid columns={3}>
        <MetricCard label={t('billingPage.tables')} value={format.number(tableCount)} icon="database" />
        <MetricCard label={t('billingPage.modules')} value={format.number(moduleCount)} icon="components" />
        <MetricCard label={t('common.status')} value={format.number(validatedCount)} icon="success" />
      </Grid>
      <LocalizedDataTable
        rows={rows}
        getRowId={row => row.id}
        density="compact"
        columns={[
          { id: 'created', header: t('common.created'), render: row => format.dateTime(row.createdAt) },
          { id: 'tables', header: t('billingPage.tables'), render: row => format.number(toFiniteNumber(row.tableCount)), numeric: true },
          { id: 'modules', header: t('billingPage.modules'), render: row => format.number(toFiniteNumber(row.moduleCount)), numeric: true },
          { id: 'status', header: t('common.status'), render: row => {
            const status = exportStatus(row, domainLabel);
            return <StatusChip label={status.label} tone={status.tone} />;
          } },
          { id: 'actions', header: t('common.actions'), render: row => (
            <Stack direction="row" spacing={1} wrap alignItems="center">
              <SemanticLink href={`/v1/exports/${row.id}/content`}>{t('billingPage.download')}</SemanticLink>
              <Button
                size="small"
                tone="secondary"
                onClick={() => validate.mutate(row.id)}
                loading={validate.isPending && validate.variables === row.id}
              >
                {t('billingPage.validate')}
              </Button>
            </Stack>
          ) }
        ]}
      />
      <LocalizedMutationFeedback
        mutation={validate}
        showSuccess={validate.isSuccess && validate.data.valid === true}
        successMessage={t('billingPage.exportPackageValidated')}
      />
      {validate.isSuccess && validate.data.valid !== true
        ? <LocalizedErrorState error={t('billingPage.exportPackageInvalid')} />
        : null}
    </Card>
  );
}
