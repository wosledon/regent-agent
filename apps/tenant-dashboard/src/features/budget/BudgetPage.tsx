import { useQuery } from '@tanstack/react-query';
import { Card, EmptyState, ErrorState, LoadingState, PageHeader, SemanticLink, Stack, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function BudgetPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['projects'], queryFn: api.projects });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  const currencies = [...new Set(data?.map(project => project.currency) ?? [])];
  if (currencies.length === 0) return <EmptyState title="No budgets" description="Create a project to establish the first budget." />;
  return (
    <Stack spacing={3}>
      <PageHeader title="Budget & Cost" description="Project budgets by original currency." actions={<SemanticLink href="/budget/runtime">Open cost ledger and scheduling</SemanticLink>} />
      <Stack spacing={2}>
        {currencies.map(currency => {
          const projects = data!.filter(project => project.currency === currency);
          const spent = projects.reduce((sum, project) => sum + Number(project.spentMinor), 0);
          const budget = projects.reduce((sum, project) => sum + Number(project.budgetMinor), 0);
          return <Card key={currency} title={currency}><Text variant="h2">{spent.toLocaleString()} / {budget.toLocaleString()}</Text><Text>{budget ? (spent / budget * 100).toFixed(1) : 0}% used</Text></Card>;
        })}
      </Stack>
    </Stack>
  );
}
