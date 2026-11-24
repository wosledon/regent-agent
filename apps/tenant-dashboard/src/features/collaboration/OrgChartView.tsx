import { GraphCanvas, type GraphCanvasEdge, type GraphCanvasNode } from '@regent/ui-shared';
import type { CollaborationEdge, CollaborationNode } from '../../lib/domain-types';

interface OrgChartViewProps {
  nodes: CollaborationNode[];
  edges: CollaborationEdge[];
  selectedId?: string | null;
  onSelect: (node: CollaborationNode) => void;
}

export function OrgChartView({ nodes, edges, selectedId, onSelect }: OrgChartViewProps) {
  const employees = nodes.filter(node => node.type === 'Employee');
  const byId = new Map(employees.map(node => [node.id, node]));
  const children = new Map<string, CollaborationNode[]>();
  for (const employee of employees) {
    const parentNodeId = employee.parentId ? `employee:${employee.parentId}` : null;
    if (!parentNodeId || !byId.has(parentNodeId)) continue;
    children.set(parentNodeId, [...(children.get(parentNodeId) ?? []), employee]);
  }
  const roots = employees.filter(node => !node.parentId || !byId.has(`employee:${node.parentId}`));
  const positions = new Map<string, { x: number; y: number }>();
  const visited = new Set<string>();
  let leaf = 0;
  const place = (node: CollaborationNode, depth: number) => {
    if (visited.has(node.id)) return;
    visited.add(node.id);
    const descendants = children.get(node.id) ?? [];
    if (!descendants.length) {
      positions.set(node.id, { x: 60 + leaf * 250, y: 60 + depth * 170 });
      leaf++;
      return;
    }
    descendants.forEach(child => place(child, depth + 1));
    const childPositions = descendants.map(child => positions.get(child.id)!).filter(Boolean);
    positions.set(node.id, {
      x: childPositions.reduce((sum, position) => sum + position.x, 0) / childPositions.length,
      y: 60 + depth * 170
    });
  };
  roots.forEach(root => place(root, 0));
  employees.filter(node => !visited.has(node.id)).forEach(node => place(node, 0));

  const canvasNodes: GraphCanvasNode[] = employees.map(node => ({
    id: node.id,
    label: node.label,
    subtitle: node.subtitle,
    status: node.status,
    kind: 'Employee',
    ...(positions.get(node.id) ?? { x: node.x, y: node.y })
  }));
  const canvasEdges: GraphCanvasEdge[] = edges.filter(edge => edge.kind === 'reports_to').map(edge => ({
    id: edge.id, source: edge.sourceId, target: edge.targetId, kind: edge.kind, weight: edge.weight, label: edge.label
  }));
  return (
    <GraphCanvas
      nodes={canvasNodes}
      edges={canvasEdges}
      selectedId={selectedId}
      onSelect={canvasNode => {
        const node = employees.find(employee => employee.id === canvasNode.id);
        if (node) onSelect(node);
      }}
      height={Math.max(480, (roots.length + leaf) * 120)}
    />
  );
}
