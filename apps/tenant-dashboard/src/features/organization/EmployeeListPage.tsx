import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Avatar, Card, ContentContainer, EmptyState, FilterBar, Grid, Icon, MetricCard,
  PageHeader, Select, SemanticLink, Stack, StatusChip, Text, TextInput, type ChipTone
} from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../app/useAuth';
import { useTenantFormatting } from '../../hooks/useTenantFormatting';
import { LocalizedDataTable } from '../common/LocalizedDataTable';
import { LocalizedErrorState } from '../common/LocalizedErrorState';
import { LocalizedFreshnessBadge } from '../common/LocalizedFreshnessBadge';
import { LocalizedLoadingState } from '../common/LocalizedLoadingState';
import { api } from '../../lib/api-client/api';
import type { components } from '../../lib/api-client/schema';
import { useDomainLabels } from '../../lib/domainLabels';
import { localizedCount } from '../../lib/localizedCount';

type EmployeeSummary = components['schemas']['EmployeeSummary'];

function employeeStateTone(state: string): ChipTone {
  switch (state.toLowerCase()) {
    case 'active':
      return 'success';
    case 'busy':
    case 'frozen':
      return 'warning';
    case 'rate_limited':
    case 'degraded':
    case 'unavailable':
    case 'terminated':
      return 'error';
    case 'pending_review':
      return 'info';
    default:
      return 'neutral';
  }
}

function EmployeeIdentity({ employee }: { employee: EmployeeSummary }) {
  return (
    <Stack direction="row" spacing={1.5} alignItems="center" sx={{ minWidth: 0 }}>
      <Avatar name={employee.displayName} status={employee.state} size="small" />
      <Stack spacing={0.25} sx={{ minWidth: 0 }}>
        <Text variant="bodyStrong" sx={{ overflowWrap: 'anywhere' }}>{employee.displayName}</Text>
        <Text variant="caption">{employee.employeeNo} · {employee.position}</Text>
      </Stack>
    </Stack>
  );
}

export function EmployeeListPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const domainLabel = useDomainLabels();
  const format = useTenantFormatting();
  const canReadCollaboration = session?.effectiveAccess?.canReadCollaboration === true;
  const { data, isLoading, error } = useQuery({ queryKey: ['employees'], queryFn: api.employees });
  const [query, setQuery] = useState('');
  const [state, setState] = useState('all');

  const summary = useMemo(() => {
    const employees = data ?? [];
    return {
      total: employees.length,
      active: employees.filter(employee => employee.state.toLowerCase() === 'active').length,
      fresh: employees.filter(employee => ['live', 'fresh'].includes(employee.freshness.toLowerCase())).length
    };
  }, [data]);
  const stateOptions = useMemo(() => [...new Set((data ?? []).map(employee => employee.state))], [data]);
  const filteredEmployees = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    return (data ?? []).filter(employee => {
      const matchesState = state === 'all' || employee.state === state;
      const searchable = `${employee.displayName} ${employee.employeeNo} ${employee.position} ${employee.stateReason ?? ''}`.toLowerCase();
      return matchesState && (!normalizedQuery || searchable.includes(normalizedQuery));
    });
  }, [data, query, state]);

  if (isLoading) return <LocalizedLoadingState />;
  if (error) return <LocalizedErrorState error={error} />;

  return (
    <ContentContainer>
      <Stack spacing={3}>
        <PageHeader
          title={t('organizationPage.organization')}
          description={t('organizationPage.employeeWorkStateAndPermanentEmploymentIdentity')}
          actions={<Stack direction="row" spacing={2} wrap>
            {canReadCollaboration ? <SemanticLink href="/organization/collaboration">{t('organizationPage.collaborationWorkspace')}</SemanticLink> : null}
            <SemanticLink href="/organization/manage">{t('organizationPage.manageOrganization')}</SemanticLink>
          </Stack>}
        />
        <Grid columns={3}>
          <MetricCard label={t('collaborationPage.employees')} value={format.number(summary.total)} hint={t('organizationPage.employeeWorkStateAndPermanentEmploymentIdentity')} icon="people" />
          <MetricCard label={t('collaborationPage.activeOrganizationMembers')} value={format.number(summary.active)} icon="activity" />
          <MetricCard label={t('common.status')} value={format.number(summary.fresh)} icon="clock" />
        </Grid>
        {!data?.length ? (
          <Card>
            <EmptyState title={t('organizationPage.noEmployees')} description={t('organizationPage.startTheCompanyToCreateTheFirstController')} />
          </Card>
        ) : (
          <>
            <Card padding="small">
              <FilterBar action={<StatusChip label={localizedCount(t, format, 'collaborationPage.employeeCount', filteredEmployees.length)} tone="info" />}>
                <Stack sx={{ flex: '1 1 260px' }}>
                  <TextInput
                    value={query}
                    onChange={setQuery}
                    placeholder={t('collaborationPage.searchPeopleGroupsProjectsOrEvents')}
                    startAdornment={<Icon name="search" />}
                    inputProps={{ 'aria-label': t('collaborationPage.searchCollaboration') }}
                  />
                </Stack>
                <Stack sx={{ width: { xs: '100%', sm: 220 } }}>
                  <Select
                    label={t('common.status')}
                    value={state}
                    onChange={setState}
                    options={[
                      { value: 'all', label: t('collaborationPage.allStatuses') },
                      ...stateOptions.map(value => ({ value, label: domainLabel(value) }))
                    ]}
                  />
                </Stack>
              </FilterBar>
            </Card>
            <Stack sx={{ display: { xs: 'none', md: 'block' } }}>
              <Card padding="none">
                <LocalizedDataTable
                  rows={filteredEmployees}
                  getRowId={employee => employee.id}
                  density="compact"
                  minWidth={920}
                  ariaLabel={t('collaborationPage.employees')}
                  emptyTitle={t('common.noRecords')}
                  columns={[
                    { id: 'employee', header: t('common.name'), render: employee => <EmployeeIdentity employee={employee} /> },
                    { id: 'state', header: t('common.status'), render: employee => <Stack spacing={0.5}><StatusChip label={domainLabel(employee.state)} tone={employeeStateTone(employee.state)} /><LocalizedFreshnessBadge freshness={employee.freshness} /></Stack> },
                    { id: 'reason', header: t('common.reason'), render: employee => employee.stateReason ?? t('common.notAvailable') },
                    { id: 'actions', header: t('common.actions'), render: employee => <SemanticLink href={`/organization/employees/${employee.id}`}>{t('organizationPage.viewWorkTimeline')}</SemanticLink> }
                  ]}
                />
              </Card>
            </Stack>
            <Stack spacing={2} sx={{ display: { xs: 'flex', md: 'none' } }}>
              {filteredEmployees.map(employee => (
                <Card key={employee.id} action={<StatusChip label={domainLabel(employee.state)} tone={employeeStateTone(employee.state)} />}>
                  <EmployeeIdentity employee={employee} />
                  {employee.stateReason ? <Text variant="secondary">{employee.stateReason}</Text> : null}
                  <Stack direction="row" wrap spacing={2} alignItems="center">
                    <LocalizedFreshnessBadge freshness={employee.freshness} />
                    <SemanticLink href={`/organization/employees/${employee.id}`}>{t('organizationPage.viewWorkTimeline')}</SemanticLink>
                  </Stack>
                </Card>
              ))}
            </Stack>
          </>
        )}
      </Stack>
    </ContentContainer>
  );
}
