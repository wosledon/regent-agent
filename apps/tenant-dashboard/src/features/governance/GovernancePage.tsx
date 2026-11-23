import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, DataTable, ErrorState, FormField, Grid, LoadingState,
  PageHeader, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function GovernancePage() {
  const queryClient = useQueryClient();
  const governance = useQuery({ queryKey: ['governance'], queryFn: api.governance });
  const [toolName, setToolName] = useState('');
  const [toolVersion, setToolVersion] = useState('1.0.0');
  const [toolCapabilities, setToolCapabilities] = useState('');
  const [scanContent, setScanContent] = useState('');
  const [scanResult, setScanResult] = useState<Awaited<ReturnType<typeof api.scanInjection>> | null>(null);
  const [dlpContent, setDlpContent] = useState('');
  const [dlpResult, setDlpResult] = useState<Awaited<ReturnType<typeof api.redactDlp>> | null>(null);
  const [incidentTitle, setIncidentTitle] = useState('');
  const [severity, setSeverity] = useState('SEV3');
  const [commander, setCommander] = useState('Operator');
  const [primary, setPrimary] = useState('Owner');
  const [delegate, setDelegate] = useState('Controller');
  const [scope, setScope] = useState('release.execute');
  const [expiresAt, setExpiresAt] = useState('');
  const [contentRef, setContentRef] = useState('');
  const [contentText, setContentText] = useState('');
  const [appealReason, setAppealReason] = useState('');
  const [incidentId, setIncidentId] = useState('');
  const [evidenceText, setEvidenceText] = useState('');
  const [verification, setVerification] = useState<Awaited<ReturnType<typeof api.verifyIncidentEvidence>> | null>(null);
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['governance'] });

  const registerTool = useMutation({
    mutationFn: () => api.registerTool({
      id: crypto.randomUUID(), name: toolName, version: toolVersion,
      capabilities: toolCapabilities.split(',').map(value => value.trim()).filter(Boolean),
      sbomJson: JSON.stringify({ vulnerabilities: [] }), digest: '', signature: '', status: 'Draft', createdAt: new Date().toISOString()
    }), onSuccess: async () => { setToolName(''); await invalidate(); }
  });
  const admitTool = useMutation({ mutationFn: (id: string) => api.admitTool(id, 'Security Reviewer'), onSuccess: invalidate });
  const scan = useMutation({ mutationFn: () => api.scanInjection(scanContent), onSuccess: setScanResult });
  const redact = useMutation({ mutationFn: () => api.redactDlp(dlpContent), onSuccess: setDlpResult });
  const createIncident = useMutation({
    mutationFn: () => api.createIncident({
      id: crypto.randomUUID(), severity, title: incidentTitle, commander,
      status: 'Open', containmentActions: [], evidenceRefs: [], notifications: [],
      recoverySummary: null, retrospective: null, createdAt: new Date().toISOString()
    }), onSuccess: async () => { setIncidentTitle(''); await invalidate(); }
  });
  const contain = useMutation({ mutationFn: ({ id, action }: { id: string; action: string }) => api.containIncident(id, action), onSuccess: invalidate });
  const recover = useMutation({ mutationFn: (id: string) => api.recoverIncident(id, 'Service restored and evidence preserved'), onSuccess: invalidate });
  const close = useMutation({ mutationFn: (id: string) => api.closeIncident(id, 'Post-incident review completed'), onSuccess: invalidate });
  const review = useMutation({ mutationFn: () => api.reviewContent(contentRef, contentText), onSuccess: async () => { setContentText(''); await invalidate(); } });
  const appeal = useMutation({ mutationFn: (id: string) => api.appealContent(id, appealReason), onSuccess: async () => { setAppealReason(''); await invalidate(); } });
  const resolveAppeal = useMutation({ mutationFn: ({ id, upheld }: { id: string; upheld: boolean }) => api.resolveContentAppeal(id, upheld), onSuccess: invalidate });
  const sealEvidence = useMutation({ mutationFn: () => api.sealIncidentEvidence(incidentId, evidenceText), onSuccess: async () => { setEvidenceText(''); await invalidate(); } });
  const verifyEvidence = useMutation({ mutationFn: () => api.verifyIncidentEvidence(incidentId), onSuccess: setVerification });
  const createDelegation = useMutation({
    mutationFn: () => api.createApprovalDelegation({
      id: crypto.randomUUID(), primaryApprover: primary, delegateApprover: delegate,
      actionScope: scope, expiresAt: new Date(expiresAt).toISOString(), status: 'Active', reason: 'Planned absence'
    }), onSuccess: async () => { setExpiresAt(''); await invalidate(); }
  });

  if (governance.isLoading) return <LoadingState />;
  if (governance.error) return <ErrorState message={governance.error.message} />;
  const state = governance.data;

  return (
    <Stack spacing={3}>
      <PageHeader title="Security & governance" description="Execution-boundary authorization, supply chain, DLP, content safety and incident response." />
      <Grid columns={2}>
        <Card title="Tool supply chain">
          <FormField label="Tool name"><TextInput value={toolName} onChange={setToolName} placeholder="research.search" /></FormField>
          <FormField label="Version"><TextInput value={toolVersion} onChange={setToolVersion} /></FormField>
          <FormField label="Capabilities"><TextInput value={toolCapabilities} onChange={setToolCapabilities} placeholder="research.execute" /></FormField>
          <Button onClick={() => registerTool.mutate()} disabled={!toolName || !toolCapabilities} loading={registerTool.isPending}>Register immutable tool</Button>
          <DataTable rows={state?.tools ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'name', header: 'Tool', render: row => `${row.name ?? ''}@${row.version ?? ''}` },
            { id: 'status', header: 'Status', render: row => <StatusChip label={row.status ?? 'Unknown'} tone={row.status === 'Admitted' ? 'success' : 'warning'} /> },
            { id: 'action', header: 'Action', render: row => <Button tone="secondary" onClick={() => admitTool.mutate(row.id ?? '')} disabled={row.status !== 'Draft'}>Admit</Button> }
          ]} />
        </Card>
        <Card title="Prompt and data boundaries">
          <FormField label="Untrusted content"><TextInput value={scanContent} onChange={setScanContent} multiline minRows={3} placeholder="Ignore previous instructions..." /></FormField>
          <Button onClick={() => scan.mutate()} disabled={!scanContent} loading={scan.isPending}>Scan injection</Button>
          {scanResult ? <StatusChip label={scanResult.safe ? 'No injection pattern' : 'Quarantine'} tone={scanResult.safe ? 'success' : 'error'} /> : null}
          {scanResult?.findings?.map(finding => <Text key={finding} variant="caption">{finding}</Text>)}
          <FormField label="Sensitive content"><TextInput value={dlpContent} onChange={setDlpContent} multiline minRows={2} placeholder="Contact owner@example.test" /></FormField>
          <Button tone="secondary" onClick={() => redact.mutate()} disabled={!dlpContent} loading={redact.isPending}>Redact and tokenize</Button>
          {dlpResult ? <Text variant="caption">{dlpResult.redactedText ?? ''}</Text> : null}
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card title="Security incident">
          <FormField label="Title"><TextInput value={incidentTitle} onChange={setIncidentTitle} placeholder="Credential exposure" /></FormField>
          <Select label="Severity" value={severity} onChange={setSeverity} options={['SEV1', 'SEV2', 'SEV3', 'SEV4'].map(value => ({ value, label: value }))} />
          <FormField label="Incident commander"><TextInput value={commander} onChange={setCommander} /></FormField>
          <Button tone="danger" onClick={() => createIncident.mutate()} disabled={!incidentTitle} loading={createIncident.isPending}>Declare incident</Button>
          <DataTable rows={state?.incidents ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'title', header: 'Incident', render: row => row.title ?? '' },
            { id: 'severity', header: 'Severity', render: row => row.severity ?? '' },
            { id: 'status', header: 'Status', render: row => row.status ?? '' },
            { id: 'actions', header: 'Actions', render: row => <Stack direction="row">
              <Button tone="neutral" onClick={() => contain.mutate({ id: row.id ?? '', action: 'Revoke affected access' })} disabled={row.status !== 'Open'}>Contain</Button>
              <Button tone="neutral" onClick={() => recover.mutate(row.id ?? '')} disabled={row.status !== 'Contained'}>Recover</Button>
              <Button tone="secondary" onClick={() => close.mutate(row.id ?? '')} disabled={row.status !== 'Recovered'}>Close</Button>
            </Stack> }
          ]} />
        </Card>
        <Card title="Approval absence delegation">
          <FormField label="Primary approver"><TextInput value={primary} onChange={setPrimary} /></FormField>
          <FormField label="Delegate"><TextInput value={delegate} onChange={setDelegate} /></FormField>
          <FormField label="Action scope"><TextInput value={scope} onChange={setScope} /></FormField>
          <FormField label="Expires at"><TextInput value={expiresAt} onChange={setExpiresAt} type="datetime-local" /></FormField>
          <Button onClick={() => createDelegation.mutate()} disabled={!expiresAt || primary === delegate} loading={createDelegation.isPending}>Create delegation</Button>
          <DataTable rows={state?.approvalDelegations ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'primary', header: 'Primary', render: row => row.primaryApprover ?? '' },
            { id: 'delegate', header: 'Delegate', render: row => row.delegateApprover ?? '' },
            { id: 'scope', header: 'Scope', render: row => row.actionScope ?? '' },
            { id: 'expires', header: 'Expires', render: row => row.expiresAt ? new Date(row.expiresAt).toLocaleString() : '' }
          ]} />
        </Card>
      </Grid>
      <Grid columns={2}>
        <Card title="Content safety">
          <FormField label="Content reference"><TextInput value={contentRef} onChange={setContentRef} /></FormField>
          <FormField label="Content"><TextInput value={contentText} onChange={setContentText} multiline minRows={2} /></FormField>
          <Button onClick={() => review.mutate()} disabled={!contentRef || !contentText} loading={review.isPending}>Review content</Button>
          <DataTable rows={state?.contentFindings ?? []} getRowId={row => row.id ?? ''} columns={[
            { id: 'ref', header: 'Content', render: row => row.contentRef ?? '' },
            { id: 'category', header: 'Category', render: row => row.category ?? '' },
            { id: 'status', header: 'Status', render: row => <StatusChip label={row.status ?? ''} tone={row.disposition === 'Block' ? 'error' : 'warning'} /> },
            { id: 'actions', header: 'Actions', render: row => <Stack direction="row">
              <Button tone="neutral" onClick={() => appeal.mutate(row.id ?? '')} disabled={row.status !== 'Open'}>Appeal</Button>
              <Button tone="secondary" onClick={() => resolveAppeal.mutate({ id: row.id ?? '', upheld: true })} disabled={row.status !== 'UnderReview'}>Uphold</Button>
              <Button tone="secondary" onClick={() => resolveAppeal.mutate({ id: row.id ?? '', upheld: false })} disabled={row.status !== 'UnderReview'}>Overturn</Button>
            </Stack> }
          ]} />
          <FormField label="Appeal reason"><TextInput value={appealReason} onChange={setAppealReason} /></FormField>
        </Card>
        <Card title="Incident evidence">
          <Select label="Incident" value={incidentId} onChange={setIncidentId} options={[{ value: '', label: 'Select incident' }, ...(state?.incidents ?? []).map(item => ({ value: item.id ?? '', label: item.title ?? '' }))]} />
          <FormField label="Evidence"><TextInput value={evidenceText} onChange={setEvidenceText} multiline minRows={2} /></FormField>
          <Stack direction="row">
            <Button onClick={() => sealEvidence.mutate()} disabled={!incidentId || !evidenceText} loading={sealEvidence.isPending}>Sign evidence</Button>
            <Button tone="secondary" onClick={() => verifyEvidence.mutate()} disabled={!incidentId} loading={verifyEvidence.isPending}>Verify evidence</Button>
          </Stack>
          {verification ? <StatusChip label={verification.message ?? ''} tone={verification.valid ? 'success' : 'error'} /> : null}
        </Card>
      </Grid>
    </Stack>
  );
}
