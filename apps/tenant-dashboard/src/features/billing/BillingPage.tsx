import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button, Card, ContentContainer, DetailList, EmptyState, Grid, MetricCard,
  PageHeader, Stack, StatusChip, Text
} from '@regent/ui-shared';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { useTranslation } from 'react-i18next';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { useDomainLabels } from '../../lib/domainLabels';
import { toFiniteNumber } from '../../lib/formatting';

export function BillingPage() {
  const format = useTenantFormatting();
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const queryClient = useQueryClient();
  const summary = useQuery({ queryKey: ['billing'], queryFn: api.billing });
  const plans = useQuery({ queryKey: ['billing-plans'], queryFn: api.billingPlans });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['billing'] });
  const checkout = useMutation({ mutationFn: (planId: string) => api.checkout(planId), onSuccess: invalidate });
  const pay = useMutation({ mutationFn: (invoiceId: string) => api.payLocal(invoiceId), onSuccess: invalidate });
  if (summary.isLoading || plans.isLoading) return <LocalizedLoadingState />;
  const error = summary.error ?? plans.error;
  if (error) return <LocalizedErrorState error={error} />;
  const billing = summary.data;
  if (!billing) return <EmptyState title={t('common.notAvailable')} />;
  const pendingInvoices = billing.invoices.filter(invoice => invoice.status === 'Pending').length;
  const exceededQuotas = billing.quotas.filter(quota => quota.exceeded).length;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('billingPage.planBilling')}
          description={t('billingPage.tenantSubscriptionQuotasAndAppendOnlyLocalBillingLedger')}
          meta={<StatusChip label={domainLabel(billing.subscription.status)} tone="success" />}
        />
        <Grid columns={4}>
          <MetricCard label={t('billingPage.currentSubscription')} value={billing.plan.name} icon="billing" />
          <MetricCard
            label={t('billingPage.monthly')}
            value={format.moneyMinor(toFiniteNumber(billing.plan.monthlyAmountMinor), billing.plan.currency)}
            icon="wallet"
          />
          <MetricCard
            label={t('common.domain.pending')}
            value={format.number(pendingInvoices)}
            tone={pendingInvoices ? 'warning' : 'default'}
            icon="clock"
          />
          <MetricCard
            label={t('common.domain.exceeded')}
            value={format.number(exceededQuotas)}
            tone={exceededQuotas ? 'error' : 'default'}
            icon="warning"
          />
        </Grid>

        <Card title={t('billingPage.currentSubscription')} padding="small">
          <DetailList
            dense
            columns={2}
            items={[
              { label: t('billingPage.plan'), value: billing.plan.name },
              { label: t('common.status'), value: domainLabel(billing.subscription.status) },
              { label: t('billingPage.monthly'), value: format.moneyMinor(toFiniteNumber(billing.plan.monthlyAmountMinor), billing.plan.currency) },
              { label: t('common.version'), value: format.number(toFiniteNumber(billing.subscription.version)) }
            ]}
          />
          <Text>{t('billingPage.period', {
            value0: format.date(billing.subscription.currentPeriodStart),
            value1: format.date(billing.subscription.currentPeriodEnd)
          })}</Text>
          <Text variant="caption">{t('billingPage.localModeDoesNotChargeAPaymentProviderExternalPaymentAdaptersAreNotConfi')}</Text>
        </Card>

        <Card title={t('billingPage.planQuotas')} padding="small">
          <LocalizedDataTable
            rows={billing.quotas}
            getRowId={row => row.resource}
            density="compact"
            columns={[
              { id: 'resource', header: t('billingPage.resource'), render: row => row.resource },
              { id: 'used', header: t('billingPage.used'), render: row => format.number(toFiniteNumber(row.used)), numeric: true },
              { id: 'limit', header: t('billingPage.limit'), render: row => format.number(toFiniteNumber(row.limit)), numeric: true },
              { id: 'status', header: t('common.status'), render: row => (
                <StatusChip label={row.exceeded ? domainLabel('Exceeded') : domainLabel('WithinQuota')} tone={row.exceeded ? 'error' : 'success'} />
              ) }
            ]}
          />
        </Card>

        <Card title={t('billingPage.availablePlans')} padding="small">
          <LocalizedDataTable
            rows={plans.data ?? []}
            getRowId={row => row.id}
            density="compact"
            columns={[
              { id: 'name', header: t('billingPage.plan'), render: row => (
                <Stack direction="row" spacing={1} wrap alignItems="center">
                  <Text variant="bodyStrong">{row.name}</Text>
                  {row.id === billing.plan.id ? <StatusChip label={domainLabel('Active')} tone="success" /> : null}
                </Stack>
              ) },
              { id: 'price', header: t('billingPage.monthly'), render: row => format.moneyMinor(toFiniteNumber(row.monthlyAmountMinor), row.currency), numeric: true },
              { id: 'employees', header: t('billingPage.employees'), render: row => format.number(toFiniteNumber(row.maxEmployees)), numeric: true },
              { id: 'projects', header: t('billingPage.projects'), render: row => format.number(toFiniteNumber(row.maxProjects)), numeric: true },
              { id: 'action', header: t('common.action'), render: row => (
                <Button
                  size="small"
                  onClick={() => checkout.mutate(row.id)}
                  disabled={row.id === billing.plan.id || checkout.isPending}
                  loading={checkout.isPending && checkout.variables === row.id}
                >
                  {t('common.select')}
                </Button>
              ) }
            ]}
          />
        </Card>

        <Card title={t('billingPage.invoicesAndLedger')} padding="small">
          <Grid columns={2}>
            <LocalizedDataTable
              rows={billing.invoices}
              getRowId={row => row.id}
              density="compact"
              minWidth={520}
              columns={[
                { id: 'plan', header: t('billingPage.plan'), render: row => row.planId },
                { id: 'amount', header: t('common.amount'), render: row => format.moneyMinor(toFiniteNumber(row.amountMinor), row.currency), numeric: true },
                { id: 'status', header: t('common.status'), render: row => <StatusChip label={domainLabel(row.status)} tone={row.status === 'Pending' ? 'warning' : 'success'} /> },
                { id: 'action', header: t('common.action'), render: row => row.status === 'Pending'
                  ? <Button size="small" tone="secondary" onClick={() => pay.mutate(row.id)} loading={pay.isPending && pay.variables === row.id}>{t('billingPage.payLocally')}</Button>
                  : '—' }
              ]}
            />
            <LocalizedDataTable
              rows={billing.ledger}
              getRowId={row => row.id}
              density="compact"
              minWidth={520}
              columns={[
                { id: 'type', header: t('billingPage.entry'), render: row => domainLabel(row.entryType) },
                { id: 'amount', header: t('common.amount'), render: row => format.moneyMinor(toFiniteNumber(row.amountMinor), row.currency), numeric: true },
                { id: 'version', header: t('billingPage.priceVersion'), render: row => row.priceVersion },
                { id: 'created', header: t('common.created'), render: row => format.dateTime(row.createdAt) }
              ]}
            />
          </Grid>
        </Card>
        <LocalizedMutationFeedback mutation={checkout} successMessage={t('billingPage.planSelected')} />
        <LocalizedMutationFeedback mutation={pay} successMessage={t('billingPage.invoicePaidLocally')} />
      </Stack>
    </ContentContainer>
  );
}
