import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, ContentContainer, PageHeader, Stack, StatusChip, Text } from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { governanceRiskTone, governanceStatusTone } from './governancePresentation';

export function NotificationsPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['notifications'], queryFn: api.notifications });
  const action = useMutation({
    mutationFn: ({ id, verb }: { id: string; verb: 'read' | 'acknowledge' | 'archive' }) => api.notificationAction(id, verb),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] })
  });
  if (isLoading) return <LocalizedLoadingState />;
  if (error) return <LocalizedErrorState error={error} />;
  const notifications = data ?? [];
  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader title={t('governancePage.notifications')} />
        <LocalizedMutationFeedback mutation={action} successMessage={t('governancePage.notificationUpdated')} />
        <LocalizedDataTable
          rows={notifications}
          getRowId={item => item.id}
          density="compact"
          minWidth={820}
          ariaLabel={t('governancePage.notifications')}
          emptyTitle={t('governancePage.noNotifications')}
          columns={[
            { id: 'title', header: t('common.title'), render: item => <Text variant="bodyStrong" component="h3">{item.title}</Text> },
            { id: 'level', header: t('common.severity'), render: item => <StatusChip label={domainLabel(item.level)} tone={governanceRiskTone(item.level)} /> },
            { id: 'status', header: t('common.status'), render: item => <StatusChip label={domainLabel(item.status)} tone={governanceStatusTone(item.status)} /> },
            { id: 'createdAt', header: t('common.created'), render: item => format.dateTime(item.createdAt) },
            {
              id: 'actions',
              header: t('common.actions'),
              align: 'right',
              render: item => <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                <Button tone="neutral" size="small" onClick={() => action.mutate({ id: item.id, verb: 'acknowledge' })} loading={action.isPending && action.variables?.id === item.id && action.variables.verb === 'acknowledge'}>{t('governancePage.acknowledge')}</Button>
                <Button tone="neutral" size="small" onClick={() => action.mutate({ id: item.id, verb: 'archive' })} loading={action.isPending && action.variables?.id === item.id && action.variables.verb === 'archive'}>{t('common.archive')}</Button>
              </Stack>
            }
          ]}
        />
      </Stack>
    </ContentContainer>
  );
}
