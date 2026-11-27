import { fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, renderPage } from '../../test/renderPage';
import { MemoryCaptureForm } from './MemoryCaptureForm';

const apiMocks = vi.hoisted(() => ({ captureMemory: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  apiMocks.captureMemory.mockReset();
  apiMocks.captureMemory.mockRejectedValue(new Error('Capture failed'));
});

describe('MemoryCaptureForm writes', () => {
  it('guards invalid submits and renders mutation errors', async () => {
    renderPage(<MemoryCaptureForm onCaptured={vi.fn()} />);
    const button = await screen.findByRole('button', { name: 'Capture claim' });
    const form = button.closest('form');
    if (!form) throw new Error('Capture form was not rendered.');
    fireEvent.submit(form);
    expect(apiMocks.captureMemory).not.toHaveBeenCalled();

    fireEvent.change(field('Content'), { target: { value: 'A traceable claim.' } });
    fireEvent.change(field('Source reference'), { target: { value: 'decision:auth-policy' } });
    fireEvent.click(button);
    await waitFor(() => expect(apiMocks.captureMemory).toHaveBeenCalledTimes(1));
    expect(await screen.findByText('Capture failed')).toBeInTheDocument();
  });
});
