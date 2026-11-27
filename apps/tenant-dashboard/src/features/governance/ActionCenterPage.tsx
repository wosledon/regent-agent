import { useQuery } from '@tanstack/react-query';
import { ContentContainer, PageHeader, SemanticLink, Stack, StatusChip, Text } from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { resolveActionCenterNextAction } from '../../lib/dashboardPresentation';
import { governanceRiskTone } from './governancePresentation';

export function ActionCenterPage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const { data, isLoading, error } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });
  if (isLoading) return <LocalizedLoadingState />;
  if (error) return <LocalizedErrorState error={error} />;
  const actions = data?.actions ?? [];
  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader title={t('governancePage.actionCenter')} />
        <LocalizedDataTable
          rows={actions}
          getRowId={action => `${action.sourceType}-${action.id}`}
          density="compact"
          minWidth={920}
          ariaLabel={t('governancePage.actionCenter')}
          emptyTitle={t('governancePage.noOpenActions')}
          emptyDescription={t('governancePage.theCompanyHasNoPendingEscalations')}
          columns={[
            {
              id: 'title',
              header: t('common.action'),
              render: action => <Stack spacing={0.5}>
                <Text variant="bodyStrong" component="h3">{action.title}</Text>
                {action.requiresBossDecision ? <StatusChip label={t('common.decision')} tone="info" sx={{ alignSelf: 'flex-start' }} /> : null}
              </Stack>
            },
            { id: 'severity', header: t('common.severity'), render: action => <StatusChip label={domainLabel(action.severity)} tone={governanceRiskTone(action.severity)} /> },
            { id: 'source', header: t('common.category'), render: action => domainLabel(action.sourceType) },
            { id: 'owner', header: t('common.role'), render: action => action.owner },
            { id: 'nextAction', header: t('common.action'), render: action => <Text variant="secondary">{resolveActionCenterNextAction(action, t)}</Text> },
            { id: 'actions', header: t('common.actions'), align: 'right', render: action => <SemanticLink href={action.href}>{t('common.open')}</SemanticLink> }
          ]}
        />
      </Stack>
    </ContentContainer>
  );
}
