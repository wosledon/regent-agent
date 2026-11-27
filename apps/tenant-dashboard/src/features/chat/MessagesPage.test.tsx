import { screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import { MessagesPage } from './MessagesPage';

const apiMocks = vi.hoisted(() => ({ conversations: vi.fn(), employees: vi.fn(), messages: vi.fn() }));
const authMocks = vi.hoisted(() => ({
  session: {
    sessionKind: 'Standard',
    effectiveAccess: { canReadChat: true, canReadCollaboration: true, collaborationScope: 'observer' }
  }
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));
vi.mock('../../app/useAuth', () => ({ useAuth: () => authMocks }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  authMocks.session.sessionKind = 'Standard';
  authMocks.session.effectiveAccess = { canReadChat: true, canReadCollaboration: true, collaborationScope: 'observer' };
  apiMocks.conversations.mockResolvedValue([{ id: 'conversation-1', title: 'Company HQ', kind: 'Company', memberEmployeeIds: [], createdBy: 'Owner', scopeType: 'Tenant', createdAt: '2026-11-25T10:00:00Z' }]);
  apiMocks.employees.mockResolvedValue([]);
  apiMocks.messages.mockRejectedValue(new Error('Messages unavailable'));
});

describe('MessagesPage thread states', () => {
  it('does not render an empty state together with a message error', async () => {
    renderPage(<MessagesPage />);
    expect(await screen.findByText('Messages unavailable')).toBeInTheDocument();
    expect(screen.queryByText('No messages')).not.toBeInTheDocument();
  });

  it('does not issue chat queries for an emergency session with denied effective access', async () => {
    authMocks.session.sessionKind = 'Emergency';
    authMocks.session.effectiveAccess = { canReadChat: false, canReadCollaboration: false, collaborationScope: 'none' };
    renderPage(<MessagesPage />);

    expect(await screen.findByText('Permission denied')).toBeInTheDocument();
    expect(apiMocks.conversations).not.toHaveBeenCalled();
    expect(apiMocks.employees).not.toHaveBeenCalled();
    expect(apiMocks.messages).not.toHaveBeenCalled();
  });

  it('never requests messages for a conversation id outside the authorized list', async () => {
    renderPage(<MessagesPage />, ['/messages?conversation=unknown-conversation']);

    expect(await screen.findByText('Conversation unavailable')).toBeInTheDocument();
    expect(apiMocks.messages).not.toHaveBeenCalled();
  });

  it('shows a localized observer empty state without write guidance', async () => {
    apiMocks.conversations.mockResolvedValue([]);
    renderPage(<MessagesPage />);

    expect(await screen.findByText('No shared collaboration conversations')).toBeInTheDocument();
    expect(screen.getByText(/Direct chats remain visible only to their participants/)).toBeInTheDocument();
    expect(screen.queryByText(/Create a group/)).not.toBeInTheDocument();
  });
});
