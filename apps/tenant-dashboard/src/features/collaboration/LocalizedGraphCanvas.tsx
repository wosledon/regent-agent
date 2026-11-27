import { useId, useMemo, useState } from 'react';
import { Box, useTheme } from '@mui/material';
import { useTranslation } from 'react-i18next';
import {
  Icon, SemanticIconButton, Stack, Text, Tooltip, type GraphCanvasEdge, type GraphCanvasNode
} from '@regent/ui-shared';
import { useDomainLabels } from '../../lib/domainLabels';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { localizeCollaborationSubtitle } from './localizedCollaboration';

export type LocalizedGraphCanvasNode = GraphCanvasNode & { sourceSubtitle?: string };

interface LocalizedGraphCanvasProps {
  nodes: ReadonlyArray<LocalizedGraphCanvasNode>;
  edges: ReadonlyArray<GraphCanvasEdge>;
  selectedId?: string | null;
  onSelect: (node: LocalizedGraphCanvasNode) => void;
  height?: number;
}

export function LocalizedGraphCanvas({ nodes, edges, selectedId, onSelect, height = 560 }: LocalizedGraphCanvasProps) {
  const [zoom, setZoom] = useState(1);
  const [focusedId, setFocusedId] = useState<string | null>(null);
  const theme = useTheme();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const markerId = `arrow-${useId().replace(/:/g, '')}`;
  const legendId = `graph-legend-${useId().replace(/:/g, '')}`;
  const bounds = useMemo(() => ({
    minX: Math.min(0, ...nodes.map(node => node.x - 120)),
    minY: Math.min(0, ...nodes.map(node => node.y - 80)),
    maxX: Math.max(900, ...nodes.map(node => node.x + 240)),
    maxY: Math.max(520, ...nodes.map(node => node.y + 140))
  }), [nodes]);
  const byId = new Map(nodes.map(node => [node.id, node]));
  const localization = useMemo(() => ({ t, format, domainLabel }), [t, format, domainLabel]);

  return (
    <Box
      role="group"
      aria-label={t('collaborationPage.graphLabel')}
      aria-describedby={legendId}
      sx={{ position: 'relative', width: '100%', overflow: 'hidden', border: 1, borderColor: 'divider', borderRadius: 2, bgcolor: 'background.default' }}
    >
      <Box id={legendId} sx={{ borderBottom: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
        <Stack direction="row" wrap spacing={2} alignItems="center" sx={{ px: 2, py: 1.25 }}>
          <Stack direction="row" spacing={0.75} alignItems="center">
            <Box component="span" sx={{ width: 10, height: 10, borderRadius: '2px', bgcolor: 'primary.main' }} />
            <Text variant="caption">{t('collaborationPage.employees')}</Text>
          </Stack>
          <Stack direction="row" spacing={0.75} alignItems="center">
            <Box component="span" sx={{ width: 10, height: 10, borderRadius: '2px', bgcolor: 'warning.main' }} />
            <Text variant="caption">{t('collaborationPage.groups')}</Text>
          </Stack>
          <Stack direction="row" spacing={0.75} alignItems="center">
            <Box component="span" sx={{ width: 10, height: 10, borderRadius: '2px', bgcolor: 'success.main' }} />
            <Text variant="caption">{t('collaborationPage.projects')}</Text>
          </Stack>
          <Stack direction="row" spacing={0.75} alignItems="center">
            <Box component="span" sx={{ width: 18, borderTop: '2px solid', borderColor: 'primary.main' }} />
            <Text variant="caption">{t('collaborationPage.employeeCollaboration')}</Text>
          </Stack>
          <Stack direction="row" spacing={0.75} alignItems="center">
            <Box component="span" sx={{ width: 18, borderTop: '2px dashed', borderColor: 'text.disabled' }} />
            <Text variant="caption">{t('collaborationPage.reportingAndProjectWork')}</Text>
          </Stack>
        </Stack>
      </Box>
      <Box sx={{ position: 'relative', width: '100%', overflow: 'auto' }}>
        <Stack
          direction="row"
          spacing={0.5}
          alignItems="center"
          sx={{ position: 'absolute', right: 12, top: 12, zIndex: 1, bgcolor: 'background.paper', border: 1, borderColor: 'divider', borderRadius: 1, px: 0.5, py: 0.25 }}
        >
          <Tooltip title={t('collaborationPage.zoomOut')}><SemanticIconButton label={t('collaborationPage.zoomOut')} onClick={() => setZoom(value => Math.max(0.6, value - 0.15))} disabled={zoom <= 0.6}>−</SemanticIconButton></Tooltip>
          <Text variant="caption" sx={{ minWidth: 42, textAlign: 'center', fontVariantNumeric: 'tabular-nums' }}>{format.percent(zoom, { maximumFractionDigits: 0 })}</Text>
          <Tooltip title={t('collaborationPage.zoomIn')}><SemanticIconButton label={t('collaborationPage.zoomIn')} onClick={() => setZoom(value => Math.min(1.8, value + 0.15))} disabled={zoom >= 1.8}>+</SemanticIconButton></Tooltip>
          <Tooltip title={t('collaborationPage.resetZoom')}><SemanticIconButton label={t('collaborationPage.resetZoom')} onClick={() => setZoom(1)}><Icon name="refresh" /></SemanticIconButton></Tooltip>
        </Stack>
        <svg
          viewBox={`${bounds.minX} ${bounds.minY} ${bounds.maxX - bounds.minX} ${bounds.maxY - bounds.minY}`}
          width="100%"
          height={height}
          role="group"
          aria-label={t('collaborationPage.graphLabel')}
          style={{ minWidth: 760, display: 'block', transform: `scale(${zoom})`, transformOrigin: 'top left' }}
        >
          <defs>
            <marker id={markerId} markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z" fill={theme.palette.text.disabled} /></marker>
          </defs>
          {edges.map(edge => {
            const source = byId.get(edge.source);
            const target = byId.get(edge.target);
            if (!source || !target) return null;
            const width = edge.kind === 'collaborates' ? Math.min(6, 1 + edge.weight * 0.6) : 1.5;
            const stroke = edge.kind === 'collaborates'
              ? theme.palette.primary.main
              : edge.kind === 'works_on' ? theme.palette.success.main : theme.palette.text.disabled;
            return (
              <g key={edge.id}>
                <line
                  x1={source.x + 90}
                  y1={source.y + 42}
                  x2={target.x + 90}
                  y2={target.y + 42}
                  stroke={stroke}
                  strokeWidth={width}
                  strokeDasharray={edge.kind === 'reports_to' ? '6 4' : undefined}
                  markerEnd={`url(#${markerId})`}
                  opacity={edge.kind === 'collaborates' ? 0.75 : 0.55}
                />
                {edge.kind === 'collaborates' && edge.weight > 1 ? (
                  <text x={(source.x + target.x) / 2 + 90} y={(source.y + target.y) / 2 + 34} textAnchor="middle" fontSize="11" fontWeight={600} fill={theme.palette.text.secondary}>{format.number(edge.weight)}</text>
                ) : null}
              </g>
            );
          })}
          {nodes.map(node => {
            const selected = selectedId === node.id;
            const focused = focusedId === node.id;
            const display = localizeCollaborationSubtitle(node.kind, node.sourceSubtitle ?? node.subtitle, localization);
            const kindColor = node.kind === 'Employee'
              ? theme.palette.primary.main
              : node.kind === 'Conversation' ? theme.palette.warning.main : theme.palette.success.main;
            const statusColor = node.status === 'Busy'
              ? theme.palette.warning.main
              : node.status === 'Blocked' ? theme.palette.error.main : theme.palette.success.main;
            return (
              <g
                key={node.id}
                role="button"
                tabIndex={0}
                aria-label={t('collaborationPage.graphNodeLabel', { name: node.label, subtitle: display.subtitle })}
                aria-pressed={selected}
                onClick={() => onSelect(node)}
                onFocus={() => setFocusedId(node.id)}
                onBlur={() => setFocusedId(null)}
                onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onSelect(node); } }}
                style={{ cursor: 'pointer', outline: 'none' }}
              >
                <rect x={node.x} y={node.y} width="180" height="84" rx="8" fill={selected ? theme.palette.primary.light : theme.palette.background.paper} stroke={selected ? theme.palette.primary.main : focused ? theme.palette.info.main : theme.palette.divider} strokeWidth={selected || focused ? 2 : 1} />
                <rect x={node.x} y={node.y + 8} width="4" height="68" rx="2" fill={kindColor} />
                <circle cx={node.x + 22} cy={node.y + 22} r="7" fill={statusColor} />
                <text x={node.x + 38} y={node.y + 27} fontSize="13" fontWeight="700" fill={theme.palette.text.primary}>{node.label.slice(0, 22)}</text>
                <text x={node.x + 16} y={node.y + 52} fontSize="11" fill={theme.palette.text.secondary}>{display.subtitle.slice(0, 30)}</text>
                <text x={node.x + 16} y={node.y + 70} fontSize="10" fill={theme.palette.text.secondary}>{t('collaborationPage.nodeStatus', { type: domainLabel(node.kind), status: domainLabel(node.status) })}</text>
              </g>
            );
          })}
        </svg>
      </Box>
    </Box>
  );
}
