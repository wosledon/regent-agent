import { useState } from 'react';
import { Card, EmptyState, FilterBar, Select, Stack, StatusChip, type GraphCanvasEdge } from '@regent/ui-shared';
import { LocalizedGraphCanvas, type LocalizedGraphCanvasNode } from './LocalizedGraphCanvas';
import { useTranslation } from 'react-i18next';
import type { CollaborationEdge, CollaborationNode } from '../../lib/domain-types';
import type { CollaborationDisplayNode } from './localizedCollaboration';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';

interface NetworkViewProps {
  nodes: CollaborationDisplayNode[];
  edges: CollaborationEdge[];
  selectedId?: string | null;
  onSelect: (node: CollaborationNode) => void;
}

export function NetworkView({ nodes, edges, selectedId, onSelect }: NetworkViewProps) {
  const { t } = useTranslation();
  const format = useTenantFormatting();
  const [focus, setFocus] = useState('all');
  const canvasNodes: LocalizedGraphCanvasNode[] = nodes.map(node => ({
    id: node.id, label: node.label, subtitle: node.subtitle, sourceSubtitle: node.sourceSubtitle, status: node.status,
    kind: node.type, x: node.x, y: node.y
  }));
  const canvasEdges: GraphCanvasEdge[] = edges
    .filter(edge => focus === 'all' || (focus === 'collaboration' ? edge.kind === 'collaborates' : edge.kind === 'reports_to' || edge.kind === 'works_on'))
    .map(edge => ({ id: edge.id, source: edge.sourceId, target: edge.targetId, kind: edge.kind, weight: edge.weight, label: edge.label }));
  const focusLabel = focus === 'collaboration'
    ? t('collaborationPage.employeeCollaboration')
    : focus === 'structure' ? t('collaborationPage.reportingAndProjectWork') : t('collaborationPage.allRelationships');
  if (!nodes.length) {
    return <Card><EmptyState title={t('collaborationPage.noCollaborationData')} description={t('collaborationPage.startTheCompanyAndSendAMessageToBuildTheGraph')} /></Card>;
  }

  return (
    <Card
      title={t('collaborationPage.network')}
      description={t('collaborationPage.employeePairsWithSharedMessages')}
      headingLevel="h2"
      action={<StatusChip label={`${format.number(nodes.length)} · ${format.number(canvasEdges.length)}`} tone="info" />}
      padding="small"
    >
      <FilterBar action={<StatusChip label={focusLabel} tone="neutral" />}>
        <Stack sx={{ width: { xs: '100%', sm: 280 } }}>
          <Select
            label={t('collaborationPage.relationshipFocus')}
            value={focus}
            onChange={setFocus}
            options={[
              { value: 'all', label: t('collaborationPage.allRelationships') },
              { value: 'collaboration', label: t('collaborationPage.employeeCollaboration') },
              { value: 'structure', label: t('collaborationPage.reportingAndProjectWork') }
            ]}
          />
        </Stack>
      </FilterBar>
      <LocalizedGraphCanvas nodes={canvasNodes} edges={canvasEdges} selectedId={selectedId} onSelect={canvasNode => {
        const node = nodes.find(candidate => candidate.id === canvasNode.id);
        if (node) onSelect(node);
      }} height={640} />
    </Card>
  );
}
