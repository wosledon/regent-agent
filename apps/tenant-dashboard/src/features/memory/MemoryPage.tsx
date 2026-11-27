import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ConfirmDialog, ContentContainer, DetailList, EmptyState, FilterBar,
  FormField, FreshnessBadge, Grid, Icon, MetricCard, PageHeader, SectionHeader, Select,
  Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';
import { MemoryCaptureForm } from './MemoryCaptureForm';
import { MemoryImpactPanel } from './MemoryImpactPanel';
import { MemoryReviewQueue } from './MemoryReviewQueue';
import { MemorySafetyPanel } from './MemorySafetyPanel';

export function MemoryPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const [query, setQuery] = useState(() => t('memoryPage.defaultQuery'));
  const [scope, setScope] = useState('organization');
  const [selectedClaimId, setSelectedClaimId] = useState('');
  const [nominationScope, setNominationScope] = useState('');
  const [pendingDeleteClaimId, setPendingDeleteClaimId] = useState<string | null>(null);
  const search = useQuery({
    queryKey: ['memory-search', query, scope],
    queryFn: () => api.searchMemories(query, scope || undefined),
    enabled: query.trim().length > 0
  });
  const rebuild = useMutation({ mutationFn: () => api.rebuildMemoryIndex(), onSuccess: invalidate });
  const forget = useMutation({ mutationFn: (id: string) => api.forgetMemory(id), onSuccess: invalidate });
  const purge = useMutation({
    mutationFn: (id: string) => api.deleteMemory(id),
    onSuccess: async () => {
      setPendingDeleteClaimId(null);
      await invalidate();
    }
  });
  const archive = useMutation({ mutationFn: (id: string) => api.archiveMemory(id), onSuccess: invalidate });
  const nominate = useMutation({ mutationFn: ({ id, scope }: { id: string; scope: string }) => api.nominateMemory(id, scope), onSuccess: invalidate });

  async function invalidate() {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['memory-search'] }),
      queryClient.invalidateQueries({ queryKey: ['memory-findings'] }),
      queryClient.invalidateQueries({ queryKey: ['memory-corrections'] }),
      queryClient.invalidateQueries({ queryKey: ['memory-impact'] }),
      queryClient.invalidateQueries({ queryKey: ['memory-review-queue'] })
    ]);
  }

  const hits = search.data?.hits ?? [];
  const reviewCount = hits.filter(hit => hit.reviewRequired || hit.conflictWithIds.length > 0).length;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('memoryPage.knowledgeMemory')}
          description={t('memoryPage.hybridRecallPoisoningDetectionIndependentCorrectionAndImpactTracing')}
          meta={<FreshnessBadge freshness="unknown" label={t('common.freshness.unknown')} />}
          actions={<Button tone="secondary" size="small" onClick={() => rebuild.mutate()} disabled={rebuild.isPending} loading={rebuild.isPending}>{t('memoryPage.rebuildIndex')}</Button>}
        />
        <LocalizedMutationFeedback mutation={rebuild} successMessage={t('memoryPage.memoryIndexRebuilt')} />
        <LocalizedMutationFeedback mutation={nominate} successMessage={t('memoryPage.memoryNominatedForPublicationReview')} />
        <LocalizedMutationFeedback mutation={archive} successMessage={t('memoryPage.memoryClaimArchived')} />
        <LocalizedMutationFeedback mutation={forget} successMessage={t('memoryPage.memoryClaimForgotten')} />
        <LocalizedMutationFeedback mutation={purge} successMessage={t('memoryPage.memoryClaimPermanentlyDeleted')} />
        <Grid columns={4}>
          <MetricCard label={t('memoryPage.hybridRecall')} value={search.data ? format.number(hits.length) : t('common.notAvailable')} hint={t('memoryPage.searchQuery')} icon="search" />
          <MetricCard label={t('common.review')} value={search.data ? format.number(reviewCount) : t('common.notAvailable')} tone={reviewCount > 0 ? 'warning' : 'default'} icon="approval" />
          <MetricCard label={t('memoryPage.claim')} value={format.number(selectedClaimId ? 1 : 0)} hint={selectedClaimId || t('memoryPage.selectARecalledClaim')} icon="knowledge" />
          <MetricCard label={t('common.version')} value={search.data ? format.number(toFiniteNumber(search.data.indexVersion)) : t('common.notAvailable')} hint={t('memoryPage.hybridRecall')} icon="database" />
        </Grid>
        <Stack direction="row" wrap spacing={2} alignItems="flex-start">
          <Stack sx={{ flex: '1 1 320px', minWidth: 0 }}>
            <MemoryCaptureForm onCaptured={invalidate} />
          </Stack>
          <Stack spacing={2} sx={{ flex: '2 1 620px', minWidth: 0 }}>
            <SectionHeader title={t('memoryPage.hybridRecall')} />
            <Card padding="small">
              <Stack spacing={2}>
                <FilterBar>
                  <Stack sx={{ flex: '1 1 240px' }}>
                    <FormField label={t('memoryPage.searchQuery')}>
                      <TextInput value={query} onChange={setQuery} placeholder={t('memoryPage.searchMemory')} startAdornment={<Icon name="search" />} />
                    </FormField>
                  </Stack>
                  <Stack sx={{ flex: '0 1 190px' }}>
                    <Select label={t('common.scope')} value={scope} onChange={setScope} options={[
                      { value: '', label: t('memoryPage.allScopes') },
                      { value: 'personal', label: domainLabel('personal') },
                      { value: 'organization', label: domainLabel('organization') },
                      { value: 'knowledge_base', label: domainLabel('knowledge_base') }
                    ]} />
                  </Stack>
                </FilterBar>
                <FormField label={t('memoryPage.nominationScopePositionID')}>
                  <TextInput value={nominationScope} onChange={setNominationScope} placeholder={t('memoryPage.positionIDForUpwardNomination')} />
                </FormField>
                {search.isLoading ? <LocalizedLoadingState /> : null}
                {search.error ? <LocalizedErrorState error={search.error} /> : null}
                {search.data?.hits.length === 0 ? <EmptyState title={t('memoryPage.noMatchingMemory')} description={t('memoryPage.quarantinedAndForgottenClaimsAreExcludedFromRecall')} compact /> : null}
                <Stack spacing={1.5}>
                  {hits.map(hit => (
                    <Card
                      key={hit.claimId}
                      padding="small"
                      title={t('memoryPage.claimVersion', { scope: domainLabel(hit.scope), version: format.number(toFiniteNumber(hit.version)) })}
                      action={<Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', justifyContent: 'flex-end' }}>
                        <StatusChip label={domainLabel(hit.taint)} tone={hit.taint === 'trusted' ? 'success' : hit.taint === 'untrusted' ? 'warning' : 'error'} />
                        <StatusChip label={domainLabel(hit.reviewRequired ? 'ReadyForReview' : 'Validated')} tone={hit.reviewRequired ? 'warning' : 'success'} />
                        <FreshnessBadge freshness="unknown" label={t('common.freshness.unknown')} />
                      </Stack>}
                    >
                      <Stack spacing={1.25}>
                        <Text variant="bodyStrong">{hit.content}</Text>
                        <DetailList dense columns={2} items={[
                          { label: t('common.evidence'), value: hit.sourceRef, mono: true },
                          { label: t('memoryPage.confidence'), value: format.number(toFiniteNumber(hit.confidence)) },
                          { label: t('common.version'), value: format.number(toFiniteNumber(hit.version)) },
                          { label: t('common.scope'), value: domainLabel(hit.scope) }
                        ]} />
                        <Text variant="caption">{t('memoryPage.scoreVectorKeywordGraph', { value0: format.number(toFiniteNumber(hit.score), { minimumFractionDigits: 3, maximumFractionDigits: 3 }), value1: format.number(toFiniteNumber(hit.vectorScore), { minimumFractionDigits: 3, maximumFractionDigits: 3 }), value2: format.number(toFiniteNumber(hit.keywordScore), { minimumFractionDigits: 3, maximumFractionDigits: 3 }), value3: hit.graphExpanded ? domainLabel('expanded') : domainLabel('direct') })}</Text>
                        <Stack direction="row" wrap spacing={0.5}>
                          {hit.conflictWithIds.length > 0 ? <StatusChip label={t('memoryPage.conflicts', { value0: hit.conflictWithIds.join(', ') })} tone="warning" /> : null}
                          {hit.reviewRequired ? <StatusChip label={t('memoryPage.reviewRules', { value0: hit.findingRules.join(', ') })} tone="warning" /> : null}
                        </Stack>
                        <Text variant="caption">{t('common.nextAction', { action: hit.reviewRequired || hit.conflictWithIds.length > 0 ? t('common.review') : hit.scope === 'personal' && nominationScope.trim() ? t('memoryPage.nominate') : t('memoryPage.inspectImpact') })}</Text>
                        <Stack direction="row" wrap spacing={1}>
                          {hit.scope === 'personal' ? <Button tone="secondary" size="small" disabled={!nominationScope.trim() || nominate.isPending} loading={nominate.isPending && nominate.variables?.id === hit.claimId} onClick={() => nominate.mutate({ id: hit.claimId, scope: nominationScope.trim() })}>{t('memoryPage.nominate')}</Button> : null}
                          <Button tone="secondary" size="small" onClick={() => setSelectedClaimId(hit.claimId)}>{t('memoryPage.inspectImpact')}</Button>
                          <Button tone="neutral" size="small" disabled={archive.isPending} loading={archive.isPending && archive.variables === hit.claimId} onClick={() => archive.mutate(hit.claimId)}>{t('common.archive')}</Button>
                          <Button tone="neutral" size="small" disabled={forget.isPending} loading={forget.isPending && forget.variables === hit.claimId} onClick={() => forget.mutate(hit.claimId)}>{t('memoryPage.forget')}</Button>
                          <Button tone="danger" size="small" disabled={purge.isPending} loading={purge.isPending && purge.variables === hit.claimId} onClick={() => setPendingDeleteClaimId(hit.claimId)}>{t('common.delete')}</Button>
                        </Stack>
                      </Stack>
                    </Card>
                  ))}
                </Stack>
              </Stack>
            </Card>
          </Stack>
        </Stack>
        <MemoryReviewQueue onChanged={invalidate} />
        <MemorySafetyPanel onChanged={invalidate} />
        <MemoryImpactPanel claimId={selectedClaimId} onChanged={invalidate} />
        <ConfirmDialog
          open={pendingDeleteClaimId !== null}
          title={t('common.delete')}
          description={pendingDeleteClaimId ? t('memoryPage.confirmDelete', { claimId: pendingDeleteClaimId }) : undefined}
          confirmLabel={t('common.delete')}
          cancelLabel={t('common.no')}
          tone="danger"
          loading={purge.isPending}
          onClose={() => { if (!purge.isPending) setPendingDeleteClaimId(null); }}
          onConfirm={() => { if (pendingDeleteClaimId && !purge.isPending) purge.mutate(pendingDeleteClaimId); }}
        />
      </Stack>
    </ContentContainer>
  );
}
