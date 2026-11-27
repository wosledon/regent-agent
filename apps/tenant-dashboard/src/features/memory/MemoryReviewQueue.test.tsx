import { fireEvent, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import { MemoryReviewQueue } from './MemoryReviewQueue';

const apiMocks = vi.hoisted(() => ({ memoryReviewQueue: vi.fn(), publishMemory: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.memoryReviewQueue.mockResolvedValue([{ id: 'claim-1', scope: 'organization', content: 'Candidate claim', sourceRef: 'source:1', author: 'Owner', confidence: 0.9 }]);
  apiMocks.publishMemory.mockRejectedValue(new Error('Publish failed'));
});

describe('MemoryReviewQueue mutations', () => {
  it('keeps success feedback visible when the refreshed queue becomes empty', async () => {
    apiMocks.publishMemory.mockImplementation(async () => {
      apiMocks.memoryReviewQueue.mockResolvedValue([]);
      return {};
    });
    const { client } = renderPage(<MemoryReviewQueue onChanged={() => client.refetchQueries({ queryKey: ['memory-review-queue'] })} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Publish' }));

    expect(await screen.findByRole('heading', { name: 'No candidate claims' })).toBeInTheDocument();
    expect(await screen.findByText('Publication review recorded.')).toBeInTheDocument();
  });

  it('surfaces refresh failures from the guarded success handler', async () => {
    apiMocks.publishMemory.mockResolvedValue({});
    renderPage(<MemoryReviewQueue onChanged={vi.fn().mockRejectedValue(new Error('Queue refresh failed'))} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Publish' }));
    expect(await screen.findByText('Queue refresh failed')).toBeInTheDocument();
  });

  it('renders publish errors and permits a controlled retry', async () => {
    renderPage(<MemoryReviewQueue onChanged={vi.fn()} />);
    const publish = await screen.findByRole('button', { name: 'Publish' });
    fireEvent.click(publish);
    expect(await screen.findByText('Publish failed')).toBeInTheDocument();
    expect(publish).toBeEnabled();
  });
});
