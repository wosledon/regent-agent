import { fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, findField, renderPage } from '../../test/renderPage';
import { LifecyclePage } from './LifecyclePage';

const apiMocks = vi.hoisted(() => ({ lifecycle: vi.fn(), recordLicense: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.lifecycle.mockResolvedValue({ deletionRequests: [], deletionProofs: [], legalHolds: [], archives: [], backups: [], rights: [] });
  apiMocks.recordLicense.mockResolvedValue({});
});

describe('LifecyclePage license attestations', () => {
  it('requires explicit contributor and trademark attestations', async () => {
    renderPage(<LifecyclePage />);
    await findField('License artifact');
    fireEvent.change(field('License artifact'), { target: { value: 'artifact:release-1' } });
    const record = screen.getByRole('button', { name: 'Record license' });
    expect(record).toBeDisabled();

    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Contributor attestation' }));
    fireEvent.click(await screen.findByRole('option', { name: 'I confirm contributor rights for this artifact' }));
    expect(record).toBeDisabled();
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Trademark attestation' }));
    fireEvent.click(await screen.findByRole('option', { name: 'I completed the trademark review' }));
    expect(record).toBeEnabled();
    fireEvent.click(record);
    await waitFor(() => expect(apiMocks.recordLicense).toHaveBeenCalledWith(expect.objectContaining({
      artifactRef: 'artifact:release-1', contributorConfirmed: true, trademarkReviewed: true
    })));
  });
});
