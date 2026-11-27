import { useMutation, useQuery } from '@tanstack/react-query';
import { Button, Card, EmptyState, FreshnessBadge, SectionHeader, Stack, StatusChip, Text } from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { toFiniteNumber } from '../../lib/formatting';

interface MemoryReviewQueueProps {
  onChanged: () => void | Promise<void>;
}

export function MemoryReviewQueue({ onChanged }: MemoryReviewQueueProps) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const queue = useQuery({ queryKey: ['memory-review-queue'], queryFn: api.memoryReviewQueue });
  const publish = useMutation({
    mutationFn: ({ id, decision }: { id: string; decision: 'Approve' | 'Reject' }) => api.publishMemory(id, decision),
    onSuccess: () => onChanged()
  });
  if (queue.isLoading) return <LocalizedLoadingState />;
  if (queue.error) return <LocalizedErrorState error={queue.error} />;

  const claims = queue.data ?? [];
  return (
    <Stack spacing={2}>
      <SectionHeader title={t('memoryPage.publicationReviewQueue')} action={<StatusChip label={format.number(claims.length)} tone={claims.length > 0 ? 'warning' : 'neutral'} />} />
      <LocalizedMutationFeedback mutation={publish} successMessage={t('memoryPage.publicationReviewRecorded')} />
      {!claims.length ? <EmptyState title={t('memoryPage.noCandidateClaims')} compact /> : claims.map(claim => (
        <Card
          key={claim.id}
          padding="small"
          title={t('memoryPage.claimVersion', { scope: domainLabel(claim.scope), version: format.number(toFiniteNumber(claim.version)) })}
          description={t('memoryPage.sourceAuthorConfidence', { value0: claim.sourceRef, value1: claim.author ?? t('common.unknown'), value2: format.number(toFiniteNumber(claim.confidence)) })}
          action={<Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', justifyContent: 'flex-end' }}>
            <StatusChip label={domainLabel(claim.scope)} tone="info" />
            <StatusChip label={domainLabel(claim.verified ? 'Validated' : 'ReadyForReview')} tone={claim.verified ? 'success' : 'warning'} />
            <FreshnessBadge freshness="unknown" label={t('common.freshness.unknown')} />
          </Stack>}
        >
          <Stack spacing={1.25}>
            <Text variant="bodyStrong">{claim.content}</Text>
            <Text variant="caption">{t('common.nextAction', { action: t('common.publish') })}</Text>
            <Stack direction="row" wrap spacing={1}>
              <Button size="small" disabled={publish.isPending} loading={publish.isPending && publish.variables?.id === claim.id && publish.variables.decision === 'Approve'} onClick={() => publish.mutate({ id: claim.id, decision: 'Approve' })}>{t('common.publish')}</Button>
              <Button tone="danger" size="small" disabled={publish.isPending} loading={publish.isPending && publish.variables?.id === claim.id && publish.variables.decision === 'Reject'} onClick={() => publish.mutate({ id: claim.id, decision: 'Reject' })}>{t('common.reject')}</Button>
            </Stack>
          </Stack>
        </Card>
      ))}
    </Stack>
  );
}
