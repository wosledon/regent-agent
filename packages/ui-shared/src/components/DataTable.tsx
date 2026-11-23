import type { ReactNode } from 'react';
import { Paper, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';

export interface DataColumn<T> {
  id: string;
  header: string;
  render: (row: T) => ReactNode;
}

export interface DataTableProps<T> {
  rows: readonly T[];
  columns: readonly DataColumn<T>[];
  getRowId: (row: T) => string;
}

export function DataTable<T>({ rows, columns, getRowId }: DataTableProps<T>) {
  return (
    <TableContainer component={Paper} variant="outlined">
      <Table size="small">
        <TableHead>
          <TableRow>{columns.map(column => <TableCell key={column.id}>{column.header}</TableCell>)}</TableRow>
        </TableHead>
        <TableBody>
          {rows.map(row => <TableRow key={getRowId(row)}>{columns.map(column => <TableCell key={column.id}>{column.render(row)}</TableCell>)}</TableRow>)}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
