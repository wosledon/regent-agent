import { useQuery } from '@tanstack/react-query';
import { Card, ErrorState, LoadingState, Text } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

export function BudgetPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ['projects'], queryFn: api.projects });
  if (isLoading) return <LoadingState />;
  if (error) return <ErrorState message={error.message} />;
  const currencies = [...new Set(data?.map(project => project.currency) ?? [])];
  return (
    <div>
      <Text variant="h1">Budget & Cost</Text>
      {currencies.map(currency => {
        const projects = data!.filter(project => project.currency === currency);
        const spent = projects.reduce((sum, project) => sum + Number(project.spentMinor), 0);
        const budget = projects.reduce((sum, project) => sum + Number(project.budgetMinor), 0);
        return <Card key={currency} title={currency}><Text variant="h2">{spent.toLocaleString()} / {budget.toLocaleString()}</Text><Text>{budget ? (spent / budget * 100).toFixed(1) : 0}% used</Text></Card>;
      })}
    </div>
  );
}
