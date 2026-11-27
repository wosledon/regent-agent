import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, renderPage } from '../../test/renderPage';
import { MemorySafetyPanel } from './MemorySafetyPanel';

const apiMocks = vi.hoisted(() => ({ memoryFindings: vi.fn(), memoryCorrections: vi.fn(), quarantineMemoryFinding: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));
vi.mock('../../app/useAuth', () => ({ useAuth: () => ({ session: { displayName: 'Owner' } }) }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.memoryFindings.mockResolvedValue([{ id: 'finding-1', claimId: 'claim-1', ruleId: 'content.injection', riskLevel: 'High', status: 'Open' }]);
  apiMocks.memoryCorrections.mockResolvedValue([]);
  apiMocks.quarantineMemoryFinding.mockRejectedValue(new Error('Quarantine failed'));
});

describe('MemorySafetyPanel mutations', () => {
  it('requires a reason and renders quarantine errors', async () => {
    renderPage(<MemorySafetyPanel onChanged={vi.fn()} />);
    const quarantine = await screen.findByRole('button', { name: 'Quarantine' });
    expect(quarantine).toBeDisabled();
    fireEvent.change(field('Quarantine reason'), { target: { value: 'Confirmed injected instruction in source evidence.' } });
    fireEvent.click(quarantine);
    expect(await screen.findByText('Quarantine failed')).toBeInTheDocument();
    expect(quarantine).toBeEnabled();
  });

  it('prevents duplicate safety writes and clears pending feedback after completion', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.quarantineMemoryFinding.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(<MemorySafetyPanel onChanged={vi.fn()} />);
    const quarantine = await screen.findByRole('button', { name: 'Quarantine' });
    fireEvent.change(field('Quarantine reason'), { target: { value: 'Confirmed injected instruction in source evidence.' } });
    fireEvent.click(quarantine);
    await waitFor(() => expect(apiMocks.quarantineMemoryFinding).toHaveBeenCalledTimes(1));
    expect(quarantine).toBeDisabled();
    fireEvent.click(quarantine);
    expect(apiMocks.quarantineMemoryFinding).toHaveBeenCalledTimes(1);
    await act(async () => complete({}));
    expect(await screen.findByText('Memory safety action completed.')).toBeInTheDocument();
  });
});
