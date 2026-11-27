import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ContentContainer, DetailList, EmptyState, FormField, PageHeader,
  Select, Stack, StatusChip, TextInput
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import type { ComplianceProfile } from '../../lib/domain-types';

export function CompliancePage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['compliance'], queryFn: api.compliance });
  const [draft, setDraft] = useState<ComplianceProfile | null>(null);
  const profile = draft ?? data;
  const save = useMutation({
    mutationFn: () => api.updateCompliance(profile!),
    onSuccess: async () => {
      setDraft(null);
      await queryClient.invalidateQueries({ queryKey: ['compliance'] });
    }
  });
  if (isLoading) return <LocalizedLoadingState />;
  if (error) return <LocalizedErrorState error={error} />;
  if (!profile) return <EmptyState title={t('governancePage.noComplianceProfile')} description={t('common.notAvailable')} />;
  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader title={t('governancePage.compliance')} />
        <Stack direction="row" spacing={2} sx={{ flexWrap: 'wrap', alignItems: 'flex-start' }}>
          <Stack sx={{ flex: '1 1 620px', minWidth: 0 }}>
            <Card
              title={t('governancePage.policy')}
              action={<StatusChip label={domainLabel(profile.status)} tone="success" />}
              padding="small"
              footer={<Button onClick={() => save.mutate()} loading={save.isPending}>{t('governancePage.savePolicy')}</Button>}
            >
              <Select label={t('governancePage.preset')} value={profile.preset} options={[{ value: 'baseline', label: t('governancePage.baseline') }, { value: 'strict', label: t('governancePage.strict') }, { value: 'custom', label: t('governancePage.custom') }]} onChange={preset => setDraft({ ...profile, preset })} />
              <Select label={t('governancePage.auditProfile')} value={profile.auditProfile} options={[{ value: 'standard', label: t('governancePage.standard') }, { value: 'worm', label: t('governancePage.wORM') }, { value: 'anchored', label: t('governancePage.anchored') }]} onChange={auditProfile => setDraft({ ...profile, auditProfile })} />
              <FormField label={t('governancePage.onlineRetentionDays')}><TextInput value={String(profile.onlineRetentionDays)} onChange={value => setDraft({ ...profile, onlineRetentionDays: Number(value) || 0 })} inputProps={{ inputMode: 'numeric' }} /></FormField>
              <LocalizedMutationFeedback mutation={save} successMessage={t('governancePage.compliancePolicySaved')} />
            </Card>
          </Stack>
          <Stack sx={{ flex: '0 1 360px', minWidth: 280 }}>
            <Card title={t('common.evidence')} padding="small">
              <DetailList dense items={[
                { label: t('governancePage.auditProfile'), value: domainLabel(profile.auditProfile) },
                { label: t('governancePage.onlineRetentionDays'), value: profile.onlineRetentionDays },
                { label: t('common.provider'), value: t('governancePage.providers', { value0: profile.allowedProviders.join(', ') }) },
                { label: t('common.scope'), value: t('governancePage.regions', { value0: profile.allowedRegions.join(', ') }) }
              ]} />
            </Card>
          </Stack>
        </Stack>
      </Stack>
    </ContentContainer>
  );
}
