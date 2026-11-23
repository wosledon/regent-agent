import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Button, Card, DataTable, ErrorState, LoadingState, PageHeader, Select, Stack, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../lib/api-client/api';
import { useAuth } from '../app/useAuth';
import i18n from '../lib/i18n';

export function SettingsPage() {
  const { t, i18n: translator } = useTranslation();
  const { session, switchTenant } = useAuth();
  const queryClient = useQueryClient();
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants });
  const changeTenant = useMutation({
    mutationFn: switchTenant,
    onSuccess: async () => {
      await queryClient.invalidateQueries();
      window.location.assign('/');
    }
  });
  if (tenants.isLoading) return <LoadingState />;
  if (tenants.error) return <ErrorState message={tenants.error.message} />;
  return (
    <Stack spacing={3}>
      <PageHeader title={t('settings')} description="Language, tenant context and local notification preferences." />
      <Card title="Preferences">
        <Select label="Language" value={translator.language} onChange={language => void i18n.changeLanguage(language)} options={[
          { value: 'zh-CN', label: '简体中文' }, { value: 'en-US', label: 'English' }
        ]} />
        <Text>Timezone: Asia/Shanghai</Text>
        <Text>Notifications: dashboard only</Text>
      </Card>
      <Card title="Tenant ownership">
        <Text variant="caption">Current tenant: {session?.tenantId ?? 'legacy session'}</Text>
        <DataTable rows={tenants.data ?? []} getRowId={row => row.id} columns={[
          { id: 'name', header: 'Tenant', render: row => row.name },
          { id: 'company', header: 'Company', render: row => row.companyName },
          { id: 'status', header: 'Status', render: row => <StatusChip label={row.status} tone={row.status === 'Active' ? 'success' : 'warning'} /> },
          { id: 'actions', header: 'Actions', render: row => <Button onClick={() => changeTenant.mutate(row.id)} disabled={row.id === session?.tenantId} loading={changeTenant.isPending}>Switch</Button> }
        ]} />
      </Card>
    </Stack>
  );
}
