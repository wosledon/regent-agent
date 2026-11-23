import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, ErrorState, LoadingState, Select, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import type { ComplianceProfile } from '../../lib/domain-types';

export function CompliancePage() {
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
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  if (!profile) return null;
  return (
    <div>
      <Text variant="h1">Compliance</Text>
      <Card title="Policy" action={<StatusChip label={profile.status} tone="success" />}>
        <Select label="Preset" value={profile.preset} options={[{ value: 'baseline', label: 'Baseline' }, { value: 'strict', label: 'Strict' }, { value: 'custom', label: 'Custom' }]} onChange={preset => setDraft({ ...profile, preset })} />
        <Select label="Audit profile" value={profile.auditProfile} options={[{ value: 'standard', label: 'Standard' }, { value: 'worm', label: 'WORM' }, { value: 'anchored', label: 'Anchored' }]} onChange={auditProfile => setDraft({ ...profile, auditProfile })} />
        <TextInput value={String(profile.onlineRetentionDays)} onChange={value => setDraft({ ...profile, onlineRetentionDays: Number(value) || 0 })} />
        <Text>Providers: {profile.allowedProviders.join(', ')}</Text>
        <Text>Regions: {profile.allowedRegions.join(', ')}</Text>
        <Button onClick={() => save.mutate()} loading={save.isPending}>Save policy</Button>
      </Card>
    </div>
  );
}
