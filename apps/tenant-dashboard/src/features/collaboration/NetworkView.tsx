import { useState } from 'react';
import { GraphCanvas, Select, Stack, type GraphCanvasEdge, type GraphCanvasNode } from '@regent/ui-shared';
import type { CollaborationEdge, CollaborationNode } from '../../lib/domain-types';

interface NetworkViewProps {
  nodes: CollaborationNode[];
  edges: CollaborationEdge[];
  selectedId?: string | null;
  onSelect: (node: CollaborationNode) => void;
}

export function NetworkView({ nodes, edges, selectedId, onSelect }: NetworkViewProps) {
  const [focus, setFocus] = useState('all');
  const canvasNodes: GraphCanvasNode[] = nodes.map(node => ({
    id: node.id, label: node.label, subtitle: node.subtitle, status: node.status,
    kind: node.type, x: node.x, y: node.y
  }));
  const canvasEdges: GraphCanvasEdge[] = edges
    .filter(edge => focus === 'all' || (focus === 'collaboration' ? edge.kind === 'collaborates' : edge.kind === 'reports_to' || edge.kind === 'works_on'))
    .map(edge => ({ id: edge.id, source: edge.sourceId, target: edge.targetId, kind: edge.kind, weight: edge.weight, label: edge.label }));
  return (
    <Stack spacing={2}>
      <Select
        label="Relationship focus"
        value={focus}
        onChange={setFocus}
        options={[
          { value: 'all', label: 'All relationships' },
          { value: 'collaboration', label: 'Employee collaboration' },
          { value: 'structure', label: 'Reporting and project work' }
        ]}
      />
      <GraphCanvas nodes={canvasNodes} edges={canvasEdges} selectedId={selectedId} onSelect={canvasNode => {
        const node = nodes.find(candidate => candidate.id === canvasNode.id);
        if (node) onSelect(node);
      }} height={640} />
    </Stack>
  );
}
