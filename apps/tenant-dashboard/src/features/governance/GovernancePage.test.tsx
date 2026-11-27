import { fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, renderPage } from '../../test/renderPage';
import { GovernancePage } from './GovernancePage';

const apiMocks = vi.hoisted(() => ({
  governance: vi.fn(), containIncident: vi.fn(), recoverIncident: vi.fn(), closeIncident: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.governance.mockResolvedValue({
    incidents: [{ id: 'incident-1', title: 'Credential exposure', severity: 'SEV2', status: 'Open' }],
    tools: [], approvalDelegations: [], contentFindings: []
  });
  apiMocks.containIncident.mockResolvedValue({});
  apiMocks.recoverIncident.mockResolvedValue({});
  apiMocks.closeIncident.mockResolvedValue({});
});

async function selectIncident(label: string) {
  fireEvent.mouseDown(await screen.findByRole('combobox', { name: 'Incident response' }));
  fireEvent.click(await screen.findByRole('option', { name: label }));
}

describe('GovernancePage incident response', () => {
  it('records only user-entered containment content', async () => {
    renderPage(<GovernancePage />);
    await selectIncident('Credential exposure · Open');
    const contain = screen.getByRole('button', { name: 'Record containment' });
    expect(contain).toBeDisabled();
    fireEvent.change(field('Containment action'), { target: { value: '   ' } });
    expect(contain).toBeDisabled();
    fireEvent.click(contain);
    expect(apiMocks.containIncident).not.toHaveBeenCalled();
    fireEvent.change(field('Containment action'), { target: { value: 'Revoked token family abc and isolated runner-2.' } });
    fireEvent.click(contain);
    await waitFor(() => expect(apiMocks.containIncident).toHaveBeenCalledWith('incident-1', 'Revoked token family abc and isolated runner-2.'));
  });

  it('renders containment errors and keeps the entered content available for retry', async () => {
    apiMocks.containIncident.mockRejectedValue(new Error('Containment conflict'));
    renderPage(<GovernancePage />);
    await selectIncident('Credential exposure · Open');
    fireEvent.change(field('Containment action'), { target: { value: 'Revoked token family abc and isolated runner-2.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record containment' }));
    expect(await screen.findByText('Containment conflict')).toBeInTheDocument();
    expect(field('Containment action')).toHaveValue('Revoked token family abc and isolated runner-2.');
    expect(screen.getByRole('button', { name: 'Record containment' })).toBeEnabled();
  });

  it('requires actual recovery and retrospective summaries', async () => {
    apiMocks.governance.mockResolvedValue({
      incidents: [{ id: 'incident-1', title: 'Credential exposure', severity: 'SEV2', status: 'Contained' }],
      tools: [], approvalDelegations: [], contentFindings: []
    });
    const first = renderPage(<GovernancePage />);
    await selectIncident('Credential exposure · Contained');
    expect(screen.getByRole('button', { name: 'Record recovery' })).toBeDisabled();
    fireEvent.change(field('Recovery summary'), { target: { value: '   ' } });
    expect(screen.getByRole('button', { name: 'Record recovery' })).toBeDisabled();
    fireEvent.change(field('Recovery summary'), { target: { value: 'Rotated credentials and verified clean login traces.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record recovery' }));
    await waitFor(() => expect(apiMocks.recoverIncident).toHaveBeenCalledWith('incident-1', 'Rotated credentials and verified clean login traces.'));
    first.unmount();

    apiMocks.governance.mockResolvedValue({
      incidents: [{ id: 'incident-1', title: 'Credential exposure', severity: 'SEV2', status: 'Recovered' }],
      tools: [], approvalDelegations: [], contentFindings: []
    });
    renderPage(<GovernancePage />);
    await selectIncident('Credential exposure · Recovered');
    expect(screen.getByRole('button', { name: 'Close with retrospective' })).toBeDisabled();
    fireEvent.change(field('Retrospective'), { target: { value: '   ' } });
    expect(screen.getByRole('button', { name: 'Close with retrospective' })).toBeDisabled();
    fireEvent.change(field('Retrospective'), { target: { value: 'Root cause was an over-scoped token; follow-up owner and due date recorded.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Close with retrospective' }));
    await waitFor(() => expect(apiMocks.closeIncident).toHaveBeenCalledWith('incident-1', expect.stringContaining('over-scoped token')));
  });
});
