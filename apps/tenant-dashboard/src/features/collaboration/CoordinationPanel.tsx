import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Card, EmptyState, ErrorState, LoadingState, Select, Stack, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import type { TaskCoordination } from '../../lib/domain-types';

interface CoordinationPanelProps {
  employees: Awaited<ReturnType<typeof api.employees>>;
}

export function CoordinationPanel({ employees }: CoordinationPanelProps) {
  const [kind, setKind] = useState('all');
  const [status, setStatus] = useState('all');
  const requests = useQuery({
    queryKey: ['collaboration', 'requests', kind, status],
    queryFn: () => api.collaborationRequests(kind, status)
  });
  const employeeName = (id: string) => employees.find(employee => employee.id === id)?.displayName ?? id;
  const counts = useMemo(() => {
    const items = requests.data?.items ?? [];
    return {
      active: items.filter(item => !['Completed', 'Resolved', 'Rejected', 'Cancelled', 'Resumed'].includes(item.status)).length,
      delegation: items.filter(item => item.kind === 'Delegation').length,
      handoff: items.filter(item => item.kind === 'Handoff').length,
      interrupt: items.filter(item => item.kind === 'Interrupt').length
    };
  }, [requests.data?.items]);

  return (
    <Card title="Delegation, handoff and interrupt process" action={<StatusChip label={`${counts.active} active`} tone="warning" />}>
      <Stack direction="row" wrap spacing={2}>
        <Select label="Coordination kind" value={kind} onChange={setKind} options={[
          { value: 'all', label: 'All kinds' },
          { value: 'Delegation', label: 'Delegations' },
          { value: 'Handoff', label: 'Handoffs' },
          { value: 'Help', label: 'Help requests' },
          { value: 'Interrupt', label: 'Interrupts' }
        ]} />
        <Select label="Coordination status" value={status} onChange={setStatus} options={[
          { value: 'all', label: 'All statuses' },
          { value: 'Proposed', label: 'Proposed' },
          { value: 'Requested', label: 'Requested' },
          { value: 'Open', label: 'Open' },
          { value: 'Accepted', label: 'Accepted' },
          { value: 'InProgress', label: 'In progress' },
          { value: 'Suspended', label: 'Suspended' },
          { value: 'ReadyToResume', label: 'Ready to resume' },
          { value: 'Completed', label: 'Completed' },
          { value: 'Resolved', label: 'Resolved' },
          { value: 'Rejected', label: 'Rejected' },
          { value: 'Cancelled', label: 'Cancelled' },
          { value: 'Resumed', label: 'Resumed' }
        ]} />
        <Text variant="caption">{counts.delegation} delegations · {counts.handoff} handoffs · {counts.interrupt} interrupts</Text>
      </Stack>
      {requests.isLoading ? <LoadingState /> : null}
      {requests.error ? <ErrorState message={requests.error.message} /> : null}
      {!requests.isLoading && !requests.error && !requests.data?.items.length ? (
        <EmptyState title="No coordination requests" description="Employee Agents can delegate work, hand off checkpoints, ask for help or request P0/P1 interrupts through the Agent data plane." />
      ) : null}
      {requests.data?.items.map(item => <CoordinationCard key={item.id} item={item} employeeName={employeeName} />)}
      <Text variant="caption">This control plane is read-only. Acceptance, rejection, checkpointing and resumption are performed by the employee Agents with task-scoped tokens.</Text>
    </Card>
  );
}

function CoordinationCard({ item, employeeName }: { item: TaskCoordination; employeeName: (id: string) => string }) {
  const details = formatJson(item.detailsJson);
  const response = item.responseJson ? formatJson(item.responseJson) : null;
  return (
    <Stack spacing={1} sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 1, minWidth: 0 }}>
      <Stack direction="row" wrap spacing={1} alignItems="center">
        <Text variant="h3">{item.kind}</Text>
        <StatusChip label={item.status} tone={item.status === 'Rejected' ? 'error' : item.status === 'Completed' || item.status === 'Resolved' ? 'success' : 'warning'} />
        <StatusChip label={item.priority} tone={item.priority === 'P0' ? 'error' : 'info'} />
        <Text variant="caption">{new Date(item.updatedAt).toLocaleString()} · v{item.version}</Text>
      </Stack>
      <Text>{item.summary}</Text>
      <Text variant="caption">{employeeName(item.requesterEmployeeId)} → {employeeName(item.targetEmployeeId)} · task {item.taskId}</Text>
      {item.deadline ? <Text variant="caption">Deadline: {new Date(item.deadline).toLocaleString()}</Text> : null}
      <Text component="pre" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', m: 0, fontSize: '0.75rem' }}>{details}</Text>
      {response ? <Text component="pre" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', m: 0, fontSize: '0.75rem' }}>{response}</Text> : null}
    </Stack>
  );
}

function formatJson(value: string) {
  try { return JSON.stringify(JSON.parse(value), null, 2); }
  catch { return value; }
}
