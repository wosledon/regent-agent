import { screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import { CoordinationPanel } from './CoordinationPanel';

const apiMocks = vi.hoisted(() => ({ collaborationRequests: vi.fn(), localization: vi.fn() }));
const authMocks = vi.hoisted(() => ({
  session: {
    effectiveAccess: { canReadChat: true, canReadCollaboration: true, collaborationScope: 'observer' }
  }
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));
vi.mock('../../app/useAuth', () => ({ useAuth: () => authMocks }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  authMocks.session.effectiveAccess = { canReadChat: true, canReadCollaboration: true, collaborationScope: 'observer' };
  apiMocks.collaborationRequests.mockResolvedValue({ items: [] });
  apiMocks.localization.mockResolvedValue({ timeZone: 'UTC' });
});

describe('CoordinationPanel access gating', () => {
  it('does not request coordination data without collaboration access', () => {
    authMocks.session.effectiveAccess = { canReadChat: true, canReadCollaboration: false, collaborationScope: 'participant' };
    renderPage(<CoordinationPanel employees={[]} />);

    expect(apiMocks.collaborationRequests).not.toHaveBeenCalled();
  });

  it('loads read-only coordination data for an observer', async () => {
    renderPage(<CoordinationPanel employees={[]} />);

    expect(await screen.findByText('No coordination requests')).toBeInTheDocument();
    expect(apiMocks.collaborationRequests).toHaveBeenCalledOnce();
  });
});
