import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, DataTable, ErrorState, LoadingState, PageHeader, Stack, StatusChip, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function BillingPage() {
  const queryClient = useQueryClient();
  const summary = useQuery({ queryKey: ['billing'], queryFn: api.billing });
  const plans = useQuery({ queryKey: ['billing-plans'], queryFn: api.billingPlans });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['billing'] });
  const checkout = useMutation({ mutationFn: api.checkout, onSuccess: invalidate });
  const pay = useMutation({ mutationFn: api.payLocal, onSuccess: invalidate });
  if (summary.isLoading || plans.isLoading) return <LoadingState />;
  const error = summary.error ?? plans.error ?? checkout.error ?? pay.error;
  if (error) return <ErrorState message={error.message} />;
  const billing = summary.data!;
  return <Stack spacing={3}>
    <PageHeader title="Plan & Billing" description="Tenant subscription, quotas and append-only local billing ledger." />
    <Card title="Current subscription" action={<StatusChip label={billing.subscription.status} tone="success" />}>
      <Text variant="h2">{billing.plan.name}</Text>
      <Text>{billing.plan.currency} {billing.plan.monthlyAmountMinor.toLocaleString()} / month</Text>
      <Text>Period: {new Date(billing.subscription.currentPeriodStart).toLocaleDateString()} – {new Date(billing.subscription.currentPeriodEnd).toLocaleDateString()}</Text>
      <Text variant="caption">Local mode does not charge a payment provider. External payment adapters are not configured.</Text>
    </Card>
    <Card title="Plan quotas">
      <DataTable rows={billing.quotas} getRowId={row => row.resource} columns={[
        { id: 'resource', header: 'Resource', render: row => row.resource },
        { id: 'used', header: 'Used', render: row => row.used.toLocaleString() },
        { id: 'limit', header: 'Limit', render: row => row.limit.toLocaleString() },
        { id: 'status', header: 'Status', render: row => <StatusChip label={row.exceeded ? 'Exceeded' : 'Within quota'} tone={row.exceeded ? 'error' : 'success'} /> }
      ]} />
    </Card>
    <Card title="Available plans">
      <DataTable rows={plans.data ?? []} getRowId={row => row.id} columns={[
        { id: 'name', header: 'Plan', render: row => row.name },
        { id: 'price', header: 'Monthly', render: row => `${row.currency} ${row.monthlyAmountMinor.toLocaleString()}` },
        { id: 'employees', header: 'Employees', render: row => row.maxEmployees },
        { id: 'projects', header: 'Projects', render: row => row.maxProjects },
        { id: 'action', header: 'Action', render: row => <Button onClick={() => checkout.mutate(row.id)} disabled={row.id === billing.plan.id || checkout.isPending}>Select</Button> }
      ]} />
    </Card>
    <Card title="Invoices and ledger">
      <DataTable rows={billing.invoices} getRowId={row => row.id} columns={[
        { id: 'plan', header: 'Plan', render: row => row.planId },
        { id: 'amount', header: 'Amount', render: row => `${row.currency} ${row.amountMinor.toLocaleString()}` },
        { id: 'status', header: 'Status', render: row => row.status },
        { id: 'action', header: 'Action', render: row => row.status === 'Pending'
          ? <Button tone="secondary" onClick={() => pay.mutate(row.id)} loading={pay.isPending}>Pay locally</Button> : '—' }
      ]} />
      <DataTable rows={billing.ledger} getRowId={row => row.id} columns={[
        { id: 'type', header: 'Entry', render: row => row.entryType },
        { id: 'amount', header: 'Amount', render: row => `${row.currency} ${row.amountMinor.toLocaleString()}` },
        { id: 'version', header: 'Price version', render: row => row.priceVersion },
        { id: 'created', header: 'Created', render: row => new Date(row.createdAt).toLocaleString() }
      ]} />
    </Card>
  </Stack>;
}
