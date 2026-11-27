import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AppThemeProvider } from '@regent/ui-shared';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BackupsPage } from './BackupsPage';
import type { BackupJob, BackupProfile, RestoreJob } from '../lib/api';
import i18n from '../lib/i18n';

const apiMocks = vi.hoisted(() => ({
  backups: vi.fn(), backupCoverage: vi.fn(), backupJobs: vi.fn(), restoreJobs: vi.fn(),
  saveBackupProfile: vi.fn(), runBackup: vi.fn(), requestRestore: vi.fn(), approveRestore: vi.fn()
}));
vi.mock('../lib/api', () => ({ api: apiMocks }));

const profile: BackupProfile = {
  id: 'default', deploymentId: 'custom-deployment', enabled: true,
  components: 'postgres,postgres_pitr,rustfs,keyvault', retentionDays: 73,
  postgresFull: true, postgresPitr: true, rustFs: true, keyVault: true,
  offsite: true, restoreDrills: true, schedule: 'weekly', targetRef: 'offsite://target', version: 9
};
const coverage = {
  profileId: 'default', enabled: true, status: 'covered', enabledComponents: ['rustfs', 'keyvault'],
  effectiveRpoMinutes: 5, effectiveRtoMinutes: 20, rpoStatus: 'measured', rtoStatus: 'measured'
};
const jobs: BackupJob[] = [
  { id: 'job-1', profileId: 'default', backupSetId: 'backup-set-1', profileVersion: 9, component: 'rustfs', status: 'Completed', startedAt: '2026-11-25T10:00:00Z', sha256: 'a' },
  { id: 'job-2', profileId: 'default', backupSetId: 'backup-set-1', profileVersion: 9, component: 'keyvault', status: 'Completed', startedAt: '2026-11-25T10:00:00Z', sha256: 'b' }
];
const pending: RestoreJob = {
  id: 'restore-1', profileId: 'default', backupIds: ['job-1', 'job-2'], scope: 'Full',
  approvalRef: 'drill-1', requester: 'operator-1', status: 'PendingApproval', requestedAt: '2026-11-25T10:05:00Z'
};

function renderPage(canWrite = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><AppThemeProvider><BackupsPage canWrite={canWrite} /></AppThemeProvider></QueryClientProvider>);
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.backups.mockResolvedValue([profile]);
  apiMocks.backupCoverage.mockResolvedValue(coverage);
  apiMocks.backupJobs.mockResolvedValue(jobs);
  apiMocks.restoreJobs.mockResolvedValue([]);
  apiMocks.saveBackupProfile.mockResolvedValue({ ...profile, enabled: false });
  apiMocks.runBackup.mockResolvedValue(jobs);
  apiMocks.requestRestore.mockResolvedValue(pending);
  apiMocks.approveRestore.mockResolvedValue({ ...pending, status: 'Verified', approver: 'operator-2' });
});

describe('BackupsPage', () => {
  it('patches the loaded profile without replacing retained configuration', async () => {
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Disable' }));

    await waitFor(() => expect(apiMocks.saveBackupProfile).toHaveBeenCalledWith({ ...profile, enabled: false }));
  });

  it('requests a selected completed set before a separate approval', async () => {
    apiMocks.restoreJobs.mockResolvedValue([]);
    apiMocks.requestRestore.mockImplementation(async () => {
      apiMocks.restoreJobs.mockResolvedValue([pending]);
      return pending;
    });
    renderPage();
    fireEvent.mouseDown(await screen.findByRole('combobox', { name: 'Completed backup set' }));
    fireEvent.click(await screen.findByRole('option', { name: /backup-s/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Request restore approval' }));

    await waitFor(() => expect(apiMocks.requestRestore).toHaveBeenCalledWith(['job-1', 'job-2'], 'Full'));
    expect(apiMocks.approveRestore).not.toHaveBeenCalled();
    expect(await screen.findByText('Restore requested. Pending approval by a different operator.')).toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: 'Approve restore' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/Approve Full restore for custom-deployment/)).toBeInTheDocument();
    expect(within(dialog).getByText(/backup-set-1/)).toBeInTheDocument();
    expect(apiMocks.approveRestore).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(apiMocks.approveRestore).not.toHaveBeenCalled();

    fireEvent.click(await screen.findByRole('button', { name: 'Approve restore' }));
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Approve restore' }));
    await waitFor(() => expect(apiMocks.approveRestore).toHaveBeenCalledWith('restore-1'));
  });

  it('does not render empty states while backup and restore queries are loading', async () => {
    apiMocks.backupJobs.mockReturnValue(new Promise(() => undefined));
    apiMocks.restoreJobs.mockReturnValue(new Promise(() => undefined));
    renderPage();

    await screen.findByRole('button', { name: 'Disable' });
    expect(screen.queryByText('No backup jobs')).not.toBeInTheDocument();
    expect(screen.queryByText('No completed backup sets')).not.toBeInTheDocument();
    expect(screen.queryByText('No restore drills')).not.toBeInTheDocument();
  });

  it('renders safe query errors instead of empty backup and restore states', async () => {
    apiMocks.backupJobs.mockRejectedValue(new Error('Backup jobs unavailable: private detail'));
    apiMocks.restoreJobs.mockRejectedValue(new Error('Restore drills unavailable: private detail'));
    renderPage();

    const safeMessage = i18n.t('common.errors.unexpected');
    expect((await screen.findAllByText(safeMessage)).length).toBeGreaterThan(0);
    expect(screen.queryByText(/Backup jobs unavailable/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Restore drills unavailable/)).not.toBeInTheDocument();
    expect(screen.queryByText('No backup jobs')).not.toBeInTheDocument();
    expect(screen.queryByText('No restore drills')).not.toBeInTheDocument();
  });

  it('requires every profile component for Full scope', async () => {
    apiMocks.backupJobs.mockResolvedValue([jobs[0]]);
    renderPage();
    fireEvent.mouseDown(await screen.findByRole('combobox', { name: 'Completed backup set' }));
    fireEvent.click(await screen.findByRole('option', { name: /backup-s/ }));

    expect(screen.getByRole('button', { name: 'Request restore approval' })).toBeDisabled();
    expect(screen.getByText('Full scope also requires: keyvault.')).toBeInTheDocument();
  });

  it('sanitizes raw backup job error details', async () => {
    apiMocks.backupJobs.mockResolvedValue([{ ...jobs[0], status: 'Blocked', error: 'private storage detail' }]);
    renderPage();

    expect(await screen.findByText('Details unavailable')).toBeInTheDocument();
    expect(screen.queryByText(/private storage detail/)).not.toBeInTheDocument();
  });

  it('localizes interpolated coverage rows when the language changes', async () => {
    apiMocks.backupCoverage.mockResolvedValue({
      ...coverage, componentRtoMinutes: { rustfs: 5, keyvault: null }
    });
    renderPage(false);

    expect(await screen.findByText('Status: Covered')).toBeInTheDocument();
    expect(screen.getByText('Components: rustfs and keyvault')).toBeInTheDocument();
    expect(screen.getByText('Effective RPO: 5 min')).toBeInTheDocument();
    expect(screen.getByText('Effective RTO: 20 min')).toBeInTheDocument();
    expect(screen.getByText('Component drills: rustfs: 5 min and keyvault: Unverified')).toBeInTheDocument();

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByText('状态：已覆盖')).toBeInTheDocument();
    expect(screen.getByText('组件：rustfs和keyvault')).toBeInTheDocument();
    expect(screen.getByText('有效 RPO：5分钟')).toBeInTheDocument();
    expect(screen.getByText('有效 RTO：20分钟')).toBeInTheDocument();
    expect(screen.getByText('组件演练：rustfs：5分钟和keyvault：未验证')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('localizes the not_configured coverage state without exposing the raw value', async () => {
    apiMocks.backups.mockResolvedValue([{ ...profile, enabled: false }]);
    apiMocks.backupCoverage.mockResolvedValue({
      ...coverage, enabled: false, status: 'not_configured', enabledComponents: [],
      effectiveRpoMinutes: null, effectiveRtoMinutes: null, rpoStatus: 'not_configured', rtoStatus: 'not_configured'
    });
    renderPage(false);

    expect(await screen.findByText('Status: Not configured')).toBeInTheDocument();
    expect(screen.queryByText(/not_configured/)).not.toBeInTheDocument();
    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByText('状态：未配置')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('does not render write controls for a read-only session', async () => {
    renderPage(false);
    expect(await screen.findByText('Read-only session: backup and restore writes are unavailable.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Disable' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Run component jobs' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Request restore approval' })).not.toBeInTheDocument();
  });
});
