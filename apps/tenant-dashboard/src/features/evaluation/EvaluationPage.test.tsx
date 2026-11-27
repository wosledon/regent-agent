import { fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, renderPage } from '../../test/renderPage';
import { EvaluationPage } from './EvaluationPage';

const apiMocks = vi.hoisted(() => ({
  evaluationSuites: vi.fn(), evaluations: vi.fn(), evaluationQa: vi.fn(), employees: vi.fn(),
  runEvaluation: vi.fn(), runQaGate: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.evaluationSuites.mockResolvedValue([{ id: 'suite-1', name: 'Core Capability', version: 1, caseCount: 10 }]);
  apiMocks.evaluations.mockResolvedValue([]);
  apiMocks.evaluationQa.mockResolvedValue({ calibrations: [], gates: [] });
  apiMocks.employees.mockResolvedValue([{ id: 'employee-1', employeeNo: 'E-1', displayName: 'Engineer', position: 'Engineering', state: 'active', stateReason: null, freshness: 'fresh' }]);
  apiMocks.runEvaluation.mockResolvedValue({});
  apiMocks.runQaGate.mockResolvedValue({});
});

async function choose(label: string, option: string) {
  fireEvent.mouseDown(screen.getByRole('combobox', { name: label }));
  fireEvent.click(await screen.findByRole('option', { name: option }));
}

describe('EvaluationPage explicit selections and validation', () => {
  it('does not preselect a demo employee or core suite', async () => {
    renderPage(<EvaluationPage />);
    await screen.findByText('Core Capability v1');
    expect(screen.getByRole('combobox', { name: 'Candidate employee' }).parentElement?.querySelector('input')).toHaveValue('');
    expect(screen.getByRole('combobox', { name: 'Evaluation suite' }).parentElement?.querySelector('input')).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Run evaluation' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Evaluate QA gate' })).toBeDisabled();
  });

  it('runs only the explicitly selected suite and employee', async () => {
    renderPage(<EvaluationPage />);
    await screen.findByText('Core Capability v1');
    await choose('Candidate employee', 'Engineer · E-1');
    fireEvent.click(screen.getByRole('button', { name: 'Run evaluation' }));
    await waitFor(() => expect(apiMocks.runEvaluation).toHaveBeenCalledWith('suite-1', 'employee-1'));
    await choose('Evaluation suite', 'Core Capability v1');
    expect(screen.getByRole('button', { name: 'Evaluate QA gate' })).toBeEnabled();
  });

  it('validates integer counts and sample bounds before a gate write', async () => {
    renderPage(<EvaluationPage />);
    await screen.findByText('Core Capability v1');
    await choose('Candidate employee', 'Engineer · E-1');
    await choose('Evaluation suite', 'Core Capability v1');
    fireEvent.change(field('Successes'), { target: { value: '101' } });
    expect(screen.getByRole('button', { name: 'Evaluate QA gate' })).toBeDisabled();
    expect(screen.getByText('Successes must be an integer from 0 to 100.')).toBeInTheDocument();
    fireEvent.change(field('Successes'), { target: { value: '90' } });
    fireEvent.change(field('Baseline successes'), { target: { value: '101' } });
    expect(screen.getByRole('button', { name: 'Evaluate QA gate' })).toBeDisabled();
    fireEvent.change(field('Baseline successes'), { target: { value: '85' } });
    fireEvent.change(field('Repeats'), { target: { value: '0' } });
    expect(screen.getByRole('button', { name: 'Evaluate QA gate' })).toBeDisabled();
    fireEvent.change(field('Repeats'), { target: { value: '1.5' } });
    expect(screen.getByRole('button', { name: 'Evaluate QA gate' })).toBeDisabled();
    fireEvent.change(field('Repeats'), { target: { value: '2' } });
    fireEvent.change(field('Discordant A'), { target: { value: '-1' } });
    expect(screen.getByRole('button', { name: 'Evaluate QA gate' })).toBeDisabled();
  });

  it('renders QA gate mutation errors without replacing loaded selections', async () => {
    apiMocks.runQaGate.mockRejectedValue(new Error('QA gate conflict'));
    renderPage(<EvaluationPage />);
    await screen.findByText('Core Capability v1');
    await choose('Candidate employee', 'Engineer · E-1');
    await choose('Evaluation suite', 'Core Capability v1');
    fireEvent.click(screen.getByRole('button', { name: 'Evaluate QA gate' }));
    expect(await screen.findByText('QA gate conflict')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Model evaluation' })).toBeInTheDocument();
  });

  it('renders evaluation mutation feedback', async () => {
    apiMocks.runEvaluation.mockRejectedValue(new Error('Evaluation conflict'));
    renderPage(<EvaluationPage />);
    await screen.findByText('Core Capability v1');
    await choose('Candidate employee', 'Engineer · E-1');
    fireEvent.click(screen.getByRole('button', { name: 'Run evaluation' }));
    expect(await screen.findByText('Evaluation conflict')).toBeInTheDocument();
  });
});
