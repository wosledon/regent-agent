import { Card, EmptyState, StatusChip, type GraphCanvasEdge } from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { LocalizedGraphCanvas, type LocalizedGraphCanvasNode } from './LocalizedGraphCanvas';
import type { CollaborationEdge, CollaborationNode } from '../../lib/domain-types';
import type { CollaborationDisplayNode } from './localizedCollaboration';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { localizedCount } from '../../lib/localizedCount';

interface OrgChartViewProps {
  nodes: CollaborationDisplayNode[];
  edges: CollaborationEdge[];
  selectedId?: string | null;
  onSelect: (node: CollaborationNode) => void;
}

export function OrgChartView({ nodes, edges, selectedId, onSelect }: OrgChartViewProps) {
  const { t } = useTranslation();
  const format = useTenantFormatting();
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

  const canvasNodes: LocalizedGraphCanvasNode[] = employees.map(node => ({
    id: node.id,
    label: node.label,
    subtitle: node.subtitle,
    sourceSubtitle: node.sourceSubtitle,
    status: node.status,
    kind: 'Employee',
    ...(positions.get(node.id) ?? { x: node.x, y: node.y })
  }));
  const canvasEdges: GraphCanvasEdge[] = edges.filter(edge => edge.kind === 'reports_to').map(edge => ({
    id: edge.id, source: edge.sourceId, target: edge.targetId, kind: edge.kind, weight: edge.weight, label: edge.label
  }));
  if (!employees.length) {
    return <Card><EmptyState title={t('collaborationPage.noEmployeesYet')} description={t('collaborationPage.startTheCompanyToOpenTheOffice')} /></Card>;
  }

  return (
    <Card
      title={t('collaborationPage.orgChart')}
      description={t('collaborationPage.reportingAndProjectWork')}
      headingLevel="h2"
      action={<StatusChip label={localizedCount(t, format, 'collaborationPage.employeeCount', employees.length)} tone="info" />}
      padding="small"
    >
      <LocalizedGraphCanvas
        nodes={canvasNodes}
        edges={canvasEdges}
        selectedId={selectedId}
        onSelect={canvasNode => {
          const node = employees.find(employee => employee.id === canvasNode.id);
          if (node) onSelect(node);
        }}
        height={Math.max(480, (roots.length + leaf) * 120)}
      />
    </Card>
  );
}
