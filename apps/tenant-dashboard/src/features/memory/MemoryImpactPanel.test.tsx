import { fireEvent, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, renderPage } from '../../test/renderPage';
import { MemoryImpactPanel } from './MemoryImpactPanel';

const apiMocks = vi.hoisted(() => ({ memoryImpact: vi.fn(), recordMemoryImpact: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.memoryImpact.mockResolvedValue([]);
  apiMocks.recordMemoryImpact.mockRejectedValue(new Error('Impact write failed'));
});

describe('MemoryImpactPanel mutations', () => {
  it('surfaces refresh failures without an unhandled rejection', async () => {
    apiMocks.recordMemoryImpact.mockResolvedValue({});
    renderPage(<MemoryImpactPanel claimId="claim-1" onChanged={vi.fn().mockRejectedValue(new Error('Impact refresh failed'))} />);
    const record = await screen.findByRole('button', { name: 'Record impact' });
    fireEvent.change(field('Target ID'), { target: { value: 'task-1' } });
    fireEvent.change(field('Risk'), { target: { value: 'High' } });
    fireEvent.change(field('Required action'), { target: { value: 'Invalidate the task context and retrieve again.' } });
    fireEvent.click(record);
    expect(await screen.findByText('Impact refresh failed')).toBeInTheDocument();
  });

  it('guards required content and renders mutation errors', async () => {
    renderPage(<MemoryImpactPanel claimId="claim-1" onChanged={vi.fn()} />);
    const record = await screen.findByRole('button', { name: 'Record impact' });
    expect(record).toBeDisabled();
    fireEvent.change(field('Target ID'), { target: { value: 'task-1' } });
    fireEvent.change(field('Risk'), { target: { value: 'High' } });
    fireEvent.change(field('Required action'), { target: { value: 'Invalidate the task context and retrieve again.' } });
    fireEvent.click(record);
    expect(await screen.findByText('Impact write failed')).toBeInTheDocument();
  });
});
