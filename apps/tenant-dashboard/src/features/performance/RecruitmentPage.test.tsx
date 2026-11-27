import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, renderPage } from '../../test/renderPage';
import { RecruitmentPage } from './RecruitmentPage';
import i18n from '../../lib/i18n';

const apiMocks = vi.hoisted(() => ({
  recruitmentInterviews: vi.fn(), recruitmentAppeals: vi.fn(), fairnessAudits: vi.fn(), positions: vi.fn(), candidates: vi.fn(),
  recordInterview: vi.fn(), createCandidateAppeal: vi.fn(), resolveCandidateAppeal: vi.fn(), runFairnessAudit: vi.fn(),
  rejectCandidate: vi.fn(), retryCandidate: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

const candidate = { id: 'candidate-1', positionId: 'position-1', displayName: 'Candidate A', modelVersion: 'v1', status: 'Screening', assessmentJson: '{}', fairnessJson: '{}', jobDescription: 'Engineer' };
const secondCandidate = { ...candidate, id: 'candidate-2', displayName: 'Candidate B' };
const appeals = [
  { id: 'appeal-1', candidateId: 'candidate-1', reason: 'Review requested', status: 'Open' },
  { id: 'appeal-2', candidateId: 'candidate-2', reason: 'Second review requested', status: 'Open' }
];

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.recruitmentInterviews.mockResolvedValue([]);
  apiMocks.recruitmentAppeals.mockResolvedValue([{ id: 'appeal-1', candidateId: 'candidate-1', reason: 'Review requested', status: 'Open' }]);
  apiMocks.fairnessAudits.mockResolvedValue([]);
  apiMocks.positions.mockResolvedValue([{ id: 'position-1', name: 'Engineer' }]);
  apiMocks.candidates.mockResolvedValue([candidate]);
  apiMocks.recordInterview.mockResolvedValue({});
  apiMocks.resolveCandidateAppeal.mockResolvedValue({});
  apiMocks.runFairnessAudit.mockResolvedValue({});
  apiMocks.rejectCandidate.mockResolvedValue({});
});

async function choose(label: string, option: string) {
  fireEvent.mouseDown(screen.getByRole('combobox', { name: label }));
  fireEvent.click(await screen.findByRole('option', { name: option }));
}

async function prepareRejection() {
  await screen.findByText('Candidate A');
  await choose('Candidate to reject', 'Candidate A');
  fireEvent.change(field('Job-related rejection reason'), { target: { value: 'Did not demonstrate the required distributed systems depth.' } });
  fireEvent.change(field('Rejection evidence'), { target: { value: 'Panel rubric missed required system design evidence.' } });
  return screen.getByRole('button', { name: 'Reject candidate' });
}

async function confirmRejection(candidateName = 'Candidate A') {
  const dialog = await screen.findByRole('dialog');
  expect(within(dialog).getByText(new RegExp(`Reject ${candidateName}`))).toBeInTheDocument();
  fireEvent.change(within(dialog).getByRole('textbox', { name: candidateName }), { target: { value: candidateName } });
  fireEvent.click(within(dialog).getByRole('button', { name: 'Reject candidate' }));
}

async function prepareResolution() {
  await screen.findByText('Candidate A');
  await choose('Appeal to resolve', 'candidate-1 · Review requested');
  fireEvent.change(field('Appeal resolution'), { target: { value: 'Independent panel rechecked rubric and upheld the decision.' } });
  return screen.getByRole('button', { name: 'Resolve appeal' });
}

async function prepareAudit() {
  await screen.findByText('Candidate A');
  await choose('Position', 'Engineer');
  return screen.getByRole('button', { name: 'Run audit' });
}

describe('RecruitmentPage validation and evidence', () => {
  it('validates interview score and JSON evidence', async () => {
    renderPage(<RecruitmentPage />);
    await screen.findByText('Candidate A');
    await choose('Interview candidate', 'Candidate A');
    const record = screen.getByRole('button', { name: 'Record stage' });
    fireEvent.change(field('Score'), { target: { value: '101' } });
    expect(record).toBeDisabled();
    fireEvent.change(field('Score'), { target: { value: '88' } });
    fireEvent.change(field('Evidence JSON'), { target: { value: '{bad' } });
    expect(record).toBeDisabled();
    fireEvent.change(field('Evidence JSON'), { target: { value: '{"interview":"panel-1"}' } });
    expect(record).toBeEnabled();
    fireEvent.click(record);
    await waitFor(() => expect(apiMocks.recordInterview).toHaveBeenCalledWith(expect.objectContaining({ candidateId: 'candidate-1', score: 88, evidenceJson: '{"interview":"panel-1"}' })));
  });

  it('prevents duplicate interview writes while pending', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.recordInterview.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(<RecruitmentPage />);
    await screen.findByText('Candidate A');
    await choose('Interview candidate', 'Candidate A');
    fireEvent.change(field('Evidence JSON'), { target: { value: '{"interview":"panel-1"}' } });
    const record = screen.getByRole('button', { name: 'Record stage' });
    fireEvent.click(record);
    await waitFor(() => expect(apiMocks.recordInterview).toHaveBeenCalledTimes(1));
    expect(record).toBeDisabled();
    fireEvent.click(record);
    expect(apiMocks.recordInterview).toHaveBeenCalledTimes(1);
    await act(async () => complete({}));
  });

  it('requires candidate-specific rejection evidence and confirmation', async () => {
    renderPage(<RecruitmentPage />);
    await screen.findByText('Candidate A');
    await choose('Candidate to reject', 'Candidate A');
    const reject = screen.getByRole('button', { name: 'Reject candidate' });
    expect(reject).toBeDisabled();
    fireEvent.change(field('Job-related rejection reason'), { target: { value: 'Job-related evidence did not meet the bar.' } });
    fireEvent.change(field('Rejection evidence'), { target: { value: 'Panel rubric missed required system design evidence.' } });
    expect(reject).toBeDisabled();
    expect(screen.getByText(/Job-related rejection reason must be specific/)).toBeInTheDocument();
    fireEvent.change(field('Job-related rejection reason'), { target: { value: 'Did not demonstrate the required distributed systems depth.' } });
    expect(reject).toBeEnabled();
    fireEvent.click(reject);
    expect(apiMocks.rejectCandidate).not.toHaveBeenCalled();
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/Reject Candidate A/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Reject candidate' })).toBeDisabled();
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Candidate A' }), { target: { value: 'Candidate A' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reject candidate' }));
    await waitFor(() => expect(apiMocks.rejectCandidate).toHaveBeenCalledWith('candidate-1', expect.stringContaining('Panel rubric missed required system design evidence.')));
  });

  it('rejects localized Chinese generic rejection and appeal-resolution reasons', async () => {
    await i18n.changeLanguage('zh-CN');
    renderPage(<RecruitmentPage />);
    await screen.findByText('Candidate A');
    await choose('要拒绝的候选人', 'Candidate A');
    fireEvent.change(field('岗位相关拒绝原因'), { target: { value: '岗位相关证据未达到要求。' } });
    fireEvent.change(field('拒绝证据'), { target: { value: '评审小组已核对系统设计证据。' } });
    const reject = screen.getByRole('button', { name: '拒绝候选人' });
    expect(reject).toBeDisabled();
    fireEvent.change(field('岗位相关拒绝原因'), { target: { value: '缺少系统设计要求的深度。' } });
    expect(reject).toBeEnabled();

    await choose('要处理的申诉', 'candidate-1 · Review requested');
    fireEvent.change(field('申诉处理结果'), { target: { value: '复测已完成。' } });
    const resolve = screen.getByRole('button', { name: '处理申诉' });
    expect(resolve).toBeDisabled();
    fireEvent.change(field('申诉处理结果'), { target: { value: '独立评审组复核了评分标准并维持原决定。' } });
    expect(resolve).toBeEnabled();
  });

  it('collects a real appeal resolution and validates fairness inputs', async () => {
    renderPage(<RecruitmentPage />);
    await screen.findByText('Candidate A');
    await choose('Appeal to resolve', 'candidate-1 · Review requested');
    const resolve = screen.getByRole('button', { name: 'Resolve appeal' });
    expect(resolve).toBeDisabled();
    fireEvent.change(field('Appeal resolution'), { target: { value: 'Retest completed.' } });
    expect(resolve).toBeDisabled();
    expect(screen.getByText(/Appeal resolution must be specific/)).toBeInTheDocument();
    fireEvent.change(field('Appeal resolution'), { target: { value: 'Independent panel rechecked rubric and upheld the decision.' } });
    fireEvent.click(resolve);
    await waitFor(() => expect(apiMocks.resolveCandidateAppeal).toHaveBeenCalledWith('appeal-1', 'Independent panel rechecked rubric and upheld the decision.'));

    await choose('Position', 'Engineer');
    fireEvent.change(field('Sample size'), { target: { value: '0' } });
    expect(screen.getByRole('button', { name: 'Run audit' })).toBeDisabled();
    fireEvent.change(field('Sample size'), { target: { value: '20' } });
    fireEvent.change(field('Selection rate ratio'), { target: { value: 'Infinity' } });
    expect(screen.getByRole('button', { name: 'Run audit' })).toBeDisabled();
  });

  it('renders rejection errors, retains evidence and prevents duplicate retries', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.rejectCandidate.mockRejectedValueOnce(new Error('Rejection conflict')).mockReturnValueOnce(new Promise(resolve => { complete = resolve; }));
    renderPage(<RecruitmentPage />);
    const reject = await prepareRejection();

    fireEvent.click(reject);
    await confirmRejection();
    expect(await screen.findByText('Rejection conflict')).toBeInTheDocument();
    expect(field('Job-related rejection reason')).toHaveValue('Did not demonstrate the required distributed systems depth.');
    expect(field('Rejection evidence')).toHaveValue('Panel rubric missed required system design evidence.');
    fireEvent.click(reject);
    await confirmRejection();
    await waitFor(() => expect(apiMocks.rejectCandidate).toHaveBeenCalledTimes(2));
    expect(reject).toBeDisabled();
    fireEvent.click(reject);
    expect(apiMocks.rejectCandidate).toHaveBeenCalledTimes(2);
    await act(async () => complete({}));
    expect(await screen.findByText('Candidate rejected with job-related evidence.')).toBeInTheDocument();
  });

  it('renders appeal-resolution errors, retains the resolution and prevents duplicate retries', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.resolveCandidateAppeal.mockRejectedValueOnce(new Error('Appeal resolution conflict')).mockReturnValueOnce(new Promise(resolve => { complete = resolve; }));
    renderPage(<RecruitmentPage />);
    const resolve = await prepareResolution();

    fireEvent.click(resolve);
    expect(await screen.findByText('Appeal resolution conflict')).toBeInTheDocument();
    expect(field('Appeal resolution')).toHaveValue('Independent panel rechecked rubric and upheld the decision.');
    fireEvent.click(resolve);
    await waitFor(() => expect(apiMocks.resolveCandidateAppeal).toHaveBeenCalledTimes(2));
    expect(resolve).toBeDisabled();
    fireEvent.click(resolve);
    expect(apiMocks.resolveCandidateAppeal).toHaveBeenCalledTimes(2);
    await act(async () => complete({}));
    expect(await screen.findByText('Appeal resolution recorded.')).toBeInTheDocument();
  });

  it('keeps rejection drafts with their candidate and submits only the active candidate', async () => {
    apiMocks.candidates.mockResolvedValue([candidate, secondCandidate]);
    renderPage(<RecruitmentPage />);
    await screen.findByText('Candidate A');
    await choose('Candidate to reject', 'Candidate A');
    fireEvent.change(field('Job-related rejection reason'), { target: { value: 'Candidate A missed the system design rubric.' } });
    fireEvent.change(field('Rejection evidence'), { target: { value: 'Candidate A panel evidence reference.' } });

    await choose('Candidate to reject', 'Candidate B');
    expect(field('Job-related rejection reason')).toHaveValue('');
    expect(field('Rejection evidence')).toHaveValue('');
    fireEvent.change(field('Job-related rejection reason'), { target: { value: 'Candidate B missed the coding rubric.' } });
    fireEvent.change(field('Rejection evidence'), { target: { value: 'Candidate B panel evidence reference.' } });
    await choose('Candidate to reject', 'Candidate A');
    expect(field('Job-related rejection reason')).toHaveValue('Candidate A missed the system design rubric.');
    expect(field('Rejection evidence')).toHaveValue('Candidate A panel evidence reference.');

    await choose('Candidate to reject', 'Candidate B');
    fireEvent.click(screen.getByRole('button', { name: 'Reject candidate' }));
    await confirmRejection('Candidate B');
    await waitFor(() => expect(apiMocks.rejectCandidate).toHaveBeenCalledTimes(1));
    const [submittedId, submittedReason] = apiMocks.rejectCandidate.mock.calls[0];
    expect(submittedId).toBe('candidate-2');
    expect(submittedReason).toContain('Candidate B missed the coding rubric.');
    expect(submittedReason).toContain('Candidate B panel evidence reference.');
    expect(submittedReason).not.toContain('Candidate A');
  });

  it('does not erase another candidate draft when an earlier rejection completes', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.candidates.mockResolvedValue([candidate, secondCandidate]);
    apiMocks.rejectCandidate.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(<RecruitmentPage />);
    await screen.findByText('Candidate A');
    await choose('Candidate to reject', 'Candidate A');
    fireEvent.change(field('Job-related rejection reason'), { target: { value: 'Candidate A rejection reason.' } });
    fireEvent.change(field('Rejection evidence'), { target: { value: 'Candidate A rejection evidence.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reject candidate' }));
    await confirmRejection();
    await waitFor(() => expect(apiMocks.rejectCandidate).toHaveBeenCalledWith('candidate-1', expect.stringContaining('Candidate A rejection reason.')));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await choose('Candidate to reject', 'Candidate B');
    fireEvent.change(field('Job-related rejection reason'), { target: { value: 'Candidate B rejection reason.' } });
    fireEvent.change(field('Rejection evidence'), { target: { value: 'Candidate B rejection evidence.' } });
    await act(async () => complete({}));
    expect(await screen.findByText('Candidate rejected with job-related evidence.')).toBeInTheDocument();
    expect(field('Job-related rejection reason')).toHaveValue('Candidate B rejection reason.');
    expect(field('Rejection evidence')).toHaveValue('Candidate B rejection evidence.');

    await choose('Candidate to reject', 'Candidate A');
    expect(field('Job-related rejection reason')).toHaveValue('');
    expect(field('Rejection evidence')).toHaveValue('');
  });

  it('keeps appeal resolutions with their appeal and submits only the active appeal', async () => {
    apiMocks.candidates.mockResolvedValue([candidate, secondCandidate]);
    apiMocks.recruitmentAppeals.mockResolvedValue(appeals);
    renderPage(<RecruitmentPage />);
    await screen.findByText('Candidate A');
    await choose('Appeal to resolve', 'candidate-1 · Review requested');
    fireEvent.change(field('Appeal resolution'), { target: { value: 'First appeal panel resolution.' } });

    await choose('Appeal to resolve', 'candidate-2 · Second review requested');
    expect(field('Appeal resolution')).toHaveValue('');
    fireEvent.change(field('Appeal resolution'), { target: { value: 'Second appeal panel resolution.' } });
    await choose('Appeal to resolve', 'candidate-1 · Review requested');
    expect(field('Appeal resolution')).toHaveValue('First appeal panel resolution.');

    await choose('Appeal to resolve', 'candidate-2 · Second review requested');
    fireEvent.click(screen.getByRole('button', { name: 'Resolve appeal' }));
    await waitFor(() => expect(apiMocks.resolveCandidateAppeal).toHaveBeenCalledWith('appeal-2', 'Second appeal panel resolution.'));
    expect(apiMocks.resolveCandidateAppeal).not.toHaveBeenCalledWith('appeal-1', expect.anything());
  });

  it('does not erase another appeal resolution when an earlier resolution completes', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.candidates.mockResolvedValue([candidate, secondCandidate]);
    apiMocks.recruitmentAppeals.mockResolvedValue(appeals);
    apiMocks.resolveCandidateAppeal.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(<RecruitmentPage />);
    await screen.findByText('Candidate A');
    await choose('Appeal to resolve', 'candidate-1 · Review requested');
    fireEvent.change(field('Appeal resolution'), { target: { value: 'First appeal panel resolution.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Resolve appeal' }));
    await waitFor(() => expect(apiMocks.resolveCandidateAppeal).toHaveBeenCalledWith('appeal-1', 'First appeal panel resolution.'));

    await choose('Appeal to resolve', 'candidate-2 · Second review requested');
    fireEvent.change(field('Appeal resolution'), { target: { value: 'Second appeal panel resolution.' } });
    await act(async () => complete({}));
    expect(await screen.findByText('Appeal resolution recorded.')).toBeInTheDocument();
    expect(field('Appeal resolution')).toHaveValue('Second appeal panel resolution.');

    await choose('Appeal to resolve', 'candidate-1 · Review requested');
    expect(field('Appeal resolution')).toHaveValue('');
  });

  it('renders fairness-audit errors and prevents duplicate retries', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.runFairnessAudit.mockRejectedValueOnce(new Error('Fairness audit conflict')).mockReturnValueOnce(new Promise(resolve => { complete = resolve; }));
    renderPage(<RecruitmentPage />);
    const audit = await prepareAudit();

    fireEvent.click(audit);
    expect(await screen.findByText('Fairness audit conflict')).toBeInTheDocument();
    fireEvent.click(audit);
    await waitFor(() => expect(apiMocks.runFairnessAudit).toHaveBeenCalledTimes(2));
    expect(audit).toBeDisabled();
    fireEvent.click(audit);
    expect(apiMocks.runFairnessAudit).toHaveBeenCalledTimes(2);
    await act(async () => complete({}));
    expect(await screen.findByText('Fairness audit completed.')).toBeInTheDocument();
  });
});
