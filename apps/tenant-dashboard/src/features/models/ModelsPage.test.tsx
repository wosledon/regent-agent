import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { field, renderPage } from '../../test/renderPage';
import i18n from '../../lib/i18n';
import { ModelsPage } from './ModelsPage';

const apiMocks = vi.hoisted(() => ({
  modelPool: vi.fn(), setManualPrice: vi.fn(), assignModelTier: vi.fn(), bindModel: vi.fn(),
  testModel: vi.fn(), addModel: vi.fn(), rotateModel: vi.fn(), deleteModel: vi.fn(), localization: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

const catalog = [
  { provider: 'OpenAI', modelId: 'model-a', version: '3', protocol: 'OpenAI', priceState: 'Manual', inputPricePerMillion: 1.5, outputPricePerMillion: 2.5, currency: 'USD', tier: 'cheap' },
  { provider: 'OpenAI', modelId: 'model-b', version: '4', protocol: 'OpenAI', priceState: 'Unpriced', inputPricePerMillion: null, outputPricePerMillion: null, currency: 'EUR', tier: 'Unassigned' }
];

beforeEach(async () => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.modelPool.mockResolvedValue({ catalog, bindings: [], unavailableEvents: [] });
  apiMocks.setManualPrice.mockResolvedValue({});
  apiMocks.assignModelTier.mockResolvedValue({});
  apiMocks.rotateModel.mockResolvedValue({});
  apiMocks.deleteModel.mockResolvedValue(undefined);
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
  await i18n.changeLanguage('en-US');
});

async function renderWithContractFeedback(outcome: 'success' | 'error'): Promise<string> {
  const serverMessage = outcome === 'success' ? 'model-a contract passed' : 'model-a contract failed';
  const feedback = outcome === 'success' ? 'Model contract is supported.' : serverMessage;
  if (outcome === 'success') {
    apiMocks.testModel.mockResolvedValue({ provider: 'OpenAI', modelId: 'model-a', protocol: 'OpenAI', supported: true, message: serverMessage });
  } else {
    apiMocks.testModel.mockRejectedValue(new Error(serverMessage));
  }
  renderPage(<ModelsPage />);
  await screen.findByDisplayValue('1.5');
  fireEvent.click(screen.getAllByRole('button', { name: 'Select model' })[0]);
  fireEvent.click(screen.getByRole('button', { name: 'Test local contract' }));
  expect(await screen.findByText(feedback)).toBeInTheDocument();
  if (outcome === 'success') expect(screen.queryByText(serverMessage)).not.toBeInTheDocument();
  return feedback;
}

describe('ModelsPage catalog drafts', () => {
  it('hydrates price, currency, tier, protocol and version from the selected model', async () => {
    renderPage(<ModelsPage />);
    expect(await screen.findByDisplayValue('1.5')).toBeInTheDocument();
    expect(screen.getByDisplayValue('2.5')).toBeInTheDocument();
    expect(screen.getByDisplayValue('USD')).toBeInTheDocument();
    expect(screen.getAllByDisplayValue('3')).toHaveLength(3);
    expect(screen.getAllByRole('combobox', { name: 'Protocol' })[0]).toHaveTextContent('OpenAI');
    expect(screen.getByRole('combobox', { name: 'Tier' })).toHaveTextContent('cheap');
  });

  it('resets only clean fields and blocks writing dirty values to another model', async () => {
    renderPage(<ModelsPage />);
    const input = await screen.findByDisplayValue('1.5');
    fireEvent.change(input, { target: { value: '9' } });
    fireEvent.mouseDown(screen.getAllByRole('combobox', { name: 'Model' })[0]);
    fireEvent.click(await screen.findByRole('option', { name: 'model-b' }));

    expect(input).toHaveValue('9');
    expect(field('Output price / million')).toHaveValue('');
    expect(field('Currency')).toHaveValue('EUR');
    expect(screen.getAllByDisplayValue('4')).toHaveLength(3);
    expect(screen.getByRole('button', { name: 'Save manual price' })).toBeDisabled();
    expect(screen.getByText(/Price edits belong to another model/)).toBeInTheDocument();

    fireEvent.mouseDown(screen.getAllByRole('combobox', { name: 'Model' })[0]);
    fireEvent.click(await screen.findByRole('option', { name: 'model-a' }));
    expect(input).toHaveValue('9');
    expect(screen.getByRole('button', { name: 'Save manual price' })).toBeEnabled();
  });

  it('keeps missing catalog prices blank and blocks accidental zero-price writes', async () => {
    renderPage(<ModelsPage />);
    await screen.findByDisplayValue('1.5');
    fireEvent.mouseDown(screen.getAllByRole('combobox', { name: 'Model' })[0]);
    fireEvent.click(await screen.findByRole('option', { name: 'model-b' }));

    expect(field('Input price / million')).toHaveValue('');
    expect(field('Output price / million')).toHaveValue('');
    const save = screen.getByRole('button', { name: 'Save manual price' });
    expect(save).toBeDisabled();
    fireEvent.click(save);
    expect(apiMocks.setManualPrice).not.toHaveBeenCalled();
  });

  it('does not silently select another model when the requested catalog entry disappears', async () => {
    const { client } = renderPage(<ModelsPage />);
    await screen.findByDisplayValue('1.5');
    fireEvent.mouseDown(screen.getAllByRole('combobox', { name: 'Model' })[0]);
    fireEvent.click(await screen.findByRole('option', { name: 'model-b' }));

    apiMocks.modelPool.mockResolvedValue({ catalog: [catalog[0]], bindings: [], unavailableEvents: [] });
    await act(async () => { await client.refetchQueries({ queryKey: ['model-pool'] }); });

    expect(field('Input price / million')).toHaveValue('');
    expect(field('Output price / million')).toHaveValue('');
    const save = screen.getByRole('button', { name: 'Save manual price' });
    expect(save).toBeDisabled();
    fireEvent.click(save);
    expect(apiMocks.setManualPrice).not.toHaveBeenCalled();
  });

  it('reconciles only saved price fields after a successful mutation', async () => {
    apiMocks.setManualPrice.mockImplementation(async () => {
      apiMocks.modelPool.mockResolvedValue({
        catalog: [{ ...catalog[0], inputPricePerMillion: 4, outputPricePerMillion: 5 }], bindings: [], unavailableEvents: []
      });
      return {};
    });
    renderPage(<ModelsPage />);
    const input = await screen.findByDisplayValue('1.5');
    fireEvent.change(input, { target: { value: '4' } });
    fireEvent.change(field('Output price / million'), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save manual price' }));

    expect(await screen.findByText('Manual price saved.')).toBeInTheDocument();
    await waitFor(() => expect(field('Input price / million')).toHaveValue('4'));
    expect(field('Output price / million')).toHaveValue('5');
    expect(screen.getByRole('button', { name: 'Reset draft to selected model' })).toBeDisabled();
  });

  it('rotates only the selected model with its validated version draft', async () => {
    renderPage(<ModelsPage />);
    await screen.findByDisplayValue('1.5');
    const rotate = screen.getByRole('button', { name: 'Rotate' });
    expect(rotate).toBeEnabled();

    fireEvent.change(field('Version'), { target: { value: '0' } });
    expect(rotate).toBeDisabled();
    fireEvent.change(field('Version'), { target: { value: '5' } });
    fireEvent.click(rotate);
    await waitFor(() => expect(apiMocks.rotateModel).toHaveBeenCalledWith('OpenAI', 'model-a', '5', 'OpenAI'));
    expect(await screen.findByText('Model rotation saved.')).toBeInTheDocument();
  });

  it('requires provider/model-identifying confirmation before deletion', async () => {
    renderPage(<ModelsPage />);
    const deleteButton = (await screen.findAllByRole('button', { name: 'Delete' }))[0];
    fireEvent.click(deleteButton);

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/OpenAI\/model-a/, { selector: 'p' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Delete' })).toBeDisabled();
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'OpenAI/model-a' }), { target: { value: 'OpenAI/model-a' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(apiMocks.deleteModel).toHaveBeenCalledWith('OpenAI', 'model-a'));
  });

  it('does not show a late contract result for a previously selected model', async () => {
    let complete!: (value: { provider: string; modelId: string; protocol: string; supported: boolean; message: string }) => void;
    apiMocks.testModel.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(<ModelsPage />);
    await screen.findByDisplayValue('1.5');
    fireEvent.click(screen.getByRole('button', { name: 'Test local contract' }));
    await waitFor(() => expect(apiMocks.testModel).toHaveBeenCalledWith('OpenAI', 'model-a'));

    fireEvent.mouseDown(screen.getAllByRole('combobox', { name: 'Model' })[0]);
    fireEvent.click(await screen.findByRole('option', { name: 'model-b' }));
    await act(async () => complete({ provider: 'OpenAI', modelId: 'model-a', protocol: 'OpenAI', supported: true, message: 'model-a contract passed' }));

    expect(screen.queryByText('Model contract is supported.')).not.toBeInTheDocument();
    expect(screen.queryByText('model-a contract passed')).not.toBeInTheDocument();

    fireEvent.mouseDown(screen.getAllByRole('combobox', { name: 'Model' })[0]);
    fireEvent.click(await screen.findByRole('option', { name: 'model-a' }));
    expect(screen.queryByText('Model contract is supported.')).not.toBeInTheDocument();
    expect(screen.queryByText('model-a contract passed')).not.toBeInTheDocument();
  });

  it('does not show a late contract error for a previously selected model', async () => {
    let fail!: (reason: unknown) => void;
    apiMocks.testModel.mockReturnValue(new Promise((_resolve, reject) => { fail = reject; }));
    renderPage(<ModelsPage />);
    await screen.findByDisplayValue('1.5');
    fireEvent.click(screen.getByRole('button', { name: 'Test local contract' }));
    await waitFor(() => expect(apiMocks.testModel).toHaveBeenCalledOnce());
    fireEvent.mouseDown(screen.getAllByRole('combobox', { name: 'Model' })[0]);
    fireEvent.click(await screen.findByRole('option', { name: 'model-b' }));
    await act(async () => { fail(new Error('model-a contract failed')); });

    expect(screen.queryByText('Model contract test failed.')).not.toBeInTheDocument();
    expect(screen.queryByText('model-a contract failed')).not.toBeInTheDocument();

    fireEvent.mouseDown(screen.getAllByRole('combobox', { name: 'Model' })[0]);
    fireEvent.click(await screen.findByRole('option', { name: 'model-a' }));
    expect(screen.queryByText('Model contract test failed.')).not.toBeInTheDocument();
    expect(screen.queryByText('model-a contract failed')).not.toBeInTheDocument();
  });

  it.each(['success', 'error'] as const)('invalidates contract %s feedback when the same model rotates', async outcome => {
    const message = await renderWithContractFeedback(outcome);
    let completeRefetch!: (value: unknown) => void;
    apiMocks.modelPool.mockReturnValueOnce(new Promise(resolve => { completeRefetch = resolve; }));
    const rotated = { ...catalog[0], version: '5' };
    apiMocks.rotateModel.mockResolvedValue(rotated);
    fireEvent.click(screen.getAllByRole('button', { name: 'Rotate' })[0]);
    await waitFor(() => expect(apiMocks.modelPool).toHaveBeenCalledTimes(2));

    expect(screen.queryByText(message)).not.toBeInTheDocument();
    await act(async () => completeRefetch({ catalog: [rotated, catalog[1]], bindings: [], unavailableEvents: [] }));
    expect(await screen.findByText('Model rotation saved.')).toBeInTheDocument();
    expect(screen.queryByText(message)).not.toBeInTheDocument();
  });

  it.each(['success', 'error'] as const)('invalidates contract %s feedback when the same model is deleted', async outcome => {
    const message = await renderWithContractFeedback(outcome);
    let completeRefetch!: (value: unknown) => void;
    apiMocks.modelPool.mockReturnValueOnce(new Promise(resolve => { completeRefetch = resolve; }));
    fireEvent.click(screen.getAllByRole('button', { name: 'Delete' })[0]);
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'OpenAI/model-a' }), { target: { value: 'OpenAI/model-a' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(apiMocks.modelPool).toHaveBeenCalledTimes(2));

    expect(screen.queryByText(message)).not.toBeInTheDocument();
    await act(async () => completeRefetch({ catalog: [catalog[1]], bindings: [], unavailableEvents: [] }));
    expect(await screen.findByText('Model deleted.')).toBeInTheDocument();
    expect(screen.queryByText(message)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Test local contract' })).not.toBeInTheDocument();
  });

  it.each([
    ['en-US', true, 'Model contract is supported.'],
    ['en-US', false, 'Model contract is not supported.'],
    ['zh-CN', true, '模型契约受支持。'],
    ['zh-CN', false, '模型契约不受支持。']
  ] as const)('renders the localized supported result in %s (%s)', async (locale, supported, expected) => {
    await i18n.changeLanguage(locale);
    apiMocks.testModel.mockResolvedValue({ provider: 'OpenAI', modelId: 'model-a', protocol: 'OpenAI', supported, message: 'backend-contract-marker' });
    renderPage(<ModelsPage />);
    await screen.findByDisplayValue('1.5');
    fireEvent.click(screen.getByRole('button', { name: locale === 'en-US' ? 'Test local contract' : '测试本地契约' }));

    expect(await screen.findByText(expected)).toBeInTheDocument();
    expect(screen.queryByText('backend-contract-marker')).not.toBeInTheDocument();
  });

  it.each([
    ['en-US', { status: 403, code: 'FORBIDDEN', message: 'backend-contract-marker' }, 'Permission denied. You do not have access to this resource.', 'Test local contract'],
    ['zh-CN', { status: 403, code: 'FORBIDDEN', message: 'backend-contract-marker' }, '权限不足，无法访问此资源。', '测试本地契约'],
    ['en-US', new TypeError('Failed to fetch'), 'Could not reach the service. Check your connection and try again.', 'Test local contract'],
    ['zh-CN', new TypeError('Failed to fetch'), '无法连接服务，请检查网络后重试。', '测试本地契约']
  ] as const)('maps contract-test API and network failures through shared localized errors in %s', async (locale, error, expected, buttonLabel) => {
    await i18n.changeLanguage(locale);
    apiMocks.testModel.mockRejectedValue(error);
    renderPage(<ModelsPage />);
    await screen.findByDisplayValue('1.5');
    fireEvent.click(screen.getByRole('button', { name: buttonLabel }));

    expect(await screen.findByText(expected)).toBeInTheDocument();
    expect(screen.queryByText('backend-contract-marker')).not.toBeInTheDocument();
    expect(screen.queryByText('Failed to fetch')).not.toBeInTheDocument();
    expect(screen.queryByText('Model contract test failed.')).not.toBeInTheDocument();
  });

  it('prevents duplicate price writes while pending', async () => {
    let complete!: (value: unknown) => void;
    apiMocks.setManualPrice.mockReturnValue(new Promise(resolve => { complete = resolve; }));
    renderPage(<ModelsPage />);
    const input = await screen.findByDisplayValue('1.5');
    fireEvent.change(input, { target: { value: '4' } });
    const save = screen.getByRole('button', { name: 'Save manual price' });
    fireEvent.click(save);
    await waitFor(() => expect(apiMocks.setManualPrice).toHaveBeenCalledTimes(1));
    expect(save).toBeDisabled();
    fireEvent.click(save);
    expect(apiMocks.setManualPrice).toHaveBeenCalledTimes(1);
    await act(async () => complete({}));
  });

  it('validates prices and exposes mutation errors', async () => {
    apiMocks.setManualPrice.mockRejectedValue(new Error('Price conflict'));
    renderPage(<ModelsPage />);
    const input = await screen.findByDisplayValue('1.5');
    fireEvent.change(input, { target: { value: '-1' } });
    expect(screen.getByRole('button', { name: 'Save manual price' })).toBeDisabled();
    fireEvent.change(input, { target: { value: '4' } });
    fireEvent.change(field('Currency'), { target: { value: 'US' } });
    expect(screen.getByRole('button', { name: 'Save manual price' })).toBeDisabled();
    fireEvent.change(field('Currency'), { target: { value: 'USD' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save manual price' }));
    expect(await screen.findByText('Price conflict')).toBeInTheDocument();
  });
});
