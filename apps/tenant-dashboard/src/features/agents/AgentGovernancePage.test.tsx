import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, renderPage } from '../../test/renderPage';
import { AgentGovernancePage } from './AgentGovernancePage';

const apiMocks = vi.hoisted(() => ({
  taskGovernance: vi.fn(), agentEvolution: vi.fn(), assessCapability: vi.fn(), createReviewTask: vi.fn(),
  approveReviewTask: vi.fn(), createDebate: vi.fn(), discloseTools: vi.fn(), localization: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));
vi.mock('../../app/useAuth', () => ({ useAuth: () => ({ session: { displayName: 'Owner', role: 'Owner', authMethod: 'Oidc', sessionKind: 'Standard', capabilities: ['dashboard.read'] } }) }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.taskGovernance.mockResolvedValue({
    capabilities: [], reviews: [{ id: 'review-1', subjectId: 'subject-1', status: 'Open' }], debates: [], ledger: []
  });
  apiMocks.agentEvolution.mockResolvedValue({ prompts: [], skills: [], onboardingBaselines: [], publicAssets: [], toolPolicies: [] });
  apiMocks.assessCapability.mockResolvedValue({});
  apiMocks.createReviewTask.mockResolvedValue({});
  apiMocks.approveReviewTask.mockResolvedValue({});
  apiMocks.createDebate.mockResolvedValue({});
  apiMocks.discloseTools.mockResolvedValue({ tools: [{ name: 'research.search', level: 'Primary' }] });
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
});

describe('AgentGovernancePage authoritative inputs', () => {
  it('shows read-only caller authority separately from policy requirements', async () => {
    renderPage(<AgentGovernancePage />);
    expect(await screen.findByText('Caller authority (read-only)')).toBeInTheDocument();
    expect(screen.getByText('Role: Owner · Authentication: OIDC · Session: Standard')).toBeInTheDocument();
    expect(screen.getByText('dashboard.read')).toBeInTheDocument();
    expect(field('Policy-required capabilities')).toHaveValue('research.execute');
  });

  it('collects real capability scores instead of writing fixed demo values', async () => {
    renderPage(<AgentGovernancePage />);
    const assess = await screen.findByRole('button', { name: 'Assess capability' });
    expect(assess).toBeDisabled();
    fireEvent.change(field('Employee ID'), { target: { value: 'employee-1' } });
    for (const [label, value] of [['Model score (0–1)', '0.7'], ['History score (0–1)', '0.6'], ['Tool fit (0–1)', '0.8'], ['Context fit (0–1)', '0.9'], ['Confidence (0–1)', '0.75']]) {
      fireEvent.change(field(label), { target: { value } });
    }
    fireEvent.click(assess);
    await waitFor(() => expect(apiMocks.assessCapability).toHaveBeenCalledWith({
      employeeId: 'employee-1', dimension: 'engineering', modelScore: 0.7, historyScore: 0.6,
      toolFit: 0.8, contextFit: 0.9, confidence: 0.75
    }));
  });

  it('requires real review identities and a user-selected debate deadline', async () => {
    renderPage(<AgentGovernancePage />);
    await screen.findByRole('button', { name: 'Create review' });
    fireEvent.change(field('Review author'), { target: { value: '' } });
    fireEvent.change(field('Review subject ID'), { target: { value: 'subject-2' } });
    expect(screen.getByRole('button', { name: 'Create review' })).toBeDisabled();
    fireEvent.change(field('Review author'), { target: { value: 'reviewer@example.test' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create review' }));
    await waitFor(() => expect(apiMocks.createReviewTask).toHaveBeenCalledWith(expect.objectContaining({ author: 'reviewer@example.test' })));

    fireEvent.change(field('Review approver'), { target: { value: 'approver@example.test' } });
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }));
    await waitFor(() => expect(apiMocks.approveReviewTask).toHaveBeenCalledWith('review-1', 'approver@example.test'));

    fireEvent.change(field('Debate question'), { target: { value: 'Should this release proceed?' } });
    expect(screen.getByRole('button', { name: 'Open debate' })).toBeDisabled();
    fireEvent.change(field('Debate deadline'), { target: { value: '2099-01-01T12:00' } });
    fireEvent.click(screen.getByRole('button', { name: 'Open debate' }));
    await waitFor(() => expect(apiMocks.createDebate).toHaveBeenCalledWith(expect.objectContaining({ deadline: new Date('2099-01-01T12:00').toISOString() })));
  });

  it('does not attach a late disclosure result to changed policy inputs', async () => {
    let complete!: (value: { tools: { name: string; level: string }[] }) => void;
    apiMocks.discloseTools.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(<AgentGovernancePage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Disclose' }));
    await waitFor(() => expect(apiMocks.discloseTools).toHaveBeenCalledOnce());
    fireEvent.change(field('Policy-required capabilities'), { target: { value: 'research.execute,audit.read' } });
    await act(async () => complete({ tools: [{ name: 'research.search', level: 'Primary' }] }));

    expect(screen.queryByText('Primary: research.search')).not.toBeInTheDocument();
    expect(screen.queryByText('Tool disclosure evaluated.')).not.toBeInTheDocument();
  });

  it('clears stale disclosure output when policy inputs change', async () => {
    renderPage(<AgentGovernancePage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Disclose' }));
    expect(await screen.findByText('Primary: research.search')).toBeInTheDocument();
    fireEvent.change(field('Policy-required capabilities'), { target: { value: 'research.execute,audit.read' } });
    expect(screen.queryByText('Primary: research.search')).not.toBeInTheDocument();
  });

  it('does not attach a late disclosure error to changed policy inputs', async () => {
    let fail!: (reason: unknown) => void;
    apiMocks.discloseTools.mockReturnValue(new Promise((_resolve, reject) => { fail = reject; }));
    renderPage(<AgentGovernancePage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Disclose' }));
    await waitFor(() => expect(apiMocks.discloseTools).toHaveBeenCalledOnce());
    fireEvent.change(field('Policy-required capabilities'), { target: { value: 'research.execute,audit.read' } });
    await act(async () => { fail(new Error('Stale disclosure failed')); });

    expect(screen.queryByText('Stale disclosure failed')).not.toBeInTheDocument();
  });

  it('does not revive a disclosure error after policy inputs change away and back', async () => {
    apiMocks.discloseTools.mockRejectedValue(new Error('Disclosure conflict'));
    renderPage(<AgentGovernancePage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Disclose' }));
    expect(await screen.findByText('Disclosure conflict')).toBeInTheDocument();

    const capabilities = field('Policy-required capabilities');
    fireEvent.change(capabilities, { target: { value: 'research.execute,audit.read' } });
    expect(screen.queryByText('Disclosure conflict')).not.toBeInTheDocument();
    fireEvent.change(capabilities, { target: { value: 'research.execute' } });
    expect(screen.queryByText('Disclosure conflict')).not.toBeInTheDocument();
  });
});
