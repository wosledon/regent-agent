import type { ReactNode } from 'react';
import { Paper, Skeleton, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import { EmptyState } from './EmptyState';
import { ErrorState } from './ErrorState';
import { designTokens } from '../theme/theme';

export interface DataColumn<T> {
  id: string;
  header: string;
  render: (row: T) => ReactNode;
  align?: 'left' | 'center' | 'right';
  width?: number | string;
  numeric?: boolean;
}

export type DataTableDensity = 'compact' | 'standard' | 'comfortable';

export interface DataTableProps<T> {
  rows: readonly T[];
  columns: readonly DataColumn<T>[];
  getRowId: (row: T, index: number) => string;
  emptyTitle?: string;
  emptyDescription?: string;
  emptyAction?: ReactNode;
  toolbar?: ReactNode;
  footer?: ReactNode;
  density?: DataTableDensity;
  loading?: boolean;
  error?: string;
  onRetry?: () => void;
  stickyHeader?: boolean;
  minWidth?: number;
  ariaLabel?: string;
}

export function DataTable<T>({
  rows,
  columns,
  getRowId,
  emptyTitle = 'No records',
  emptyDescription,
  emptyAction,
  toolbar,
  footer,
  density = 'standard',
  loading = false,
  error,
  onRetry,
  stickyHeader = true,
  minWidth = 720,
  ariaLabel
}: DataTableProps<T>) {
  const size = density === 'compact' ? 'small' : 'medium';
  const rowHeight = density === 'compact' ? 36 : density === 'comfortable' ? 52 : 44;
  return (
    <Paper variant="outlined" sx={{ borderRadius: designTokens.radius.md, overflow: 'hidden' }}>
      {toolbar ? <Stack sx={{ p: 1.5, borderBottom: 1, borderColor: 'divider', flexWrap: 'wrap', direction: 'row', gap: 1 }}>{toolbar}</Stack> : null}
      {error ? <Stack sx={{ p: 2 }}><ErrorState message={error} onRetry={onRetry} compact /></Stack> : loading ? (
        <TableContainer>
          <Table size={size} aria-label={ariaLabel} aria-busy="true">
            <TableHead>
              <TableRow>{columns.map(column => <TableCell key={column.id} style={{ width: column.width }} align={column.align}>{column.header}</TableCell>)}</TableRow>
            </TableHead>
            <TableBody>
              {Array.from({ length: 5 }, (_, rowIndex) => (
                <TableRow key={rowIndex} style={{ height: rowHeight }}>
                  {columns.map(column => <TableCell key={column.id}><Skeleton variant="text" width={`${55 + ((rowIndex + column.id.length) % 4) * 10}%`} /></TableCell>)}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      ) : rows.length === 0 ? (
        <EmptyState title={emptyTitle} description={emptyDescription} action={emptyAction} compact />
      ) : (
        <TableContainer>
          <Table size={size} stickyHeader={stickyHeader} aria-label={ariaLabel} sx={{ minWidth }}>
            <TableHead>
              <TableRow>{columns.map(column => <TableCell key={column.id} style={{ width: column.width }} align={column.align ?? (column.numeric ? 'right' : 'left')}>{column.header}</TableCell>)}</TableRow>
            </TableHead>
            <TableBody>
              {rows.map((row, index) => (
                <TableRow key={getRowId(row, index)} hover sx={{ height: rowHeight }}>
                  {columns.map(column => <TableCell key={column.id} align={column.align ?? (column.numeric ? 'right' : 'left')} sx={column.numeric ? { fontVariantNumeric: 'tabular-nums' } : undefined}>{column.render(row)}</TableCell>)}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      {footer ? <Stack sx={{ px: 2, py: 1.25, borderTop: 1, borderColor: 'divider', color: 'text.secondary' }}>{footer}</Stack> : null}
    </Paper>
  );
}
