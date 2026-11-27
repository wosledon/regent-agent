import { act, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../test/renderPage';
import i18n from '../lib/i18n';
import { useTenantFormatting } from './useTenantFormatting';

vi.mock('../lib/api-client/api', () => ({
  api: { localization: vi.fn().mockResolvedValue({ locale: 'en-US', timeZone: 'America/New_York', currency: 'USD', version: 1 }) }
}));

function FormattingProbe() {
  const format = useTenantFormatting();
  return <>
    <span data-testid="date">{format.dateTime('2026-11-25T12:00:00Z')}</span>
    <span data-testid="number">{format.number(12345, { notation: 'compact', maximumFractionDigits: 1 })}</span>
    <span data-testid="percent">{format.percent(0.125)}</span>
    <span data-testid="money">{format.moneyMajor(1234.5, 'USD')}</span>
    <span data-testid="invalid-date">{format.dateTime('not-a-date')}</span>
  </>;
}

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('useTenantFormatting', () => {
  it('updates locale formatting, money, invalid fallbacks and document language immediately', async () => {
    renderPage(<FormattingProbe />);
    await waitFor(() => expect(screen.getByTestId('date')).toHaveTextContent('11/25/2026, 07:00:00'));
    expect(screen.getByTestId('number')).toHaveTextContent('12.3K');
    expect(screen.getByTestId('percent')).toHaveTextContent('12.5%');
    expect(screen.getByTestId('money').textContent).toBe('USD\u00a01,234.50');
    expect(screen.getByTestId('invalid-date')).toHaveTextContent('Not available');
    expect(document.documentElement.lang).toBe('en-US');

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    await waitFor(() => expect(screen.getByTestId('date')).toHaveTextContent('2026/11/25 07:00:00'));
    expect(screen.getByTestId('number')).toHaveTextContent('1.2万');
    expect(screen.getByTestId('percent')).toHaveTextContent('12.5%');
    expect(screen.getByTestId('money').textContent).toBe('USD\u00a01,234.50');
    expect(screen.getByTestId('invalid-date')).toHaveTextContent('暂无数据');
    expect(document.documentElement.lang).toBe('zh-CN');
  });
});
