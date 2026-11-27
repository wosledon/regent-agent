import { act, fireEvent, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import i18n from '../../lib/i18n';
import { LocalizedGraphCanvas, type LocalizedGraphCanvasNode } from './LocalizedGraphCanvas';

vi.mock('../../lib/api-client/api', () => ({
  api: { localization: vi.fn().mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 }) }
}));

const node: LocalizedGraphCanvasNode = {
  id: 'project:1', label: 'Dashboard', subtitle: 'Active · 1234 tasks · Healthy',
  sourceSubtitle: 'Active · 1234 tasks · Healthy', status: 'Healthy', kind: 'Project', x: 0, y: 0
};

beforeEach(async () => {
  await i18n.changeLanguage('en-US');
});

describe('LocalizedGraphCanvas', () => {
  it('localizes graph aria, subtitle and status while preserving the source node', async () => {
    const onSelect = vi.fn();
    renderPage(<LocalizedGraphCanvas nodes={[node]} edges={[]} onSelect={onSelect} />);
    const englishNode = await screen.findByRole('button', { name: 'Dashboard, Active · 1,234 tasks · Healthy' });
    expect(screen.getByText('Project · Healthy')).toBeInTheDocument();

    await act(async () => { await i18n.changeLanguage('zh-CN'); });
    const chineseNode = screen.getByRole('button', { name: 'Dashboard，活跃 · 1,234 个任务 · 健康' });
    expect(screen.getByText('项目 · 健康')).toBeInTheDocument();
    fireEvent.click(chineseNode);
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ sourceSubtitle: node.sourceSubtitle, status: 'Healthy' }));
    expect(englishNode).toBeInTheDocument();
  });
});
