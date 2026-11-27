import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { Route, Routes, useNavigate } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, findField, renderPage } from '../../test/renderPage';
import { ApprovalPage } from './ApprovalPage';
import i18n from '../../lib/i18n';

const authMocks = vi.hoisted(() => ({ session: { role: 'Owner', capabilities: [] as string[] } }));
const apiMocks = vi.hoisted(() => ({ approval: vi.fn(), decideApproval: vi.fn() }));
vi.mock('../../app/useAuth', () => ({ useAuth: () => authMocks }));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

const approval = {
  id: 'approval-1', actionType: 'release.execute', title: 'Production release', impact: 'High impact',
  severity: 'high', state: 'pending', requester: 'Owner', createdAt: '2026-11-25T10:00:00Z',
  expiresAt: '2026-11-25T11:00:00Z', options: [], evidenceRefs: ['evidence:1']
};

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  authMocks.session = { role: 'Owner', capabilities: [] };
  apiMocks.approval.mockResolvedValue(approval);
  apiMocks.decideApproval.mockResolvedValue({ ...approval, state: 'approved' });
});

function ApprovalNavigation() {
  const navigate = useNavigate();
  return <button onClick={() => navigate('/governance/approvals/approval-2')}>Open approval 2</button>;
}

function renderApproval() {
  return renderPage(<><ApprovalNavigation /><Routes><Route path="/governance/approvals/:approvalId" element={<ApprovalPage />} /></Routes></>, ['/governance/approvals/approval-1']);
}

async function openDecisionDialog(decision: 'Approve' | 'Reject') {
  fireEvent.click(screen.getByRole('button', { name: decision }));
  return screen.findByRole('dialog');
}

async function confirmDecision(decision: 'Approve' | 'Reject') {
  const dialog = await openDecisionDialog(decision);
  fireEvent.click(within(dialog).getByRole('button', { name: decision }));
}

async function waitForDecisionDialogClosed() {
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
}

describe('ApprovalPage decisions', () => {
  it('uses the shared loading state without rendering approval details', () => {
    apiMocks.approval.mockReturnValue(new Promise(() => undefined));
    renderApproval();
    expect(screen.getByRole('progressbar', { name: 'Loading approval' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Approval not found' })).not.toBeInTheDocument();
  });

  it('requires a non-default reason and explicit confirmation', async () => {
    renderApproval();
    const approve = await screen.findByRole('button', { name: 'Approve' });
    const reason = field('Decision reason');
    expect(reason).toHaveValue('');
    expect(approve).toBeDisabled();
    fireEvent.change(reason, { target: { value: 'Reviewed from Tenant Dashboard.' } });
    expect(approve).toBeDisabled();
    fireEvent.change(reason, { target: { value: 'Verified rollback evidence and release checks.' } });
    expect(approve).toBeEnabled();
    const dialog = await openDecisionDialog('Approve');
    expect(apiMocks.decideApproval).not.toHaveBeenCalled();
    expect(within(dialog).getByText(/Production release/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'No' }));
    await waitForDecisionDialogClosed();

    await confirmDecision('Approve');
    await waitFor(() => expect(apiMocks.decideApproval).toHaveBeenCalledWith('approval-1', {
      decision: 'approve', reason: 'Verified rollback evidence and release checks.'
    }, expect.any(String)));
    expect(await screen.findByText('Approval decision recorded.')).toBeInTheDocument();
  });

  it('rejects localized Chinese generic reasons while accepting a specific reason', async () => {
    await i18n.changeLanguage('zh-CN');
    renderApproval();
    const reason = await findField('决策原因');
    const approve = screen.getByRole('button', { name: '批准' });
    fireEvent.change(reason, { target: { value: '已审核。' } });
    expect(approve).toBeDisabled();
    fireEvent.change(reason, { target: { value: '已核对发布证据和回滚记录。' } });
    expect(approve).toBeEnabled();
  });

  it('prevents duplicate decisions while pending and after a recorded success', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.decideApproval.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderApproval();
    const reason = await findField('Decision reason');
    fireEvent.change(reason, { target: { value: 'Verified the release evidence and rollback plan.' } });
    const approve = screen.getByRole('button', { name: 'Approve' });
    await confirmDecision('Approve');
    await waitFor(() => expect(apiMocks.decideApproval).toHaveBeenCalledTimes(1));
    expect(approve).toBeDisabled();
    fireEvent.click(approve);
    expect(apiMocks.decideApproval).toHaveBeenCalledTimes(1);

    await act(async () => complete({ ...approval, state: 'approved' }));
    expect(await screen.findByText('Approval decision recorded.')).toBeInTheDocument();
    expect(approve).toBeDisabled();
    fireEvent.click(approve);
    expect(apiMocks.decideApproval).toHaveBeenCalledTimes(1);
  });

  it('resets reason and errors across direct route changes and targets the active approval', async () => {
    apiMocks.approval.mockImplementation(async (id: string) => ({ ...approval, id, title: `Approval ${id}` }));
    apiMocks.decideApproval.mockRejectedValueOnce(new Error('First decision conflict')).mockResolvedValueOnce({ ...approval, id: 'approval-2', state: 'approved' });
    renderApproval();
    fireEvent.change(await findField('Decision reason'), { target: { value: 'Checked the first approval evidence.' } });
    await confirmDecision('Reject');
    expect(await screen.findByText('First decision conflict')).toBeInTheDocument();
    await waitForDecisionDialogClosed();

    fireEvent.click(screen.getByRole('button', { name: 'Open approval 2' }));
    expect(await screen.findByRole('heading', { name: 'Approval approval-2' })).toBeInTheDocument();
    expect(field('Decision reason')).toHaveValue('');
    expect(screen.queryByText('First decision conflict')).not.toBeInTheDocument();
    fireEvent.change(field('Decision reason'), { target: { value: 'Checked the second approval evidence.' } });
    await confirmDecision('Approve');

    await waitFor(() => expect(apiMocks.decideApproval).toHaveBeenNthCalledWith(2, 'approval-2', {
      decision: 'approve', reason: 'Checked the second approval evidence.'
    }, expect.any(String)));
  });

  it('resets pending feedback and ignores a late response from the previous route', async () => {
    let completeFirst!: (value: unknown) => void;
    apiMocks.approval.mockImplementation(async (id: string) => ({ ...approval, id, title: `Approval ${id}` }));
    apiMocks.decideApproval.mockReturnValueOnce(new Promise(resolve => { completeFirst = resolve; })).mockResolvedValueOnce({ ...approval, id: 'approval-2', state: 'approved' });
    renderApproval();
    fireEvent.change(await findField('Decision reason'), { target: { value: 'Checked the first approval evidence.' } });
    await confirmDecision('Approve');
    await waitFor(() => expect(apiMocks.decideApproval).toHaveBeenCalledTimes(1));
    expect(field('Decision reason')).toBeDisabled();
    await waitForDecisionDialogClosed();

    fireEvent.click(screen.getByRole('button', { name: 'Open approval 2' }));
    expect(await screen.findByRole('heading', { name: 'Approval approval-2' })).toBeInTheDocument();
    const secondReason = field('Decision reason');
    expect(secondReason).toHaveValue('');
    expect(secondReason).toBeEnabled();
    fireEvent.change(secondReason, { target: { value: 'Checked the second approval evidence.' } });
    await act(async () => completeFirst({ ...approval, state: 'approved' }));

    expect(secondReason).toHaveValue('Checked the second approval evidence.');
    expect(screen.queryByText('Approval decision recorded.')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled();
    await confirmDecision('Approve');
    await waitFor(() => expect(apiMocks.decideApproval).toHaveBeenNthCalledWith(2, 'approval-2', {
      decision: 'approve', reason: 'Checked the second approval evidence.'
    }, expect.any(String)));
  });

  it('does not carry prior success feedback into another approval', async () => {
    apiMocks.approval.mockImplementation(async (id: string) => ({ ...approval, id, title: `Approval ${id}` }));
    renderApproval();
    fireEvent.change(await findField('Decision reason'), { target: { value: 'Checked the first approval evidence.' } });
    await confirmDecision('Approve');
    expect(await screen.findByText('Approval decision recorded.')).toBeInTheDocument();
    await waitForDecisionDialogClosed();

    fireEvent.click(screen.getByRole('button', { name: 'Open approval 2' }));
    expect(await screen.findByRole('heading', { name: 'Approval approval-2' })).toBeInTheDocument();
    expect(field('Decision reason')).toHaveValue('');
    expect(screen.queryByText('Approval decision recorded.')).not.toBeInTheDocument();
  });

  it('hides decision controls without owner or tenant.write authority', async () => {
    authMocks.session = { role: 'Member', capabilities: [] };
    renderApproval();
    expect(await screen.findByText(/Permission denied: Owner, Co-owner or tenant.write/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Decision reason')).not.toBeInTheDocument();
  });

  it('allows a CoOwner session to decide with a specific reason', async () => {
    authMocks.session = { role: 'CoOwner', capabilities: [] };
    renderApproval();
    const reason = await findField('Decision reason');
    fireEvent.change(reason, { target: { value: 'Verified the named rollback evidence for this release.' } });
    const approve = screen.getByRole('button', { name: 'Approve' });
    expect(approve).toBeEnabled();
    await confirmDecision('Approve');
    await waitFor(() => expect(apiMocks.decideApproval).toHaveBeenCalledOnce());
  });

  it('allows a session with tenant.write and renders decision errors', async () => {
    authMocks.session = { role: 'Member', capabilities: ['tenant.write'] };
    apiMocks.decideApproval.mockRejectedValue(new Error('Decision conflict'));
    renderApproval();
    const reason = await findField('Decision reason');
    fireEvent.change(reason, { target: { value: 'Checked the specific evidence reference.' } });
    await confirmDecision('Reject');
    expect(await screen.findByText('Decision conflict')).toBeInTheDocument();
  });
});
