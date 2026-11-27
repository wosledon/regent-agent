import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Button, Card, DetailList, EmptyState, FormField, Grid, SectionHeader, Select, Stack, StatusChip, TextInput } from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';

interface MemoryImpactPanelProps {
  claimId: string;
  onChanged: () => void | Promise<void>;
}

export function MemoryImpactPanel({ claimId, onChanged }: MemoryImpactPanelProps) {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const [targetType, setTargetType] = useState('Task');
  const [targetId, setTargetId] = useState('');
  const [risk, setRisk] = useState('');
  const [requiredAction, setRequiredAction] = useState('');
  const impacts = useQuery({
    queryKey: ['memory-impact', claimId],
    queryFn: () => api.memoryImpact(claimId),
    enabled: Boolean(claimId)
  });
  const record = useMutation({
    mutationFn: () => api.recordMemoryImpact(claimId, { targetType, targetId: targetId.trim(), risk: risk.trim(), requiredAction: requiredAction.trim() }),
    onSuccess: async () => { setTargetId(''); await onChanged(); }
  });
  const invalid = !targetId.trim() || !risk.trim() || !requiredAction.trim();

  if (!claimId) return (
    <Stack spacing={2}>
      <SectionHeader title={t('memoryPage.impactGraph')} />
      <Card padding="small"><EmptyState title={t('memoryPage.selectARecalledClaim')} description={t('memoryPage.chooseASearchHitToInspectWhereTheClaimWasUsed')} compact /></Card>
    </Stack>
  );
  if (impacts.isLoading) return <LocalizedLoadingState />;
  if (impacts.error) return <LocalizedErrorState error={impacts.error} />;

  return (
    <Stack spacing={2}>
      <SectionHeader title={t('memoryPage.impactGraph')} action={<StatusChip label={claimId} tone="info" />} />
      <Card padding="small">
        <Stack spacing={2}>
          <DetailList dense columns={2} items={[
            { label: t('memoryPage.claimID'), value: claimId, mono: true },
            { label: t('common.version'), value: impacts.data?.[0] ? format.number(toFiniteNumber(impacts.data[0].claimVersion)) : t('common.notAvailable') }
          ]} />
          <Grid columns={2}>
            <Select label={t('memoryPage.targetType')} value={targetType} onChange={setTargetType} options={[
              { value: 'Task', label: t('common.task') },
              { value: 'Summary', label: t('common.summary') },
              { value: 'Decision', label: t('common.decision') },
              { value: 'Review', label: t('common.review') },
              { value: 'Release', label: t('common.release') },
              { value: 'ExternalAction', label: t('memoryPage.externalAction') },
              { value: 'Context', label: t('memoryPage.context') }
            ]} />
            <FormField label={t('memoryPage.targetID')}><TextInput value={targetId} onChange={setTargetId} placeholder={t('memoryPage.task123')} /></FormField>
            <FormField label={t('memoryPage.risk')}><TextInput value={risk} onChange={setRisk} /></FormField>
            <FormField label={t('memoryPage.requiredAction')}><TextInput value={requiredAction} onChange={setRequiredAction} /></FormField>
          </Grid>
          <Stack direction="row" spacing={1.5} wrap alignItems="center">
            <Button tone="secondary" size="small" disabled={invalid || record.isPending} loading={record.isPending} onClick={() => record.mutate()}>{t('memoryPage.recordImpact')}</Button>
            <LocalizedMutationFeedback mutation={record} successMessage={t('memoryPage.memoryImpactRecorded')} />
          </Stack>
          <LocalizedDataTable
            rows={impacts.data ?? []}
            getRowId={row => row.id}
            density="compact"
            emptyTitle={t('memoryPage.noRecordedImpact')}
            columns={[
              { id: 'target', header: t('memoryPage.target'), render: row => t('memoryPage.targetValue', { type: domainLabel(row.targetType), id: row.targetId }) },
              { id: 'version', header: t('common.version'), render: row => format.number(toFiniteNumber(row.claimVersion)) },
              { id: 'risk', header: t('memoryPage.risk'), render: row => row.risk },
              { id: 'action', header: t('memoryPage.requiredAction'), render: row => row.requiredAction },
              { id: 'used', header: t('memoryPage.usedAt'), render: row => format.dateTime(row.usedAt) }
            ]}
          />
        </Stack>
      </Card>
    </Stack>
  );
}
