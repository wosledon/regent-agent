import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Card, DataTable, FilterBar, PageHeader, Stack, Text, TextInput } from '@regent/ui-shared';
import { LocalizedErrorState, LocalizedLoadingState } from '../components/LocalizedStates';
import { api } from '../lib/api';
import { localizedOpsErrorMessage } from '../lib/errorMessages';
import { useOpsFormatter } from '../lib/formatting';

export function AuditPage() {
  const { t } = useTranslation();
  const format = useOpsFormatter();
  const { data, isLoading, error } = useQuery({ queryKey: ['ops-audit'], queryFn: api.audit });
  const [operatorFilter, setOperatorFilter] = useState('');
  const [actionFilter, setActionFilter] = useState('');
  const [targetFilter, setTargetFilter] = useState('');
  const hasFilters = Boolean(operatorFilter.trim() || actionFilter.trim() || targetFilter.trim());
  const rows = useMemo(() => {
    const operator = operatorFilter.trim().toLowerCase();
    const action = actionFilter.trim().toLowerCase();
    const target = targetFilter.trim().toLowerCase();
    return (data ?? []).filter(row =>
      (!operator || row.operatorId.toLowerCase().includes(operator))
      && (!action || row.action.toLowerCase().includes(action))
      && (!target || row.target.toLowerCase().includes(target)));
  }, [data, operatorFilter, actionFilter, targetFilter]);

  return <Stack spacing={2.5}>
    <PageHeader title={t('auditPage.title')} description={t('auditPage.description')} />
    <Card title={t('auditPage.cardTitle')}>
      <FilterBar>
        <TextInput value={operatorFilter} onChange={setOperatorFilter} placeholder={t('auditPage.operator')}
          inputProps={{ 'aria-label': t('auditPage.operator') }} sx={{ width: { xs: '100%', sm: 180 } }} />
        <TextInput value={actionFilter} onChange={setActionFilter} placeholder={t('auditPage.action')}
          inputProps={{ 'aria-label': t('auditPage.action') }} sx={{ width: { xs: '100%', sm: 220 } }} />
        <TextInput value={targetFilter} onChange={setTargetFilter} placeholder={t('auditPage.target')}
          inputProps={{ 'aria-label': t('auditPage.target') }} sx={{ width: { xs: '100%', sm: 220 } }} />
      </FilterBar>
      {isLoading ? <LocalizedLoadingState /> : null}
      {error ? <LocalizedErrorState message={localizedOpsErrorMessage(error, t)} /> : null}
      {!isLoading && !error ? <Stack spacing={1}>
        <Text variant="caption">{format.number(rows.length)}</Text>
        <DataTable rows={rows} getRowId={row => row.id} density="compact" minWidth={760}
          emptyTitle={hasFilters ? t('common.noRecords') : t('auditPage.noOperationsTitle')}
          emptyDescription={hasFilters ? undefined : t('auditPage.noOperationsDescription')}
          columns={[
            { id: 'time', header: t('auditPage.time'), render: row => <Text variant="caption">{format.dateTime(row.createdAt)}</Text> },
            { id: 'operator', header: t('auditPage.operator'), render: row => <Text variant="mono">{row.operatorId}</Text> },
            { id: 'action', header: t('auditPage.action'), render: row => <Text variant="bodyStrong">{row.action}</Text> },
            { id: 'target', header: t('auditPage.target'), render: row => row.target }
          ]} />
      </Stack> : null}
    </Card>
  </Stack>;
}
