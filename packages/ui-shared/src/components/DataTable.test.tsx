import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DataTable } from './DataTable';

describe('DataTable', () => {
  it('renders a shared empty state instead of an empty table', () => {
    render(<DataTable<{ id: string; name: string }> rows={[]} columns={[{ id: 'name', header: 'Name', render: row => row.name }]} getRowId={row => row.id} emptyTitle="No projects" emptyDescription="Create a project first." />);
    expect(screen.getByRole('heading', { name: 'No projects' })).toBeInTheDocument();
    expect(screen.getByText('Create a project first.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('renders rows with stable IDs when data exists', () => {
    render(<DataTable rows={[{ id: 'project-1', name: 'Project A' }]} columns={[{ id: 'name', header: 'Name', render: row => row.name }]} getRowId={row => row.id} />);
    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Project A' })).toBeInTheDocument();
  });
});
