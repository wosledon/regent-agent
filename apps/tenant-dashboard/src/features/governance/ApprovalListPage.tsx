import { useQuery } from '@tanstack/react-query';
import { ContentContainer, PageHeader, SemanticLink, Stack, StatusChip, Text } from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { resolveActionCenterNextAction } from '../../lib/dashboardPresentation';
import { useTranslation } from 'react-i18next';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { governanceRiskTone } from './governancePresentation';

export function ApprovalListPage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const { data, isLoading, error, refetch } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });
  if (isLoading) return <LocalizedLoadingState label={t('governancePage.loadingApprovals')} />;
  if (error) return <LocalizedErrorState error={error} onRetry={() => void refetch()} />;
  const approvals = data?.actions.filter(action => action.sourceType === 'Approval') ?? [];
  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader title={t('governancePage.approvals')} description={t('governancePage.reviewPendingHighImpactDecisionsAndTheirEvidence')} />
        <LocalizedDataTable
          rows={approvals}
          getRowId={approval => approval.id}
          density="compact"
          minWidth={760}
          ariaLabel={t('governancePage.approvals')}
          emptyTitle={t('governancePage.noPendingApprovals')}
          emptyDescription={t('governancePage.newApprovalRequestsWillAppearHere')}
          columns={[
            { id: 'title', header: t('common.title'), render: approval => <Text variant="bodyStrong">{approval.title}</Text> },
            { id: 'severity', header: t('common.severity'), render: approval => <StatusChip label={domainLabel(approval.severity)} tone={governanceRiskTone(approval.severity)} /> },
            { id: 'nextAction', header: t('common.action'), render: approval => <Text variant="secondary">{resolveActionCenterNextAction(approval, t)}</Text> },
            { id: 'actions', header: t('common.actions'), align: 'right', render: approval => <SemanticLink href={approval.href}>{t('governancePage.reviewApproval')}</SemanticLink> }
          ]}
        />
      </Stack>
    </ContentContainer>
  );
}
