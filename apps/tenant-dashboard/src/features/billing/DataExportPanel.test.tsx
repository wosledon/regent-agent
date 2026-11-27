import { fireEvent, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderPage } from '../../test/renderPage';
import i18n from '../../lib/i18n';
import { DataExportPanel } from './DataExportPanel';

const apiMocks = vi.hoisted(() => ({
  exports: vi.fn(), createExport: vi.fn(), validateExport: vi.fn(), localization: vi.fn()
}));
vi.mock('../../lib/api-client/api', () => ({ api: apiMocks }));

const exportRow = {
  id: 'export-1', tenantId: 'tenant-1', status: 'Completed', format: 'zip', schemaVersion: '1',
  objectKey: 'exports/export-1.zip', sha256: 'checksum', length: 100, tableCount: 2, moduleCount: 1,
  createdAt: '2026-11-25T10:00:00Z', validatedAt: null, validationError: null
};

beforeEach(() => {
  Object.values(apiMocks).forEach(mock => mock.mockReset());
  apiMocks.exports.mockResolvedValue([exportRow]);
  apiMocks.createExport.mockResolvedValue(exportRow);
  apiMocks.localization.mockResolvedValue({ locale: 'en-US', timeZone: 'UTC', currency: 'USD', version: 1 });
});

describe('DataExportPanel validation results', () => {
  it.each([
    ['en-US', 'Validate', 'Validated', 'Export package validated.'],
    ['zh-CN', '验证', '已验证', '导出包已验证。']
  ] as const)('shows validated success only for valid=true in %s', async (locale, validateLabel, statusLabel, successLabel) => {
    await i18n.changeLanguage(locale);
    apiMocks.validateExport.mockResolvedValue({
      exportId: exportRow.id, valid: true, sha256: exportRow.sha256, tableCount: 2, moduleCount: 1,
      validatedAt: '2026-11-25T11:00:00Z', error: null
    });
    apiMocks.exports.mockResolvedValue([{ ...exportRow, validatedAt: '2026-11-25T11:00:00Z' }]);
    renderPage(<DataExportPanel canManage />);

    fireEvent.click(await screen.findByRole('button', { name: validateLabel }));

    expect(await screen.findByText(successLabel)).toBeInTheDocument();
    expect(await screen.findByText(statusLabel)).toBeInTheDocument();
    expect(screen.queryByText(locale === 'en-US' ? 'Export package validation failed.' : '导出包验证失败。')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: locale === 'en-US' ? 'Download' : '下载' })).toHaveAttribute('href', '/v1/exports/export-1/content');
  });

  it.each([
    ['en-US', 'Validate', 'Invalid', 'Export package validation failed.'],
    ['zh-CN', '验证', '无效', '导出包验证失败。']
  ] as const)('shows invalid failure and overrides stale validation timestamps in %s', async (locale, validateLabel, statusLabel, failureLabel) => {
    await i18n.changeLanguage(locale);
    const previouslyValidated = { ...exportRow, validatedAt: '2026-11-25T09:00:00Z' };
    apiMocks.exports.mockResolvedValue([previouslyValidated]);
    apiMocks.validateExport.mockResolvedValue({
      exportId: exportRow.id, valid: false, sha256: exportRow.sha256, tableCount: 2, moduleCount: 1,
      validatedAt: '2026-11-25T11:00:00Z', error: 'backend-checksum-marker'
    });
    renderPage(<DataExportPanel canManage />);
    expect(await screen.findByText(locale === 'en-US' ? 'Validated' : '已验证')).toBeInTheDocument();

    apiMocks.exports.mockResolvedValue([{ ...previouslyValidated, validationError: 'backend-checksum-marker' }]);
    fireEvent.click(screen.getByRole('button', { name: validateLabel }));

    expect(await screen.findByText(failureLabel)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText(locale === 'en-US' ? 'Validated' : '已验证')).not.toBeInTheDocument());
    expect(screen.getByText(statusLabel)).toBeInTheDocument();
    expect(screen.queryByText(locale === 'en-US' ? 'Export package validated.' : '导出包已验证。')).not.toBeInTheDocument();
    expect(screen.queryByText('backend-checksum-marker')).not.toBeInTheDocument();
  });
});
