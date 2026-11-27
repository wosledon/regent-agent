import { screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import { TracePanel } from './TracePanel';

const apiMocks = vi.hoisted(() => ({ messages: vi.fn(), collaborationTrace: vi.fn() }));
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
  apiMocks.messages.mockRejectedValue(new Error('Trace messages unavailable'));
  apiMocks.collaborationTrace.mockResolvedValue({ activities: [] });
});

describe('TracePanel states', () => {
  it('does not render no-messages together with a message error', async () => {
    renderPage(<TracePanel node={{ id: 'conversation:1', type: 'Conversation', label: 'Company HQ', subtitle: '', status: 'Active', conversationId: 'conversation-1', memberEmployeeIds: [], depth: 0, x: 0, y: 0, activityCount: 0 }} employees={[]} onClose={vi.fn()} />);
    expect(await screen.findByText('Trace messages unavailable')).toBeInTheDocument();
    expect(screen.queryByText('No messages')).not.toBeInTheDocument();
  });

  it('gates trace and message queries and cross-links by effective access', async () => {
    authMocks.session.effectiveAccess = { canReadChat: false, canReadCollaboration: false, collaborationScope: 'none' };
    renderPage(<TracePanel node={{ id: 'conversation:1', type: 'Conversation', label: 'Company HQ', subtitle: '', status: 'Active', conversationId: 'conversation-1', memberEmployeeIds: [], depth: 0, x: 0, y: 0, activityCount: 0 }} employees={[]} onClose={vi.fn()} />);

    expect(screen.queryByRole('link', { name: 'Open messages' })).not.toBeInTheDocument();
    expect(apiMocks.collaborationTrace).not.toHaveBeenCalled();
    expect(apiMocks.messages).not.toHaveBeenCalled();
  });
});
