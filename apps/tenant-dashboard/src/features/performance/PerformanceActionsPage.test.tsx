import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, findField, renderPage } from '../../test/renderPage';
import { PerformanceActionsPage } from './PerformanceActionsPage';

const apiMocks = vi.hoisted(() => ({ performanceActions: vi.fn(), improvements: vi.fn(), performAction: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.performanceActions.mockResolvedValue([]);
  apiMocks.improvements.mockResolvedValue([]);
  apiMocks.performAction.mockResolvedValue({});
});

async function prepareTermination() {
  renderPage(<PerformanceActionsPage />);
  await findField('Employee ID');
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Action' }));
  fireEvent.click(await screen.findByRole('option', { name: 'Terminate' }));
  fireEvent.change(field('Employee ID'), { target: { value: 'employee-1' } });
  fireEvent.change(field('Reason'), { target: { value: 'Approved offboarding review completed.' } });
  fireEvent.change(field('Boss approval ID'), { target: { value: 'approval-1' } });
  return screen.getByRole('button', { name: 'Execute action' });
}

async function confirmTermination() {
  const dialog = await screen.findByRole('dialog');
  expect(within(dialog).getByText(/Terminate employee employee-1/)).toBeInTheDocument();
  expect(within(dialog).getByText(/approval approval-1/)).toBeInTheDocument();
  fireEvent.change(within(dialog).getByRole('textbox', { name: 'employee-1' }), { target: { value: 'employee-1' } });
  fireEvent.click(within(dialog).getByRole('button', { name: 'Execute action' }));
}

describe('PerformanceActionsPage termination', () => {
  it('requires approval, reason and explicit employee-identifying confirmation', async () => {
    renderPage(<PerformanceActionsPage />);
    await findField('Employee ID');
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Action' }));
    fireEvent.click(await screen.findByRole('option', { name: 'Terminate' }));
    fireEvent.change(field('Employee ID'), { target: { value: 'employee-1' } });
    fireEvent.change(field('Reason'), { target: { value: 'Approved offboarding review completed.' } });
    const execute = screen.getByRole('button', { name: 'Execute action' });
    expect(execute).toBeDisabled();
    fireEvent.change(field('Boss approval ID'), { target: { value: 'approval-1' } });
    expect(execute).toBeEnabled();
    fireEvent.click(execute);
    expect(apiMocks.performAction).not.toHaveBeenCalled();
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/Terminate employee employee-1/)).toBeInTheDocument();
    expect(within(dialog).getByText(/approval approval-1/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Execute action' })).toBeDisabled();
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'employee-1' }), { target: { value: 'employee-1' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Execute action' }));
    await waitFor(() => expect(apiMocks.performAction).toHaveBeenCalledWith('employee-1', 'Terminate', 'Approved offboarding review completed.', 'approval-1'));
  });

  it('renders termination errors, retains approval context and prevents duplicate retries', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.performAction.mockRejectedValueOnce(new Error('Termination conflict')).mockReturnValueOnce(new Promise(resolve => { complete = resolve; }));
    const execute = await prepareTermination();

    fireEvent.click(execute);
    await confirmTermination();
    expect(await screen.findByText('Termination conflict')).toBeInTheDocument();
    expect(field('Boss approval ID')).toHaveValue('approval-1');
    expect(field('Reason')).toHaveValue('Approved offboarding review completed.');
    fireEvent.click(execute);
    await confirmTermination();
    await waitFor(() => expect(apiMocks.performAction).toHaveBeenCalledTimes(2));
    expect(execute).toBeDisabled();
    fireEvent.click(execute);
    expect(apiMocks.performAction).toHaveBeenCalledTimes(2);
    await act(async () => complete({}));
    expect(await screen.findByText('Performance action recorded.')).toBeInTheDocument();
  });
});
