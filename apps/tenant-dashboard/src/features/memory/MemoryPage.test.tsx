import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import { MemoryPage } from './MemoryPage';

const apiMocks = vi.hoisted(() => ({
  searchMemories: vi.fn(), rebuildMemoryIndex: vi.fn(), forgetMemory: vi.fn(), deleteMemory: vi.fn(),
  archiveMemory: vi.fn(), nominateMemory: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));
vi.mock('./MemoryReviewQueue', () => ({ MemoryReviewQueue: () => null }));
vi.mock('./MemorySafetyPanel', () => ({ MemorySafetyPanel: () => null }));
vi.mock('./MemoryImpactPanel', () => ({ MemoryImpactPanel: () => null }));
vi.mock('./MemoryCaptureForm', () => ({ MemoryCaptureForm: () => null }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.searchMemories.mockResolvedValue({
    hits: [{ claimId: 'claim-1', scope: 'organization', content: 'Recallable claim', version: 1, taint: 'trusted', score: 1, vectorScore: 1, keywordScore: 1, graphExpanded: false, conflictWithIds: [], reviewRequired: false, findingRules: [], sourceRef: 'source:1', confidence: 1 }],
    considered: 1, indexVersion: 1
  });
  apiMocks.deleteMemory.mockRejectedValue(new Error('Delete failed'));
});

describe('MemoryPage permanent deletion', () => {
  it('prevents duplicate permanent deletes while pending', async () => {
    let complete!: () => void;
    apiMocks.deleteMemory.mockReturnValue(new Promise<void>(resolve => { complete = resolve; }));
    renderPage(<MemoryPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Delete' }));
    const dialog = await screen.findByRole('dialog');
    const confirmButton = within(dialog).getByRole('button', { name: 'Delete' });
    fireEvent.click(confirmButton);
    await waitFor(() => expect(apiMocks.deleteMemory).toHaveBeenCalledTimes(1));
    expect(confirmButton).toBeDisabled();
    fireEvent.click(confirmButton);
    expect(apiMocks.deleteMemory).toHaveBeenCalledTimes(1);
    await act(async () => complete());
  });

  it('requires claim-identifying confirmation and renders delete errors', async () => {
    renderPage(<MemoryPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Delete' }));
    const dialog = await screen.findByRole('dialog');
    expect(apiMocks.deleteMemory).not.toHaveBeenCalled();
    expect(within(dialog).getByText('Permanently delete memory claim claim-1? This cannot be undone.')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'No' }));
    expect(apiMocks.deleteMemory).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(await screen.findByRole('button', { name: 'Delete' }));
    const reopenedDialog = await screen.findByRole('dialog');
    fireEvent.click(within(reopenedDialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(apiMocks.deleteMemory).toHaveBeenCalledWith('claim-1'));
    expect(await screen.findByText('Delete failed')).toBeInTheDocument();
  });
});
