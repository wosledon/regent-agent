import type { AppLocale } from '../locale';
import { SegmentedControl } from './SegmentedControl';

export interface LocaleSwitchProps {
  value: AppLocale;
  onChange: (locale: AppLocale) => void;
  label: string;
}

export function LocaleSwitch({ value, onChange, label }: LocaleSwitchProps) {
  return (
    <SegmentedControl<AppLocale>
      value={value}
      ariaLabel={label}
      options={[
        { value: 'zh-CN', label: '简体中文' },
        { value: 'en-US', label: 'English' }
      ]}
      onChange={onChange}
    />
  );
}
