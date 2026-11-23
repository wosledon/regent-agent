import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, EmptyState, ErrorState, FormField, Grid, LoadingState, PageHeader,
  Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import { MemoryCaptureForm } from './MemoryCaptureForm';
import { MemoryImpactPanel } from './MemoryImpactPanel';
import { MemoryReviewQueue } from './MemoryReviewQueue';
import { MemorySafetyPanel } from './MemorySafetyPanel';

export function MemoryPage() {
  const queryClient = useQueryClient();
  const [query, setQuery] = useState('Provider contract');
  const [scope, setScope] = useState('organization');
  const [selectedClaimId, setSelectedClaimId] = useState('');
  const [nominationScope, setNominationScope] = useState('');
  const search = useQuery({
    queryKey: ['memory-search', query, scope],
    queryFn: () => api.searchMemories(query, scope || undefined),
    enabled: query.trim().length > 0
  });
  const rebuild = useMutation({ mutationFn: api.rebuildMemoryIndex, onSuccess: invalidate });
  const forget = useMutation({ mutationFn: api.forgetMemory, onSuccess: invalidate });
  const purge = useMutation({ mutationFn: api.deleteMemory, onSuccess: invalidate });
  const archive = useMutation({ mutationFn: api.archiveMemory, onSuccess: invalidate });
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

  return (
    <Stack spacing={3}>
      <PageHeader title="Knowledge & Memory" description="Hybrid recall, poisoning detection, independent correction and impact tracing."
        actions={<Button tone="secondary" onClick={() => rebuild.mutate()} loading={rebuild.isPending}>Rebuild index</Button>} />
      <Grid columns={2}>
        <MemoryCaptureForm onCaptured={invalidate} />
        <Card title="Hybrid recall">
          <Stack spacing={2}>
            <FormField label="Search query">
              <TextInput value={query} onChange={setQuery} placeholder="Search memory" />
            </FormField>
            <FormField label="Nomination scope position ID"><TextInput value={nominationScope} onChange={setNominationScope} placeholder="Position ID for upward nomination" /></FormField>
            <Select label="Scope" value={scope} onChange={setScope} options={[
              { value: '', label: 'All scopes' },
              { value: 'personal', label: 'Personal' },
              { value: 'organization', label: 'Organization' },
              { value: 'knowledge_base', label: 'Knowledge base' }
            ]} />
            {search.isLoading ? <LoadingState /> : null}
            {search.error ? <ErrorState message={search.error.message} /> : null}
            {search.data?.hits.length === 0 ? <EmptyState title="No matching memory" description="Quarantined and forgotten claims are excluded from recall." /> : null}
            {search.data?.hits.map(hit => (
              <Card key={hit.claimId} title={`${hit.scope} · v${hit.version}`} padding="small"
                action={<StatusChip label={hit.taint} tone={hit.taint === 'trusted' ? 'success' : 'error'} />}>
                <Stack spacing={1}>
                  <Text>{hit.content}</Text>
                  <Text variant="caption">Score {Number(hit.score).toFixed(3)} · vector {Number(hit.vectorScore).toFixed(3)} · keyword {Number(hit.keywordScore).toFixed(3)} · graph {hit.graphExpanded ? 'expanded' : 'direct'}</Text>
                  <Text variant="caption">Source: {hit.sourceRef} · confidence {hit.confidence} · index {search.data.indexVersion}</Text>
                  {hit.conflictWithIds.length > 0 ? <StatusChip label={`Conflicts: ${hit.conflictWithIds.join(', ')}`} tone="warning" /> : null}
                  {hit.reviewRequired ? <StatusChip label={`Review: ${hit.findingRules.join(', ')}`} tone="warning" /> : null}
                  <Stack direction="row" wrap>
                    {hit.scope === 'personal' ? <Button tone="secondary" disabled={!nominationScope} onClick={() => nominate.mutate({ id: hit.claimId, scope: nominationScope })}>Nominate</Button> : null}
                    <Button tone="secondary" onClick={() => setSelectedClaimId(hit.claimId)}>Inspect impact</Button>
                    <Button tone="neutral" onClick={() => archive.mutate(hit.claimId)} loading={archive.isPending}>Archive</Button>
                    <Button tone="neutral" onClick={() => forget.mutate(hit.claimId)} loading={forget.isPending}>Forget</Button>
                    <Button tone="danger" onClick={() => purge.mutate(hit.claimId)} loading={purge.isPending}>Delete</Button>
                  </Stack>
                </Stack>
              </Card>
            ))}
          </Stack>
        </Card>
      </Grid>
      <MemoryReviewQueue onChanged={invalidate} />
      <MemorySafetyPanel onChanged={invalidate} />
      <MemoryImpactPanel claimId={selectedClaimId} onChanged={invalidate} />
    </Stack>
  );
}
