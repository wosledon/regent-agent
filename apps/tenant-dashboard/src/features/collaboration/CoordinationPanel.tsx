import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Avatar, Card, EmptyState, FilterBar, Grid, Icon, MetricCard, SectionHeader,
  Select, Stack, StatusChip, Text, type ChipTone
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../app/useAuth';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';
import { localizedCount } from '../../lib/localizedCount';
import type { TaskCoordination } from '../../lib/domain-types';

interface CoordinationPanelProps {
  employees: Awaited<ReturnType<typeof api.employees>>;
}

function coordinationTone(status: string): ChipTone {
  if (['Completed', 'Resolved', 'Resumed'].includes(status)) return 'success';
  if (['Rejected', 'Cancelled'].includes(status)) return 'error';
  if (['Proposed', 'Requested', 'Open', 'Accepted'].includes(status)) return 'info';
  return 'warning';
}

export function CoordinationPanel({ employees }: CoordinationPanelProps) {
  const { session } = useAuth();
  const canReadCollaboration = session?.effectiveAccess?.canReadCollaboration === true;
  const format = useTenantFormatting(canReadCollaboration);
  const { t } = useTranslation();
  const [kind, setKind] = useState('all');
  const [status, setStatus] = useState('all');
  const requests = useQuery({
    queryKey: ['collaboration', 'requests', kind, status],
    queryFn: () => api.collaborationRequests(kind, status),
    enabled: canReadCollaboration
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

  if (!canReadCollaboration) return null;
  return (
    <Stack spacing={2}>
      <SectionHeader
        title={t('collaborationPage.delegationHandoffAndInterruptProcess')}
        description={t('collaborationPage.thisControlPlaneIsReadOnlyAcceptanceRejectionCheckpointingAndResumptionA')}
        action={<StatusChip label={localizedCount(t, format, 'collaborationPage.activeCount', counts.active)} tone="warning" />}
      />
      <Grid columns={4}>
        <MetricCard label={t('common.status')} value={format.number(counts.active)} hint={t('collaborationPage.activeCount', { value: counts.active })} icon="activity" />
        <MetricCard label={t('collaborationPage.delegations')} value={format.number(counts.delegation)} icon="workflow" />
        <MetricCard label={t('collaborationPage.handoffs')} value={format.number(counts.handoff)} icon="support" />
        <MetricCard label={t('collaborationPage.interrupts')} value={format.number(counts.interrupt)} icon="notification" />
      </Grid>
      <Card padding="small">
        <FilterBar action={<Text variant="caption">{t('collaborationPage.delegationsHandoffsInterrupts', {
          delegations: localizedCount(t, format, 'collaborationPage.delegationCount', counts.delegation),
          handoffs: localizedCount(t, format, 'collaborationPage.handoffCount', counts.handoff),
          interrupts: localizedCount(t, format, 'collaborationPage.interruptCount', counts.interrupt)
        })}</Text>}>
          <Stack sx={{ width: { xs: '100%', sm: 220 } }}>
            <Select label={t('collaborationPage.coordinationKind')} value={kind} onChange={setKind} options={[
              { value: 'all', label: t('collaborationPage.allKinds') },
              { value: 'Delegation', label: t('collaborationPage.delegations') },
              { value: 'Handoff', label: t('collaborationPage.handoffs') },
              { value: 'Help', label: t('collaborationPage.helpRequests') },
              { value: 'Interrupt', label: t('collaborationPage.interrupts') }
            ]} />
          </Stack>
          <Stack sx={{ width: { xs: '100%', sm: 220 } }}>
            <Select label={t('collaborationPage.coordinationStatus')} value={status} onChange={setStatus} options={[
              { value: 'all', label: t('collaborationPage.allStatuses') },
              { value: 'Proposed', label: t('collaborationPage.proposed') },
              { value: 'Requested', label: t('collaborationPage.requested') },
              { value: 'Open', label: t('common.open') },
              { value: 'Accepted', label: t('collaborationPage.accepted') },
              { value: 'InProgress', label: t('collaborationPage.inProgress') },
              { value: 'Suspended', label: t('collaborationPage.suspended') },
              { value: 'ReadyToResume', label: t('collaborationPage.readyToResume') },
              { value: 'Completed', label: t('collaborationPage.completed') },
              { value: 'Resolved', label: t('collaborationPage.resolved') },
              { value: 'Rejected', label: t('collaborationPage.rejected') },
              { value: 'Cancelled', label: t('collaborationPage.cancelled') },
              { value: 'Resumed', label: t('collaborationPage.resumed') }
            ]} />
          </Stack>
        </FilterBar>
        {requests.isLoading ? <LocalizedLoadingState /> : null}
        {requests.error ? <LocalizedErrorState error={requests.error} /> : null}
        {!requests.isLoading && !requests.error && !requests.data?.items.length ? (
          <EmptyState title={t('collaborationPage.noCoordinationRequests')} description={t('collaborationPage.employeeAgentsCanDelegateWorkHandOffCheckpointsAskForHelpOrRequestP0P1In')} />
        ) : null}
        {requests.data?.items.length ? (
          <Grid columns={2}>
            {requests.data.items.map(item => <CoordinationCard key={item.id} item={item} employeeName={employeeName} />)}
          </Grid>
        ) : null}
      </Card>
    </Stack>
  );
}

function CoordinationCard({ item, employeeName }: { item: TaskCoordination; employeeName: (id: string) => string }) {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const details = formatJson(item.detailsJson);
  const response = item.responseJson ? formatJson(item.responseJson) : null;
  const requester = employeeName(item.requesterEmployeeId);
  const target = employeeName(item.targetEmployeeId);
  return (
    <Stack spacing={1.5} sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 2, minWidth: 0, bgcolor: 'background.paper' }}>
      <Stack direction="row" wrap spacing={1} alignItems="center">
        <Text variant="h3">{domainLabel(item.kind)}</Text>
        <StatusChip label={domainLabel(item.status)} tone={coordinationTone(item.status)} />
        <StatusChip label={domainLabel(item.priority)} tone={item.priority === 'P0' ? 'error' : 'info'} />
        <Text variant="caption" sx={{ ml: 'auto' }}>{t('collaborationPage.v', { value0: format.dateTime(item.updatedAt), value1: format.number(toFiniteNumber(item.version)) })}</Text>
      </Stack>
      <Text variant="bodyStrong">{item.summary}</Text>
      <Stack direction="row" spacing={1.5} wrap alignItems="center">
        <Stack direction="row" spacing={1} alignItems="center">
          <Avatar name={requester} size="small" />
          <Text variant="caption">{requester}</Text>
        </Stack>
        <Icon name="chevronRight" sx={{ color: 'text.disabled' }} />
        <Stack direction="row" spacing={1} alignItems="center">
          <Avatar name={target} size="small" />
          <Text variant="caption">{target}</Text>
        </Stack>
      </Stack>
      <Stack direction="row" wrap spacing={2}>
        <Text variant="caption">{t('collaborationPage.task', { value0: requester, value1: target, value2: item.taskId })}</Text>
        {item.deadline ? <Text variant="caption">{t('collaborationPage.deadline', { value0: format.dateTime(item.deadline) })}</Text> : null}
      </Stack>
      <Stack spacing={0.5} sx={{ p: 1.5, bgcolor: 'background.default', border: 1, borderColor: 'divider', borderRadius: 1 }}>
        <Text variant="caption" color="text.secondary">{t('common.task')}</Text>
        <Text component="pre" variant="mono" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', m: 0 }}>{details}</Text>
      </Stack>
      {response ? (
        <Stack spacing={0.5} sx={{ p: 1.5, bgcolor: 'background.default', border: 1, borderColor: 'divider', borderRadius: 1 }}>
          <Text variant="caption" color="text.secondary">{t('common.evidence')}</Text>
          <Text component="pre" variant="mono" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', m: 0 }}>{response}</Text>
        </Stack>
      ) : null}
    </Stack>
  );
}

function formatJson(value: string) {
  try { return JSON.stringify(JSON.parse(value), null, 2); }
  catch { return value; }
}
