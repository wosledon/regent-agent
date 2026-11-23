import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, Card, DataTable, EmptyState, ErrorState, FormField, LoadingState, Stack, StatusChip, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import { useAuth } from '../../app/useAuth';

type Finding = Awaited<ReturnType<typeof api.memoryFindings>>[number];
type Correction = Awaited<ReturnType<typeof api.memoryCorrections>>[number];

interface MemorySafetyPanelProps {
  onChanged: () => void | Promise<void>;
}

export function MemorySafetyPanel({ onChanged }: MemorySafetyPanelProps) {
  const { session } = useAuth();
  const [reviewer, setReviewer] = useState('Boss');
  const [findingId, setFindingId] = useState('');
  const [claimId, setClaimId] = useState('');
  const [correctionContent, setCorrectionContent] = useState('');
  const [correctionReason, setCorrectionReason] = useState('Independent evidence review');
  const [appealReason, setAppealReason] = useState('Quoted security example, not an instruction');
  const findings = useQuery({ queryKey: ['memory-findings'], queryFn: api.memoryFindings });
  const corrections = useQuery({ queryKey: ['memory-corrections'], queryFn: api.memoryCorrections });

  if (findings.isLoading || corrections.isLoading) return <LoadingState />;
  if (findings.error || corrections.error) return <ErrorState message={(findings.error ?? corrections.error)?.message ?? ''} />;

  const prepareCorrection = (finding: Finding) => {
    setFindingId(finding.id);
    setClaimId(finding.claimId);
  };

  return (
    <Stack spacing={3}>
      <Card title="Poisoning findings">
        <Stack spacing={2}>
          <FormField label="Independent reviewer">
            <TextInput value={reviewer} onChange={setReviewer} placeholder="Boss" />
          </FormField>
          <FormField label="Appeal reason">
            <TextInput value={appealReason} onChange={setAppeal} placeholder="Evidence for appeal" />
          </FormField>
          {findings.data?.length === 0 ? <EmptyState title="No memory findings" /> : null}
          <DataTable rows={findings.data ?? []} getRowId={row => row.id} columns={[
            { id: 'rule', header: 'Rule', render: row => row.ruleId },
            { id: 'risk', header: 'Risk', render: row => <StatusChip label={row.riskLevel} tone={row.riskLevel === 'High' ? 'error' : 'warning'} /> },
            { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone={row.status === 'Released' || row.status === 'Corrected' ? 'success' : 'warning'} /> },
            { id: 'claim', header: 'Claim', render: row => row.claimId },
            { id: 'actions', header: 'Actions', render: row => (
              <Stack direction="row" wrap>
                {row.status === 'Open' ? <Button tone="danger" onClick={() => quarantine(row)}>Quarantine</Button> : null}
                {row.status === 'Quarantined' ? <Button tone="neutral" onClick={() => appeal(row)}>Appeal</Button> : null}
                {row.status === 'Appealed' ? <Button tone="secondary" onClick={() => review(row, true)}>Release</Button> : null}
                {row.status === 'Appealed' ? <Button tone="danger" onClick={() => review(row, false)}>Keep quarantined</Button> : null}
                {row.status !== 'Corrected' ? <Button tone="neutral" onClick={() => prepareCorrection(row)}>Prepare correction</Button> : null}
              </Stack>
            ) }
          ]} />
        </Stack>
      </Card>
      <Card title="Independent correction">
        <Stack spacing={2}>
          <FormField label="Claim ID"><TextInput value={claimId} onChange={setClaimId} placeholder="Select a finding or enter a claim id" /></FormField>
          <FormField label="Finding ID"><TextInput value={findingId} onChange={setFindingId} placeholder="Optional finding id" /></FormField>
          <FormField label="Proposed content"><TextInput multiline minRows={2} value={correctionContent} onChange={setCorrectionContent} placeholder="Corrected claim" /></FormField>
          <FormField label="Reason"><TextInput value={correctionReason} onChange={setCorrectionReason} /></FormField>
          <Button disabled={!claimId || !correctionContent.trim()} onClick={createCorrection}>Create correction</Button>
        </Stack>
      </Card>
      <Card title="Correction tasks">
        {corrections.data?.length === 0 ? <EmptyState title="No correction tasks" /> : null}
        <DataTable rows={corrections.data ?? []} getRowId={row => row.id} columns={[
          { id: 'claim', header: 'Claim', render: row => row.claimId },
          { id: 'reviewer', header: 'Reviewer', render: row => row.reviewerId ?? 'Unassigned' },
          { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone={row.status === 'Completed' ? 'success' : 'warning'} /> },
          { id: 'proposal', header: 'Proposal', render: row => row.proposedContent ?? 'Proposal required' },
          { id: 'action', header: 'Action', render: row => row.status === 'PendingReview' && row.proposedContent
            ? <Button tone="secondary" onClick={() => approve(row)}>Approve correction</Button> : null }
        ]} />
      </Card>
    </Stack>
  );

  function setAppeal(value: string) {
    setAppealReason(value);
  }

  async function quarantine(finding: Finding) {
    await api.quarantineMemoryFinding(finding.id, reviewer, 'Independent safety review', 'organization');
    await onChanged();
  }

  async function appeal(finding: Finding) {
    await api.appealMemoryFinding(finding.id, appealReason, session?.displayName ?? 'Owner');
    await onChanged();
  }

  async function review(finding: Finding, release: boolean) {
    await api.reviewMemoryAppeal(finding.id, reviewer, release ? 'Evidence supports release' : 'Evidence does not support release', release);
    await onChanged();
  }

  async function createCorrection() {
    await api.createMemoryCorrection({ claimId, content: correctionContent, reason: correctionReason, reviewerId: reviewer, findingId: findingId || undefined });
    setCorrectionContent('');
    await onChanged();
  }

  async function approve(correction: Correction) {
    await api.approveMemoryCorrection(correction.id, correction.reviewerId ?? reviewer);
    await onChanged();
  }
}
