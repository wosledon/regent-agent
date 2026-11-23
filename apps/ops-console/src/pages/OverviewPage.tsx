import { useQuery } from '@tanstack/react-query';
import { Card, EmptyState, ErrorState, Grid, LoadingState, PageHeader, Stack, Text } from '@regent/ui-shared';
import { api } from '../lib/api';

export function OverviewPage() {
  const tenants = useQuery({ queryKey: ['ops-tenants'], queryFn: api.tenants });
  const grants = useQuery({ queryKey: ['ops-grants'], queryFn: api.grants });
  if (tenants.isLoading || grants.isLoading) return <LoadingState />;
  if (tenants.error || grants.error) return <ErrorState message={(tenants.error ?? grants.error)!.message} />;
  if (!tenants.data?.length) return <EmptyState title="No tenants" description="Provision the first local tenant." />;
  return <Stack spacing={3}>
    <PageHeader title="Operations Overview" description="Local tenant, support and deployment operations." />
    <Grid columns={3}>
      <Card title="Tenants"><Text variant="h2">{tenants.data.length}</Text></Card>
      <Card title="Active support grants"><Text variant="h2">{grants.data?.filter(x => x.status === 'Active').length ?? 0}</Text></Card>
      <Card title="Pending approvals"><Text variant="h2">{grants.data?.filter(x => x.status === 'Pending').length ?? 0}</Text></Card>
      <Card title="Local mode"><Text>SQLite · local files · no containers</Text></Card>
    </Grid>
  </Stack>;
}
