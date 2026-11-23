import { Card, Select, Text } from '@regent/ui-shared';
import { useState } from 'react';

export function SettingsPage() {
  const [locale, setLocale] = useState('zh-CN');
  return (
    <div>
      <Text variant="h1">Settings</Text>
      <Card title="Preferences">
        <Select label="Language" value={locale} options={[{ value: 'zh-CN', label: '简体中文' }, { value: 'en-US', label: 'English' }]} onChange={setLocale} />
        <Text>Timezone: Asia/Shanghai</Text>
        <Text>Notifications: dashboard only</Text>
      </Card>
    </div>
  );
}
