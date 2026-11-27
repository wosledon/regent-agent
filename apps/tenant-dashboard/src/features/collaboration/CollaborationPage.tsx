import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import {
  Card, ContentContainer, EmptyState, FilterBar, Grid, Icon, MetricCard, PageHeader,
  SegmentedControl, Select, Stack, StatusChip, Text, TextInput
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../app/useAuth';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { localizedCount } from '../../lib/localizedCount';
import { useDomainLabels } from '../../lib/domainLabels';
import { projectCollaborationNode } from './localizedCollaboration';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedFreshnessBadge } from '../common/LocalizedFreshnessBadge';
import { api } from '../../lib/api-client/api';
import type { CollaborationActivity, CollaborationNode } from '../../lib/domain-types';
import { ActivityTimeline } from './ActivityTimeline';
import { CoordinationPanel } from './CoordinationPanel';
import { NetworkView } from './NetworkView';
import { OfficeView } from './OfficeView';
import { OrgChartView } from './OrgChartView';
import { TracePanel } from './TracePanel';
import { collaborationFreshness, isCollaborationView } from './collaborationView';
import type { CollaborationView } from './collaborationView';

export function CollaborationPage() {
  const { session } = useAuth();
  const canReadCollaboration = session?.effectiveAccess?.canReadCollaboration === true;
  const isObserver = session?.effectiveAccess?.collaborationScope === 'observer';
  const format = useTenantFormatting(canReadCollaboration);
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const [searchParams, setSearchParams] = useSearchParams();
  const [query, setQuery] = useState('');
  const [nodeType, setNodeType] = useState('all');
  const graph = useQuery({ queryKey: ['collaboration', 'graph'], queryFn: api.collaborationGraph, enabled: canReadCollaboration });
  const employees = useQuery({ queryKey: ['employees'], queryFn: api.employees, enabled: canReadCollaboration });
  const requestedView = searchParams.get('view') ?? 'office';
  const view: CollaborationView = isCollaborationView(requestedView) ? requestedView : 'office';
  const selectedType = searchParams.get('entityType');
  const selectedId = searchParams.get('entityId');

  const localizedNodes = useMemo(() => (graph.data?.nodes ?? []).map(node =>
    projectCollaborationNode(node, { t, format, domainLabel })), [graph.data?.nodes, t, format, domainLabel]);
  const filteredNodes = useMemo(() => localizedNodes.filter(node => {
    const matchesType = nodeType === 'all' || node.type.toLowerCase() === nodeType;
    const text = `${node.label} ${node.sourceSubtitle} ${node.subtitle}`.toLowerCase();
    return matchesType && (!query || text.includes(query.toLowerCase()));
  }), [localizedNodes, nodeType, query]);
  const visibleIds = useMemo(() => new Set(filteredNodes.map(node => node.id)), [filteredNodes]);
  const filteredEdges = useMemo(() => (graph.data?.edges ?? []).filter(edge => visibleIds.has(edge.sourceId) && visibleIds.has(edge.targetId)), [graph.data?.edges, visibleIds]);
  const filteredActivities = useMemo(() => (graph.data?.activities ?? []).filter(activity => {
    const text = `${activity.title} ${activity.actor} ${activity.type}`.toLowerCase();
    return !query || text.includes(query.toLowerCase());
  }), [graph.data?.activities, query]);
  const selectedNode = localizedNodes.find(node =>
    node.type.toLowerCase() === selectedType
    && (node.employeeId ?? node.conversationId ?? node.projectId) === selectedId) ?? null;

  const selectNode = (node: CollaborationNode) => {
    const next = new URLSearchParams(searchParams);
    next.set('entityType', node.type.toLowerCase());
    next.set('entityId', node.employeeId ?? node.conversationId ?? node.projectId ?? '');
    setSearchParams(next);
  };
  const selectActivity = (activity: CollaborationActivity) => {
    const node = graph.data?.nodes.find(candidate =>
      (activity.conversationId && candidate.conversationId === activity.conversationId)
      || (activity.employeeId && candidate.employeeId === activity.employeeId)
      || (activity.projectId && candidate.projectId === activity.projectId));
    if (node) selectNode(node);
  };
  const changeView = (next: CollaborationView) => {
    const params = new URLSearchParams(searchParams);
    params.set('view', next);
    setSearchParams(params);
  };

  if (!canReadCollaboration) {
    return <EmptyState title={t('access.permissionDenied.title')} description={t('access.permissionDenied.description')} />;
  }
  if (graph.isLoading || employees.isLoading) return <LocalizedLoadingState />;
  if (graph.error || employees.error) return <LocalizedErrorState error={(graph.error ?? employees.error)} />;
  if (!graph.data) return <EmptyState title={t('collaborationPage.noCollaborationData')} description={t('collaborationPage.noCollaborationDataDescription')} />;
  const freshness = collaborationFreshness(graph.data.generatedAt);

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('collaborationPage.organizationCollaboration')}
          description={t('collaborationPage.switchBetweenTheOfficeReportingChartRelationshipNetworkAndProcessTimelin')}
          actions={<Stack direction="row" spacing={1.5} wrap alignItems="center">
            <StatusChip label={t(isObserver ? 'collaborationPage.observerAccess' : 'collaborationPage.participantAccess')} tone="info" />
            <LocalizedFreshnessBadge freshness={freshness} />
          </Stack>}
          meta={<StatusChip
            label={freshness === 'unknown' ? t('collaborationPage.updatedUnknown') : t('collaborationPage.updatedAt', { value0: format.dateTime(graph.data.generatedAt) })}
            tone={freshness === 'stale' ? 'warning' : 'neutral'}
          />}
        />
        <Grid columns={4}>
          <MetricCard label={t('collaborationPage.employees')} value={format.number(graph.data.summary.employeeCount)} hint={t('collaborationPage.activeOrganizationMembers')} icon="people" />
          <MetricCard label={t('collaborationPage.groups')} value={format.number(graph.data.summary.conversationCount)} hint={t('collaborationPage.companyProjectAndTopicRooms')} icon="messages" />
          <MetricCard label={t('collaborationPage.collaborations')} value={format.number(graph.data.summary.collaborationCount)} hint={t('collaborationPage.employeePairsWithSharedMessages')} icon="organization" />
          <MetricCard
            label={t('collaborationPage.traceableEvents')}
            value={format.number(graph.data.summary.activityCount)}
            hint={localizedCount(t, format, 'collaborationPage.coordinationRequests', graph.data.summary.coordinationCount)}
            icon="audit"
            action={<LocalizedFreshnessBadge freshness={freshness} />}
          />
        </Grid>
        <Card padding="small">
          <FilterBar action={<StatusChip label={format.number(filteredNodes.length)} tone="neutral" />}>
            <SegmentedControl
              ariaLabel={t('collaborationPage.collaborationView')}
              value={view}
              onChange={changeView}
              options={[
                { value: 'office', label: t('collaborationPage.office') },
                { value: 'org', label: t('collaborationPage.orgChart') },
                { value: 'network', label: t('collaborationPage.network') },
                { value: 'timeline', label: t('collaborationPage.timeline') }
              ]}
            />
            <Stack sx={{ flex: '1 1 240px' }}>
              <CollaborationSearch value={query} onChange={setQuery} />
            </Stack>
            <Stack sx={{ width: { xs: '100%', sm: 210 } }}>
              <Select label={t('collaborationPage.nodeType')} value={nodeType} onChange={setNodeType} options={[
                { value: 'all', label: t('collaborationPage.allNodes') },
                { value: 'employee', label: t('collaborationPage.employees') },
                { value: 'conversation', label: t('collaborationPage.groups') },
                { value: 'project', label: t('collaborationPage.projects') }
              ]} />
            </Stack>
          </FilterBar>
        </Card>
        {view === 'office' ? <OfficeView nodes={filteredNodes} selectedId={selectedNode?.id} onSelect={selectNode} /> : null}
        {view === 'org' ? <OrgChartView nodes={filteredNodes} edges={filteredEdges} selectedId={selectedNode?.id} onSelect={selectNode} /> : null}
        {view === 'network' ? <NetworkView nodes={filteredNodes} edges={filteredEdges} selectedId={selectedNode?.id} onSelect={selectNode} /> : null}
        {view === 'timeline' ? <ActivityTimeline activities={filteredActivities} onActivitySelect={selectActivity} /> : null}
        <CoordinationPanel employees={employees.data ?? []} />
        <TracePanel node={selectedNode} employees={employees.data ?? []} onClose={() => {
          const next = new URLSearchParams(searchParams);
          next.delete('entityType');
          next.delete('entityId');
          setSearchParams(next);
        }} />
        <Card tone="info" padding="small">
          <Stack direction="row" spacing={1.5} alignItems="flex-start">
            <Icon name="info" sx={{ mt: 0.25, color: 'info.main' }} />
            <Stack spacing={0.25}>
              <Text variant="bodyStrong">{t('collaborationPage.agentOperatedCollaboration')}</Text>
              <Text variant="secondary">{t('collaborationPage.groupsMembershipAndMessagesAreCreatedByEmployeeAgentsThroughTheAgentData')}</Text>
            </Stack>
          </Stack>
        </Card>
      </Stack>
    </ContentContainer>
  );
}

function CollaborationSearch({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const { t } = useTranslation();
  return <TextInput
    value={value}
    onChange={onChange}
    placeholder={t('collaborationPage.searchPeopleGroupsProjectsOrEvents')}
    startAdornment={<Icon name="search" />}
    inputProps={{ 'aria-label': t('collaborationPage.searchCollaboration') }}
  />;
}
