import { describe, expect, it } from 'vitest';
import i18n from './i18n';
import {
  localizedCurrencyError, localizedGenericReasonDefaults, localizedLocaleError, localizedNumberRangeError,
  localizedRequiredTextError, localizedSpecificTextError, localizedTimeZoneError
} from './localizedValidation';

describe('localized validation adaptation', () => {
  it('reuses existing validation rules and localizes their messages', async () => {
    await i18n.changeLanguage('en-US');
    const t = i18n.t.bind(i18n);
    expect(localizedRequiredTextError(' ', 'Project ID', t)).toBe('Project ID is required.');
    expect(localizedNumberRangeError('2', 'Score', 0, 1, t)).toBe('Score must be between 0 and 1.');
    expect(localizedSpecificTextError('ok', 'Decision reason', ['ok'], t)).toContain('must be specific');
    expect(localizedCurrencyError('US', t)).toContain('three-letter ISO');
    expect(localizedCurrencyError('ZZZ', t)).toContain('ISO currency code');
    expect(localizedCurrencyError('BTC', t)).toContain('ISO currency code');
    expect(localizedCurrencyError('USD', t)).toBeNull();
    expect(localizedTimeZoneError('Invalid/Zone', t)).toContain('valid IANA');
    expect(localizedLocaleError('en-US', t)).toBeNull();
    expect(localizedLocaleError('fr-FR', t)).toBe('Select a supported display language.');

    await i18n.changeLanguage('zh-CN');
    const chineseDefaults = localizedGenericReasonDefaults(i18n.t.bind(i18n));
    expect(localizedSpecificTextError('已审核。', '决策原因', chineseDefaults, i18n.t.bind(i18n))).toContain('必须具体说明');
    expect(localizedSpecificTextError('已核对发布证据和回滚记录。', '决策原因', chineseDefaults, i18n.t.bind(i18n))).toBeNull();
    expect(localizedRequiredTextError(' ', '项目 ID', i18n.t.bind(i18n))).toBe('项目 ID为必填项。');
    expect(localizedNumberRangeError('2', '评分', 0, 1, i18n.t.bind(i18n))).toContain('必须介于 0 和 1 之间');
    expect(localizedCurrencyError('US', i18n.t.bind(i18n))).toContain('ISO 4217');
    expect(localizedLocaleError('fr-FR', i18n.t.bind(i18n))).toBe('请选择受支持的显示语言。');
  });

  it.each(['en-US', 'zh-CN'] as const)('rejects generic reasons from both supported locales while %s is active', async locale => {
    await i18n.changeLanguage(locale);
    const t = i18n.t.bind(i18n);
    const defaults = localizedGenericReasonDefaults(t);
    expect(defaults).toEqual(expect.arrayContaining(['Reviewed', '已审核', 'Retest completed', '复测已完成']));
    expect(localizedSpecificTextError(' 已审核。 ', 'Decision reason', defaults, t)).not.toBeNull();
    expect(localizedSpecificTextError(' Reviewed! ', 'Decision reason', defaults, t)).not.toBeNull();
    expect(localizedSpecificTextError('复测已完成！', 'Appeal resolution', defaults, t)).not.toBeNull();
    expect(localizedSpecificTextError('Retest completed.', 'Appeal resolution', defaults, t)).not.toBeNull();
    expect(localizedSpecificTextError('Checked release evidence and rollback output.', 'Decision reason', defaults, t)).toBeNull();
  });
});
