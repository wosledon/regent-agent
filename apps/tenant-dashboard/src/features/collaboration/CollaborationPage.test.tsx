import { act, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import { CollaborationPage } from './CollaborationPage';
import i18n from '../../lib/i18n';
import { collaborationFreshness, collaborationStaleAfterMs, isCollaborationView } from './collaborationView';

const apiMocks = vi.hoisted(() => ({ collaborationGraph: vi.fn(), employees: vi.fn() }));
const authMocks = vi.hoisted(() => ({
  session: {
    sessionKind: 'Standard',
    effectiveAccess: { canReadChat: true, canReadCollaboration: true, collaborationScope: 'observer' }
  }
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));
vi.mock('../../app/useAuth', () => ({ useAuth: () => authMocks }));
vi.mock('./OfficeView', () => ({ OfficeView: () => <div>Office view</div> }));
vi.mock('./OrgChartView', () => ({ OrgChartView: () => <div>Org view</div> }));
vi.mock('./NetworkView', () => ({ NetworkView: () => <div>Network view</div> }));
vi.mock('./ActivityTimeline', () => ({ ActivityTimeline: () => <div>Timeline view</div> }));
vi.mock('./CoordinationPanel', () => ({ CoordinationPanel: () => null }));
vi.mock('./TracePanel', () => ({ TracePanel: () => null }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  authMocks.session.sessionKind = 'Standard';
  authMocks.session.effectiveAccess = { canReadChat: true, canReadCollaboration: true, collaborationScope: 'observer' };
  apiMocks.collaborationGraph.mockResolvedValue({
    nodes: [], edges: [], activities: [], generatedAt: new Date().toISOString(),
    summary: { employeeCount: 0, conversationCount: 0, collaborationCount: 0, activityCount: 0, coordinationCount: 0 }
  });
  apiMocks.employees.mockResolvedValue([]);
});

describe('CollaborationPage view and freshness', () => {
  it('does not issue collaboration queries for an emergency session with denied effective access', async () => {
    authMocks.session.sessionKind = 'Emergency';
    authMocks.session.effectiveAccess = { canReadChat: false, canReadCollaboration: false, collaborationScope: 'none' };
    renderPage(<CollaborationPage />);

    expect(await screen.findByText('Permission denied')).toBeInTheDocument();
    expect(apiMocks.collaborationGraph).not.toHaveBeenCalled();
    expect(apiMocks.employees).not.toHaveBeenCalled();
  });

  it('labels observer access without write guidance', async () => {
    renderPage(<CollaborationPage />);
    expect(await screen.findByText('Owner read-only observer')).toBeInTheDocument();
  });

  it('falls back to office for an unsupported view query', async () => {
    renderPage(<CollaborationPage />, ['/?view=unsupported']);
    expect(await screen.findByText('Office view')).toBeInTheDocument();
    expect(screen.queryByText('Network view')).not.toBeInTheDocument();
  });

  it('changes representative page copy and document language with the active locale', async () => {
    renderPage(<CollaborationPage />);
    expect(await screen.findByRole('heading', { name: 'Organization collaboration' })).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('en-US');

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    expect(screen.getByRole('heading', { name: '组织协作' })).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('zh-CN');
  });

  it('derives freshness and uses unknown for missing or invalid timestamps', () => {
    const now = Date.parse('2026-11-25T12:00:00Z');
    expect(collaborationFreshness(undefined, now)).toBe('unknown');
    expect(collaborationFreshness('not-a-date', now)).toBe('unknown');
    expect(collaborationFreshness('2026-11-25T11:59:00Z', now)).toBe('fresh');
    expect(collaborationFreshness(new Date(now - collaborationStaleAfterMs).toISOString(), now)).toBe('fresh');
    expect(collaborationFreshness(new Date(now - collaborationStaleAfterMs - 1).toISOString(), now)).toBe('stale');
    expect(collaborationFreshness('2026-11-25T11:00:00Z', now)).toBe('stale');
  });

  it('recognizes only supported collaboration views', () => {
    expect(isCollaborationView('office')).toBe(true);
    expect(isCollaborationView('org')).toBe(true);
    expect(isCollaborationView('network')).toBe(true);
    expect(isCollaborationView('timeline')).toBe(true);
    expect(isCollaborationView('unsupported')).toBe(false);
    expect(isCollaborationView('')).toBe(false);
  });
});
