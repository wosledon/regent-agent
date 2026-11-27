import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppThemeProvider } from '@regent/ui-shared';
import i18n from '../../lib/i18n';
import { LocalizedDataTable } from './LocalizedDataTable';

interface Row {
  id: string;
  name: string;
}

const rows: Row[] = [{ id: 'row-1', name: 'First record' }];
const columns = [{ id: 'name', header: 'Name', render: (row: Row) => row.name }];
const getRowId = (row: Row) => row.id;

function renderTable(emptyTitle?: string) {
  return render(
    <AppThemeProvider>
      <LocalizedDataTable rows={[]} columns={columns} getRowId={getRowId} emptyTitle={emptyTitle} />
    </AppThemeProvider>
  );
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('LocalizedDataTable', () => {
  it.each([
    ['en-US', 'No records'],
    ['zh-CN', '暂无记录']
  ] as const)('localizes the default empty state in %s', async (locale, expected) => {
    await i18n.changeLanguage(locale);
    renderTable();
    expect(screen.getByRole('heading', { name: expected })).toBeInTheDocument();
  });

  it.each([
    ['en-US', 'No projects'],
    ['zh-CN', '暂无项目']
  ] as const)('preserves overridden empty copy in %s', async (locale, emptyTitle) => {
    await i18n.changeLanguage(locale);
    renderTable(emptyTitle);
    expect(screen.getByRole('heading', { name: emptyTitle })).toBeInTheDocument();
  });

  it.each([
    ['en-US', 'Not available'],
    ['zh-CN', '暂无数据']
  ] as const)('replaces blank data cells with a localized fallback in %s', async (locale, expected) => {
    await i18n.changeLanguage(locale);
    render(
      <AppThemeProvider>
        <LocalizedDataTable
          rows={rows}
          getRowId={getRowId}
          columns={[
            { id: 'name', header: 'Name', render: () => '' },
            { id: 'actions', header: 'Actions', render: () => null }
          ]}
        />
      </AppThemeProvider>
    );
    expect(screen.getByRole('cell', { name: expected })).toBeInTheDocument();
    expect(screen.getAllByRole('cell', { name: expected })).toHaveLength(1);
  });

  it('preserves row IDs and table behavior', () => {
    const identifyRow = vi.fn(getRowId);
    render(
      <AppThemeProvider>
        <LocalizedDataTable rows={rows} columns={columns} getRowId={identifyRow} />
      </AppThemeProvider>
    );
    expect(screen.getByRole('row', { name: 'First record' })).toBeInTheDocument();
    expect(identifyRow).toHaveBeenCalledWith(rows[0], 0);
    expect(screen.queryByText('No records')).not.toBeInTheDocument();
  });
});
