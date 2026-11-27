import { Card, ContentContainer, EmptyState, PageHeader, Stack } from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';

export function IncidentsPage() {
  const { t } = useTranslation();
  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader title={t('governancePage.securityIncidents')} description={t('governancePage.localIncidentRecordsWillAppearHere')} />
        <Card padding="small">
          <EmptyState title={t('governancePage.noOpenIncidents')} description={t('governancePage.localIncidentRecordsWillAppearHere')} compact />
        </Card>
      </Stack>
    </ContentContainer>
  );
}
