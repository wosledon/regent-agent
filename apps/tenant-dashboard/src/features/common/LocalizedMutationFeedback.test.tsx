import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderPage } from '../../test/renderPage';
import i18n from '../../lib/i18n';
import { LocalizedMutationFeedback } from './LocalizedMutationFeedback';

describe('LocalizedMutationFeedback', () => {
  it.each([
    ['en-US', 'Changes saved.', 'Permission denied. You do not have access to this resource.'],
    ['zh-CN', '更改已保存。', '权限不足，无法访问此资源。']
  ] as const)('renders localized success and safe API errors in %s', async (locale, success, failure) => {
    await i18n.changeLanguage(locale);
    const { rerender } = renderPage(<LocalizedMutationFeedback mutation={{ isSuccess: true, error: null }} successMessage={success} />);
    expect(screen.getByText(success)).toBeInTheDocument();

    rerender(
      <LocalizedMutationFeedback
        mutation={{ isSuccess: false, error: { status: 403, code: 'FORBIDDEN', message: 'SENSITIVE_BACKEND_MARKER' } }}
        successMessage={success}
      />
    );
    expect(screen.getByText(failure)).toBeInTheDocument();
    expect(screen.queryByText('SENSITIVE_BACKEND_MARKER')).not.toBeInTheDocument();
  });

  it('can omit success copy when the mutation result is rendered directly', () => {
    renderPage(<LocalizedMutationFeedback mutation={{ isSuccess: true, error: null }} showSuccess={false} />);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
