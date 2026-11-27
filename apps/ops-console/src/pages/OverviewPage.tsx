import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Card, FreshnessBadge, Grid, Icon, PageHeader, Stack, StatusChip, Text, type CardTone, type IconName } from '@regent/ui-shared';
import { LocalizedErrorState, LocalizedLoadingState } from '../components/LocalizedStates';
import { OperationsLink } from '../components/OperationsLink';
import { api } from '../lib/api';
import { useDomainLabels } from '../lib/domainLabels';
import { localizedOpsErrorMessage } from '../lib/errorMessages';
import { useOpsFormatter } from '../lib/formatting';
import { localizedCount } from '../lib/localizedCount';

interface OverviewCardProps {
  title: string;
  icon: IconName;
  tone?: CardTone;
  to: string;
  linkLabel: string;
  loading: boolean;
  error: unknown;
  stale: boolean;
  onRetry: () => void;
  value: ReactNode;
  hint?: ReactNode;
}

function OverviewCard({ title, icon, tone = 'default', to, linkLabel, loading, error, stale, onRetry, value, hint }: OverviewCardProps) {
  const { t } = useTranslation();
  const failed = Boolean(error);
  return (
    <Card
      title={title}
      padding="small"
      action={<OperationsLink to={to}>{linkLabel} <Icon name="chevronRight" sx={{ fontSize: 14, verticalAlign: 'middle' }} /></OperationsLink>}
      sx={{ height: '100%', borderColor: 'divider' }}
    >
      <Stack spacing={1.25}>
        <Stack direction="row" spacing={1} alignItems="center">
          <Icon name={icon} sx={{ color: tone === 'default' ? 'text.secondary' : `${tone}.main` }} />
          {loading ? <LocalizedLoadingState inline /> : failed && !stale ? (
            <LocalizedErrorState
              inline
              message={localizedOpsErrorMessage(error, t)}
              onRetry={onRetry}
            />
          ) : <Text variant="metric">{value}</Text>}
        </Stack>
        {failed && stale ? (
          <LocalizedErrorState
            inline
            stale
            message={localizedOpsErrorMessage(error, t)}
            onRetry={onRetry}
          />
        ) : null}
        {!loading && (!failed || stale) && hint ? <Text component="div" variant="secondary">{hint}</Text> : null}
      </Stack>
    </Card>
  );
}

export function OverviewPage() {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const format = useOpsFormatter();
  const tenants = useQuery({ queryKey: ['ops-tenants'], queryFn: api.tenants });
  const grants = useQuery({ queryKey: ['ops-grants'], queryFn: api.grants });
  const status = useQuery({ queryKey: ['platform-status'], queryFn: api.status, refetchInterval: 30_000 });
  const alerts = useQuery({ queryKey: ['observability-alerts'], queryFn: api.alerts });
  const backups = useQuery({ queryKey: ['ops-backups'], queryFn: api.backups });
  const backupProfile = backups.data?.[0];
  const coverage = useQuery({
    queryKey: ['backup-coverage', backupProfile?.id ?? 'default'],
    queryFn: () => api.backupCoverage(backupProfile?.id ?? 'default'),
    enabled: !!backupProfile
  });

  const activeGrants = grants.data?.filter(grant => grant.status === 'Active').length ?? 0;
  const pendingApprovals = (grants.data ?? []).filter(grant => grant.status === 'Pending')
    .sort((left, right) => left.expiresAt.localeCompare(right.expiresAt));
  const activeAlerts = (alerts.data ?? []).filter(alert => alert.status !== 'Resolved');
  const highestAlert = [...activeAlerts].sort((left, right) => right.level.localeCompare(left.level))[0];
  const activeTenants = tenants.data?.filter(tenant => tenant.status === 'Active').length ?? 0;
  const suspendedTenants = (tenants.data?.length ?? 0) - activeTenants;
  const totalEmployees = tenants.data?.reduce((total, tenant) => total + tenant.employees, 0) ?? 0;
  const totalRunners = tenants.data?.reduce((total, tenant) => total + tenant.runnerCount, 0) ?? 0;
  const unhealthyComponents = status.data?.components?.filter(component => component.status !== 'Operational').length ?? 0;
  const healthStale = Boolean(status.error && status.data);
  const alertsStale = Boolean(alerts.error && alerts.data);
  const grantsStale = Boolean(grants.error && grants.data);
  const tenantsStale = Boolean(tenants.error && tenants.data);
  const backupError = backups.error ?? coverage.error;
  const backupStale = Boolean(backupError && (backups.data?.length || coverage.data));
  const backupLoading = backups.isLoading || Boolean(backupProfile && coverage.isLoading);
  const backupValue = coverage.data
    ? domainLabel(coverage.data.status)
    : backupProfile
      ? domainLabel(backupProfile.enabled ? 'Active' : 'Off')
      : domainLabel('NotConfigured');
  const backupHint = backupProfile ? (
    <Stack direction="row" spacing={1} wrap alignItems="center">
      <FreshnessBadge
        freshness={coverage.data?.lastSuccessfulAt ? 'fresh' : 'unknown'}
        label={coverage.data?.lastSuccessfulAt ? format.dateTime(coverage.data.lastSuccessfulAt) : t('common.never')}
      />
      <Text variant="caption">
        {t('backupsPage.effectiveRpo', {
          duration: coverage.data?.effectiveRpoMinutes == null ? domainLabel('Unverified') : format.duration(coverage.data.effectiveRpoMinutes, 'minute')
        })}
      </Text>
    </Stack>
  ) : t('backupsPage.noProfileTitle');

  return (
    <Stack spacing={2.5}>
      <PageHeader
        title={t('overviewPage.title')}
        description={t('overviewPage.description')}
        meta={<>
          <StatusChip label={t('overviewPage.localMode')} tone="info" />
          <Text variant="caption">{t('overviewPage.localModeDescription')}</Text>
        </>}
      />
      <Grid columns={3} spacing={1.5}>
        <OverviewCard
          title={t('statusPage.overallStatus')}
          icon="platform"
          tone={healthStale ? 'warning' : status.data?.overallStatus === 'Operational' ? 'success' : status.data ? 'error' : 'default'}
          to="/platform"
          linkLabel={t('shellLogin.links.platform')}
          loading={status.isLoading}
          error={status.error}
          stale={healthStale}
          onRetry={() => void status.refetch()}
          value={status.data ? domainLabel(status.data.overallStatus) : domainLabel('Unknown')}
          hint={<Stack direction="row" spacing={1} wrap alignItems="center">
            <FreshnessBadge
              freshness={healthStale ? 'stale' : status.data ? 'fresh' : 'unknown'}
              label={healthStale ? t('common.domain.stale') : status.data ? format.time(status.data.checkedAt) : t('common.domain.unknown')}
            />
            <Text variant="caption">{localizedCount(t, format, 'statusPage.componentCount', status.data?.components?.length ?? 0)}</Text>
            {unhealthyComponents ? <Text variant="caption" color="error">{t('statusPage.status')} · {format.number(unhealthyComponents)}</Text> : null}
          </Stack>}
        />
        <OverviewCard
          title={t('observabilityPage.alertsTitle')}
          icon="observability"
          tone={activeAlerts.some(alert => alert.level === 'L3' || alert.level === 'L4') ? 'error' : activeAlerts.length ? 'warning' : 'success'}
          to="/observability"
          linkLabel={t('shellLogin.links.observability')}
          loading={alerts.isLoading}
          error={alerts.error}
          stale={alertsStale}
          onRetry={() => void alerts.refetch()}
          value={localizedCount(t, format, 'observabilityPage.alertCount', activeAlerts.length)}
          hint={highestAlert
            ? `${t('observabilityPage.level')} · ${domainLabel(highestAlert.level)} · ${highestAlert.sloName}`
            : t('observabilityPage.noActiveAlertsTitle')}
        />
        <OverviewCard
          title={t('overviewPage.pendingApprovalsTitle')}
          icon="approvals"
          tone={pendingApprovals.length ? 'warning' : 'success'}
          to="/support"
          linkLabel={t('shellLogin.links.support')}
          loading={grants.isLoading}
          error={grants.error}
          stale={grantsStale}
          onRetry={() => void grants.refetch()}
          value={localizedCount(t, format, 'overviewPage.pendingApprovals', pendingApprovals.length)}
          hint={<Stack spacing={0.5}>
            <Text variant="caption">{localizedCount(t, format, 'overviewPage.activeGrants', activeGrants)}</Text>
            {pendingApprovals[0] ? <Text variant="caption">{t('supportPage.expires')} · {format.dateTime(pendingApprovals[0].expiresAt)}</Text> : null}
          </Stack>}
        />
        <OverviewCard
          title={t('backupsPage.coverageTitle')}
          icon="backups"
          tone={coverage.data?.rpoStatus?.toLowerCase() === 'missed' ? 'error' : coverage.data?.rpoStatus?.toLowerCase() === 'measured' ? 'success' : 'default'}
          to="/backups"
          linkLabel={t('shellLogin.links.backups')}
          loading={backupLoading}
          error={backupError}
          stale={backupStale}
          onRetry={() => {
            if (backups.error) void backups.refetch();
            if (coverage.error) void coverage.refetch();
          }}
          value={backupValue}
          hint={backupHint}
        />
        <OverviewCard
          title={t('overviewPage.tenantsTitle')}
          icon="tenants"
          tone="default"
          to="/tenants"
          linkLabel={t('shellLogin.links.tenants')}
          loading={tenants.isLoading}
          error={tenants.error}
          stale={tenantsStale}
          onRetry={() => void tenants.refetch()}
          value={localizedCount(t, format, 'overviewPage.tenants', tenants.data?.length ?? 0)}
          hint={<Stack spacing={0.5}>
            <Text variant="caption">{t('tenantsPage.employees')} · {format.number(totalEmployees)} · {t('tenantsPage.runners')} · {format.number(totalRunners)}</Text>
            <Text variant="caption">{domainLabel('Active')} · {format.number(activeTenants)} · {domainLabel('Suspended')} · {format.number(suspendedTenants)}</Text>
          </Stack>}
        />
      </Grid>
    </Stack>
  );
}
