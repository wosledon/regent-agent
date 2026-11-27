import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, renderPage } from '../../test/renderPage';
import { RuntimePolicyPage } from './RuntimePolicyPage';

const apiMocks = vi.hoisted(() => ({
  runtimePolicy: vi.fn(), costSummary: vi.fn(), setBudget: vi.fn(), recordCost: vi.fn(),
  updateSchedule: vi.fn(), updateCapacity: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

const policy = {
  costs: [], budgets: [], allocationRules: [],
  schedule: {
    timeZone: 'America/New_York', alwaysAvailable: false, emergencyWake: false, version: 7,
    windows: [
      { days: 'Mon,Wed,Fri', startLocal: '08:30', endLocal: '16:45', kind: 'Cheap' },
      { days: 'Sat', startLocal: '10:00', endLocal: '12:00', kind: 'Silent' }
    ]
  },
  capacity: { maxConcurrentTasks: 3, maxQueueLength: 9, apiP95TargetMs: 777, noisyNeighborCpuPercent: 55, version: 4 }
};

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.runtimePolicy.mockResolvedValue(policy);
  apiMocks.costSummary.mockResolvedValue({ totals: [], unallocatedCount: 0, warnings: ['same warning', 'same warning'] });
  apiMocks.updateSchedule.mockImplementation(async value => value);
  apiMocks.updateCapacity.mockImplementation(async value => value);
  apiMocks.setBudget.mockResolvedValue({});
  apiMocks.recordCost.mockResolvedValue({});
});

describe('RuntimePolicyPage drafts', () => {
  it('hydrates and submits the complete persisted schedule and capacity', async () => {
    renderPage(<RuntimePolicyPage />);
    expect(await screen.findByDisplayValue('America/New_York', {}, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.getByDisplayValue('Mon,Wed,Fri')).toBeInTheDocument();
    expect(screen.getByDisplayValue('08:30')).toBeInTheDocument();
    expect(screen.getByDisplayValue('3')).toBeInTheDocument();
    expect(screen.getByDisplayValue('9')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Emergency wake' })).toHaveTextContent('Disabled');
    expect(screen.getByText(/P95 777 ms · CPU guard 55% · v4/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Save schedule' }));
    await waitFor(() => expect(apiMocks.updateSchedule).toHaveBeenCalledWith(policy.schedule));
    fireEvent.change(field('Max concurrent tasks'), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save capacity policy' }));
    await waitFor(() => expect(apiMocks.updateCapacity).toHaveBeenCalledWith({ ...policy.capacity, maxConcurrentTasks: 5 }));
  });

  it('keeps a dirty schedule while clean capacity follows a new server version', async () => {
    const { client } = renderPage(<RuntimePolicyPage />);
    const timeZone = await screen.findByDisplayValue('America/New_York', {}, { timeout: 5000 });
    fireEvent.change(timeZone, { target: { value: 'UTC' } });
    apiMocks.runtimePolicy.mockResolvedValue({
      ...policy,
      schedule: { ...policy.schedule, timeZone: 'Europe/London', version: 8 },
      capacity: { ...policy.capacity, maxConcurrentTasks: 6, version: 5 }
    });
    await act(async () => { await client.refetchQueries({ queryKey: ['runtime-policy'] }); });

    expect(timeZone).toHaveValue('UTC');
    expect(await screen.findByDisplayValue('6')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Save schedule' }));
    await waitFor(() => expect(apiMocks.updateSchedule).toHaveBeenCalledWith(expect.objectContaining({ timeZone: 'UTC', version: 7 })));
  });

  it('does not replace clean drafts on a same-version background refetch', async () => {
    const { client } = renderPage(<RuntimePolicyPage />);
    const timeZone = await screen.findByDisplayValue('America/New_York', {}, { timeout: 5000 });
    apiMocks.runtimePolicy.mockResolvedValue({
      ...policy,
      schedule: { ...policy.schedule, timeZone: 'Europe/London' },
      capacity: { ...policy.capacity, maxConcurrentTasks: 6 }
    });
    await act(async () => { await client.refetchQueries({ queryKey: ['runtime-policy'] }); });

    await waitFor(() => expect(timeZone).toHaveValue('America/New_York'));
    expect(screen.getByDisplayValue('3')).toBeInTheDocument();
    expect(screen.queryByDisplayValue('Europe/London')).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('6')).not.toBeInTheDocument();
  });

  it.each(['schedule', 'capacity'] as const)('ignores a delayed stale refetch after saving the %s', async kind => {
    let completeRefetch!: (value: typeof policy) => void;
    const savedSchedule = { ...policy.schedule, timeZone: 'UTC', version: '8' };
    const savedCapacity = { ...policy.capacity, maxConcurrentTasks: 6, version: '5' };
    if (kind === 'schedule') apiMocks.updateSchedule.mockResolvedValue(savedSchedule);
    else apiMocks.updateCapacity.mockResolvedValue(savedCapacity);
    renderPage(<RuntimePolicyPage />);
    await screen.findByDisplayValue('America/New_York', {}, { timeout: 5000 });
    apiMocks.runtimePolicy.mockReturnValueOnce(new Promise(resolve => { completeRefetch = resolve; }));

    if (kind === 'schedule') {
      fireEvent.change(field('IANA time zone'), { target: { value: 'UTC' } });
      fireEvent.click(screen.getByRole('button', { name: 'Save schedule' }));
      expect(await screen.findByText('Work schedule saved.')).toBeInTheDocument();
    } else {
      fireEvent.change(field('Max concurrent tasks'), { target: { value: '6' } });
      fireEvent.click(screen.getByRole('button', { name: 'Save capacity policy' }));
      expect(await screen.findByText('Capacity policy saved.')).toBeInTheDocument();
    }
    await waitFor(() => expect(apiMocks.runtimePolicy).toHaveBeenCalledTimes(2));
    await act(async () => completeRefetch(policy));

    expect(kind === 'schedule' ? field('IANA time zone') : field('Max concurrent tasks')).toHaveValue(kind === 'schedule' ? 'UTC' : 6);
    expect(kind === 'schedule' ? screen.queryByDisplayValue('America/New_York') : screen.queryByDisplayValue('3')).not.toBeInTheDocument();
  });

  it.each(['schedule', 'capacity'] as const)('retains the saved %s when the mutation omits its version', async kind => {
    const savedSchedule = { ...policy.schedule, timeZone: 'UTC', version: undefined };
    const savedCapacity = { ...policy.capacity, maxConcurrentTasks: 6, version: undefined };
    if (kind === 'schedule') apiMocks.updateSchedule.mockResolvedValue(savedSchedule);
    else apiMocks.updateCapacity.mockResolvedValue(savedCapacity);
    renderPage(<RuntimePolicyPage />);
    await screen.findByDisplayValue('America/New_York', {}, { timeout: 5000 });

    if (kind === 'schedule') {
      fireEvent.change(field('IANA time zone'), { target: { value: 'UTC' } });
      fireEvent.click(screen.getByRole('button', { name: 'Save schedule' }));
      expect(await screen.findByText('Work schedule saved.')).toBeInTheDocument();
    } else {
      fireEvent.change(field('Max concurrent tasks'), { target: { value: '6' } });
      fireEvent.click(screen.getByRole('button', { name: 'Save capacity policy' }));
      expect(await screen.findByText('Capacity policy saved.')).toBeInTheDocument();
    }
    await waitFor(() => expect(apiMocks.runtimePolicy).toHaveBeenCalledTimes(2));
    expect(kind === 'schedule' ? field('IANA time zone') : field('Max concurrent tasks')).toHaveValue(kind === 'schedule' ? 'UTC' : 6);
  });

  it.each(['schedule', 'capacity'] as const)('retains the saved %s and success feedback after a failed refetch', async kind => {
    const savedSchedule = { ...policy.schedule, timeZone: 'UTC', version: 8 };
    const savedCapacity = { ...policy.capacity, maxConcurrentTasks: 6, version: 5 };
    if (kind === 'schedule') apiMocks.updateSchedule.mockResolvedValue(savedSchedule);
    else apiMocks.updateCapacity.mockResolvedValue(savedCapacity);
    renderPage(<RuntimePolicyPage />);
    await screen.findByDisplayValue('America/New_York', {}, { timeout: 5000 });
    apiMocks.runtimePolicy.mockRejectedValueOnce(new Error('Policy refresh failed'));

    if (kind === 'schedule') {
      fireEvent.change(field('IANA time zone'), { target: { value: 'UTC' } });
      fireEvent.click(screen.getByRole('button', { name: 'Save schedule' }));
      expect(await screen.findByText('Work schedule saved.')).toBeInTheDocument();
    } else {
      fireEvent.change(field('Max concurrent tasks'), { target: { value: '6' } });
      fireEvent.click(screen.getByRole('button', { name: 'Save capacity policy' }));
      expect(await screen.findByText('Capacity policy saved.')).toBeInTheDocument();
    }
    await waitFor(() => expect(apiMocks.runtimePolicy).toHaveBeenCalledTimes(2));

    expect(screen.getByRole('heading', { name: 'Budget, cost & scheduling' })).toBeInTheDocument();
    expect(kind === 'schedule' ? field('IANA time zone') : field('Max concurrent tasks')).toHaveValue(kind === 'schedule' ? 'UTC' : 6);
    expect(screen.queryByText('Policy refresh failed')).not.toBeInTheDocument();
  });

  it('never substitutes actionable defaults for missing persisted policy fields', async () => {
    apiMocks.runtimePolicy.mockResolvedValue({ costs: [], budgets: [], allocationRules: [], schedule: { version: 9 }, capacity: { version: 9 } });
    renderPage(<RuntimePolicyPage />);
    await screen.findByRole('heading', { name: 'Budget, cost & scheduling' });

    expect(field('IANA time zone')).toHaveValue('');
    expect(field('Max concurrent tasks')).toHaveValue(null);
    expect(screen.getByRole('button', { name: 'Save schedule' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Save capacity policy' })).toBeDisabled();
    expect(screen.queryByDisplayValue('Asia/Shanghai')).not.toBeInTheDocument();
  });

  it('validates every persisted work window before submitting the schedule', async () => {
    apiMocks.runtimePolicy.mockResolvedValue({
      ...policy,
      schedule: {
        ...policy.schedule,
        windows: [...policy.schedule.windows, { days: 'Sun', startLocal: '25:00', endLocal: '12:00', kind: 'Silent' }]
      }
    });
    renderPage(<RuntimePolicyPage />);
    await screen.findByDisplayValue('America/New_York', {}, { timeout: 5000 });

    expect(screen.getByRole('button', { name: 'Save schedule' })).toBeDisabled();
    expect(screen.getByText('Window 3 start must use HH:mm.')).toBeInTheDocument();
  });

  it('validates limits, capacity, timezone, windows and currency before writes', async () => {
    renderPage(<RuntimePolicyPage />);
    await screen.findByDisplayValue('America/New_York', {}, { timeout: 5000 });
    fireEvent.change(field('Project ID'), { target: { value: 'project-1' } });
    fireEvent.change(field('Soft limit'), { target: { value: '20' } });
    fireEvent.change(field('Hard limit'), { target: { value: '10' } });
    expect(screen.getByRole('button', { name: 'Save budget' })).toBeDisabled();
    expect(screen.getByText('Hard limit must be greater than or equal to the soft limit.')).toBeInTheDocument();

    fireEvent.change(field('Max concurrent tasks'), { target: { value: '0' } });
    expect(screen.getByRole('button', { name: 'Save capacity policy' })).toBeDisabled();
    fireEvent.change(field('IANA time zone'), { target: { value: 'Invalid/Zone' } });
    expect(screen.getByRole('button', { name: 'Save schedule' })).toBeDisabled();
    fireEvent.change(field('Window start'), { target: { value: '25:00' } });
    expect(screen.getByText('Time zone must be a valid IANA time zone.')).toBeInTheDocument();

    fireEvent.change(field('Currency'), { target: { value: 'US' } });
    expect(screen.getByRole('button', { name: 'Save budget' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Record cost once' })).toBeDisabled();
    fireEvent.change(field('Currency'), { target: { value: 'USD' } });
    fireEvent.change(field('Amount'), { target: { value: 'Infinity' } });
    expect(screen.getByRole('button', { name: 'Record cost once' })).toBeDisabled();
  });

  it('prevents duplicate schedule and capacity writes while pending', async () => {
    let completeSchedule!: (value: typeof policy.schedule) => void;
    let completeCapacity!: (value: typeof policy.capacity) => void;
    apiMocks.updateSchedule.mockReturnValue(new Promise(resolve => { completeSchedule = resolve; }));
    apiMocks.updateCapacity.mockReturnValue(new Promise(resolve => { completeCapacity = resolve; }));
    renderPage(<RuntimePolicyPage />);
    await screen.findByDisplayValue('America/New_York', {}, { timeout: 5000 });

    const saveSchedule = screen.getByRole('button', { name: 'Save schedule' });
    fireEvent.click(saveSchedule);
    await waitFor(() => expect(apiMocks.updateSchedule).toHaveBeenCalledTimes(1));
    expect(saveSchedule).toBeDisabled();
    fireEvent.click(saveSchedule);
    expect(apiMocks.updateSchedule).toHaveBeenCalledTimes(1);
    await act(async () => completeSchedule(policy.schedule));
    expect(await screen.findByText('Work schedule saved.')).toBeInTheDocument();

    fireEvent.change(field('Max concurrent tasks'), { target: { value: '5' } });
    const saveCapacity = screen.getByRole('button', { name: 'Save capacity policy' });
    fireEvent.click(saveCapacity);
    await waitFor(() => expect(apiMocks.updateCapacity).toHaveBeenCalledTimes(1));
    expect(saveCapacity).toBeDisabled();
    fireEvent.click(saveCapacity);
    expect(apiMocks.updateCapacity).toHaveBeenCalledTimes(1);
    await act(async () => completeCapacity({ ...policy.capacity, maxConcurrentTasks: 5 }));
  });

  it('renders schedule mutation errors without replacing the page', async () => {
    apiMocks.updateSchedule.mockRejectedValue(new Error('Schedule conflict'));
    renderPage(<RuntimePolicyPage />);
    await screen.findByDisplayValue('America/New_York', {}, { timeout: 5000 });
    fireEvent.click(screen.getByRole('button', { name: 'Save schedule' }));
    expect(await screen.findByText('Schedule conflict')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Budget, cost & scheduling' })).toBeInTheDocument();
  });
});
