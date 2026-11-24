import { useId, useMemo, useState } from 'react';
import { Box, IconButton, Tooltip, useTheme } from '@mui/material';
import { Icon } from './Icon';

export interface GraphCanvasNode {
  id: string;
  label: string;
  subtitle: string;
  x: number;
  y: number;
  kind: 'Employee' | 'Conversation' | 'Project';
  status: string;
}

export interface GraphCanvasEdge {
  id: string;
  source: string;
  target: string;
  kind: string;
  weight: number;
  label: string;
}

export interface GraphCanvasProps {
  nodes: ReadonlyArray<GraphCanvasNode>;
  edges: ReadonlyArray<GraphCanvasEdge>;
  selectedId?: string | null;
  onSelect: (node: GraphCanvasNode) => void;
  height?: number;
}

export function GraphCanvas({ nodes, edges, selectedId, onSelect, height = 560 }: GraphCanvasProps) {
  const [zoom, setZoom] = useState(1);
  const theme = useTheme();
  const markerId = `arrow-${useId().replace(/:/g, '')}`;
  const bounds = useMemo(() => ({
    minX: Math.min(0, ...nodes.map(node => node.x - 120)),
    minY: Math.min(0, ...nodes.map(node => node.y - 80)),
    maxX: Math.max(900, ...nodes.map(node => node.x + 240)),
    maxY: Math.max(520, ...nodes.map(node => node.y + 140))
  }), [nodes]);
  const byId = new Map(nodes.map(node => [node.id, node]));

  return (
    <Box sx={{ position: 'relative', width: '100%', overflow: 'auto', border: 1, borderColor: 'divider', borderRadius: 2, bgcolor: 'background.default' }}>
      <Box sx={{ position: 'absolute', right: 12, top: 12, zIndex: 1, display: 'flex', gap: 1, bgcolor: 'background.paper', borderRadius: 1, p: 0.5 }}>
        <Tooltip title="Zoom out"><IconButton aria-label="Zoom out" onClick={() => setZoom(value => Math.max(0.6, value - 0.15))}><Icon name="search" /></IconButton></Tooltip>
        <Tooltip title="Zoom in"><IconButton aria-label="Zoom in" onClick={() => setZoom(value => Math.min(1.8, value + 0.15))}><Icon name="organization" /></IconButton></Tooltip>
        <Tooltip title="Reset zoom"><IconButton aria-label="Reset zoom" onClick={() => setZoom(1)}><Icon name="dashboard" /></IconButton></Tooltip>
      </Box>
      <svg
        viewBox={`${bounds.minX} ${bounds.minY} ${bounds.maxX - bounds.minX} ${bounds.maxY - bounds.minY}`}
        width="100%"
        height={height}
        role="img"
        aria-label="Organization collaboration graph"
        style={{ minWidth: 760, transform: `scale(${zoom})`, transformOrigin: 'top left' }}
      >
        <defs>
          <marker id={markerId} markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z" fill={theme.palette.text.disabled} /></marker>
        </defs>
        {edges.map(edge => {
          const source = byId.get(edge.source);
          const target = byId.get(edge.target);
          if (!source || !target) return null;
          const width = edge.kind === 'collaborates' ? Math.min(6, 1 + edge.weight * 0.6) : 1.5;
          return (
            <g key={edge.id}>
              <line x1={source.x + 90} y1={source.y + 42} x2={target.x + 90} y2={target.y + 42} stroke={theme.palette.text.disabled} strokeWidth={width} strokeDasharray={edge.kind === 'reports_to' ? '6 4' : undefined} markerEnd={`url(#${markerId})`} opacity={edge.kind === 'collaborates' ? 0.8 : 0.55} />
              {edge.kind === 'collaborates' && edge.weight > 1 ? <text x={(source.x + target.x) / 2 + 90} y={(source.y + target.y) / 2 + 34} textAnchor="middle" fontSize="12" fill={theme.palette.text.secondary}>{edge.weight}</text> : null}
            </g>
          );
        })}
        {nodes.map(node => {
          const selected = selectedId === node.id;
          const fill = node.kind === 'Employee' ? theme.palette.primary.light : node.kind === 'Conversation' ? theme.palette.warning.light : theme.palette.success.light;
          const stroke = selected ? theme.palette.primary.main : theme.palette.text.disabled;
          return (
            <g
              key={node.id}
              role="button"
              tabIndex={0}
              aria-label={`${node.label}, ${node.subtitle}`}
              onClick={() => onSelect(node)}
              onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onSelect(node); } }}
              style={{ cursor: 'pointer' }}
            >
              <rect x={node.x} y={node.y} width="180" height="84" rx="14" fill={fill} stroke={stroke} strokeWidth={selected ? 3 : 1.5} />
              <circle cx={node.x + 22} cy={node.y + 22} r="8" fill={node.status === 'Busy' ? theme.palette.warning.main : node.status === 'Blocked' ? theme.palette.error.main : theme.palette.success.main} />
              <text x={node.x + 38} y={node.y + 27} fontSize="14" fontWeight="700" fill={theme.palette.text.primary}>{node.label.slice(0, 22)}</text>
              <text x={node.x + 16} y={node.y + 52} fontSize="11" fill={theme.palette.text.secondary}>{node.subtitle.slice(0, 30)}</text>
              <text x={node.x + 16} y={node.y + 70} fontSize="10" fill={theme.palette.text.secondary}>{node.kind} · {node.status}</text>
            </g>
          );
        })}
      </svg>
    </Box>
  );
}
