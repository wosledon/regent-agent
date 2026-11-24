import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { Button, Card, EmptyState, ErrorState, FreshnessBadge, Grid, LoadingState, PageHeader, SegmentedControl, Select, Stack, StatusChip, Text, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';
import type { CollaborationActivity, CollaborationNode } from '../../lib/domain-types';
import { ActivityTimeline } from './ActivityTimeline';
import { CreateGroupDialog } from './CreateGroupDialog';
import { NetworkView } from './NetworkView';
import { OfficeView } from './OfficeView';
import { OrgChartView } from './OrgChartView';
import { TracePanel } from './TracePanel';

type CollaborationView = 'office' | 'org' | 'network' | 'timeline';

export function CollaborationPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [query, setQuery] = useState('');
  const [nodeType, setNodeType] = useState('all');
  const [createOpen, setCreateOpen] = useState(false);
  const graph = useQuery({ queryKey: ['collaboration', 'graph'], queryFn: api.collaborationGraph });
  const employees = useQuery({ queryKey: ['employees'], queryFn: api.employees });
  const view = (searchParams.get('view') ?? 'office') as CollaborationView;
  const selectedType = searchParams.get('entityType');
  const selectedId = searchParams.get('entityId');

  const filteredNodes = useMemo(() => (graph.data?.nodes ?? []).filter(node => {
    const matchesType = nodeType === 'all' || node.type.toLowerCase() === nodeType;
    const text = `${node.label} ${node.subtitle}`.toLowerCase();
    return matchesType && (!query || text.includes(query.toLowerCase()));
  }), [graph.data?.nodes, nodeType, query]);
  const visibleIds = useMemo(() => new Set(filteredNodes.map(node => node.id)), [filteredNodes]);
  const filteredEdges = useMemo(() => (graph.data?.edges ?? []).filter(edge => visibleIds.has(edge.sourceId) && visibleIds.has(edge.targetId)), [graph.data?.edges, visibleIds]);
  const filteredActivities = useMemo(() => (graph.data?.activities ?? []).filter(activity => {
    const text = `${activity.title} ${activity.actor} ${activity.type}`.toLowerCase();
    return !query || text.includes(query.toLowerCase());
  }), [graph.data?.activities, query]);
  const selectedNode = graph.data?.nodes.find(node =>
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

  if (graph.isLoading || employees.isLoading) return <LoadingState />;
  if (graph.error || employees.error) return <ErrorState message={(graph.error ?? employees.error)!.message} />;
  if (!graph.data) return <EmptyState title="No collaboration data" description="Start the company and send a message to build the graph." />;

  return (
    <Stack spacing={3}>
      <PageHeader
        title="Organization collaboration"
        description="Switch between the office, reporting chart, relationship network and process timeline. Every node drills into source events."
        actions={<Button onClick={() => setCreateOpen(true)}>Create group</Button>}
      />
      <Grid columns={4}>
        <Card title="Employees"><Text variant="h2">{graph.data.summary.employeeCount}</Text><Text variant="caption">Active organization members</Text></Card>
        <Card title="Groups"><Text variant="h2">{graph.data.summary.conversationCount}</Text><Text variant="caption">Company, project and topic rooms</Text></Card>
        <Card title="Collaborations"><Text variant="h2">{graph.data.summary.collaborationCount}</Text><Text variant="caption">Employee pairs with shared messages</Text></Card>
        <Card title="Traceable events"><Text variant="h2">{graph.data.summary.activityCount}</Text><FreshnessBadge freshness="fresh" /></Card>
      </Grid>
      <Card title="Workspace controls">
        <Stack direction="row" wrap spacing={2} alignItems="center">
          <SegmentedControl
            ariaLabel="Collaboration view"
            value={view}
            onChange={changeView}
            options={[
              { value: 'office', label: 'Office' },
              { value: 'org', label: 'Org chart' },
              { value: 'network', label: 'Network' },
              { value: 'timeline', label: 'Timeline' }
            ]}
          />
          <BoxSearch value={query} onChange={setQuery} />
          <Select label="Node type" value={nodeType} onChange={setNodeType} options={[
            { value: 'all', label: 'All nodes' },
            { value: 'employee', label: 'Employees' },
            { value: 'conversation', label: 'Groups' },
            { value: 'project', label: 'Projects' }
          ]} />
          <StatusChip label={`Updated ${new Date(graph.data.generatedAt).toLocaleTimeString()}`} tone="neutral" />
        </Stack>
      </Card>
      {view === 'office' ? <OfficeView nodes={filteredNodes} selectedId={selectedNode?.id} onSelect={selectNode} /> : null}
      {view === 'org' ? <OrgChartView nodes={filteredNodes} edges={filteredEdges} selectedId={selectedNode?.id} onSelect={selectNode} /> : null}
      {view === 'network' ? <NetworkView nodes={filteredNodes} edges={filteredEdges} selectedId={selectedNode?.id} onSelect={selectNode} /> : null}
      {view === 'timeline' ? <ActivityTimeline activities={filteredActivities} onActivitySelect={selectActivity} /> : null}
      <TracePanel node={selectedNode} employees={employees.data ?? []} onClose={() => {
        const next = new URLSearchParams(searchParams);
        next.delete('entityType');
        next.delete('entityId');
        setSearchParams(next);
      }} />
      <CreateGroupDialog
        open={createOpen}
        employees={employees.data ?? []}
        onClose={() => setCreateOpen(false)}
        onCreated={conversationId => {
          const next = new URLSearchParams(searchParams);
          next.set('entityType', 'conversation');
          next.set('entityId', conversationId);
          setSearchParams(next);
        }}
      />
    </Stack>
  );
}

function BoxSearch({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return <TextInput value={value} onChange={onChange} placeholder="Search people, groups, projects or events" inputProps={{ 'aria-label': 'Search collaboration' }} />;
}
