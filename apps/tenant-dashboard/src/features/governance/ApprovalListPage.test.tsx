import { screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import { ApprovalListPage } from './ApprovalListPage';

const apiMocks = vi.hoisted(() => ({ dashboard: vi.fn() }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => { apiMocks.dashboard.mockReset(); });

describe('ApprovalListPage states', () => {
  it('uses the shared loading state without rendering an empty result', () => {
    apiMocks.dashboard.mockReturnValue(new Promise(() => undefined));
    renderPage(<ApprovalListPage />);
    expect(screen.getByRole('progressbar', { name: 'Loading approvals' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'No pending approvals' })).not.toBeInTheDocument();
  });

  it('uses the shared empty state when no approvals are pending', async () => {
    apiMocks.dashboard.mockResolvedValue({ actions: [] });
    renderPage(<ApprovalListPage />);
    expect(await screen.findByRole('heading', { name: 'No pending approvals' })).toBeInTheDocument();
  });

  it('uses the shared error state with retry for query failures', async () => {
    apiMocks.dashboard.mockRejectedValue(new Error('Dashboard unavailable'));
    renderPage(<ApprovalListPage />);
    expect(await screen.findByText('Dashboard unavailable')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
