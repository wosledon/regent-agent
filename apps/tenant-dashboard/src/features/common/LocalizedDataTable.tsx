import { useTranslation } from 'react-i18next';
import { DataTable, type DataTableProps } from '@regent/ui-shared';

export function LocalizedDataTable<T>({ emptyTitle, columns, ...props }: DataTableProps<T>) {
  const { t } = useTranslation();
  const localizedColumns = columns.map(column => ({
    ...column,
    render: (row: T) => {
      const rendered = column.render(row);
      const actionColumn = column.id === 'action' || column.id === 'actions';
      if (actionColumn || (rendered !== null && rendered !== undefined && !(typeof rendered === 'string' && !rendered.trim()))) {
        return rendered;
      }
      return t('common.notAvailable');
    }
  }));
  return <DataTable {...props} columns={localizedColumns} emptyTitle={emptyTitle ?? t('common.noRecords')} />;
}
