import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { AppThemeProvider } from '@regent/ui-shared';
import i18n from '../../lib/i18n';
import { LocalizedTimelineEntry } from './LocalizedTimelineEntry';

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('LocalizedTimelineEntry', () => {
  it.each([
    ['en-US', 'Review evidence', 'Next: Review evidence'],
    ['zh-CN', '审核证据', '下一步：审核证据']
  ] as const)('renders the next-action prefix from resources in %s', async (locale, nextAction, expected) => {
    await i18n.changeLanguage(locale);
    render(
      <AppThemeProvider>
        <LocalizedTimelineEntry timestampLabel="2026-11-25" title="Audit" status="Completed" nextAction={nextAction} />
      </AppThemeProvider>
    );
    expect(screen.getByText(expected)).toBeInTheDocument();
  });
});
