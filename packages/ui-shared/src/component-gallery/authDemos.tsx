import { useState } from 'react';
import { AuthLayout } from '../components/AuthLayout';
import { Button } from '../components/Button';
import { FormField, Stack } from '../components/Layout';
import { LocaleSwitch } from '../components/LocaleSwitch';
import { Text } from '../components/Text';
import { TextInput } from '../components/TextInput';
import { applyLocalePreference, initialLocale, type AppLocale } from '../locale';

function useLocaleDemo() {
  const [locale, setLocale] = useState<AppLocale>(() => initialLocale());
  const changeLocale = (next: AppLocale) => {
    setLocale(next);
    applyLocalePreference(next);
  };
  return { locale, changeLocale };
}

export function LocaleSwitchDemo() {
  const { locale, changeLocale } = useLocaleDemo();
  return <LocaleSwitch value={locale} label="Display language" onChange={changeLocale} />;
}

export function AuthLayoutDemo() {
  const { locale, changeLocale } = useLocaleDemo();
  return <AuthLayout
    audience="TENANT WORKSPACE"
    headline="A focused home for autonomous work"
    description="Bring projects, approvals and company knowledge into one secure workspace."
    highlights={[
      { icon: 'organization', title: 'Aligned teams', description: 'Clear ownership and accountable delivery.' },
      { icon: 'success', title: 'Governed execution', description: 'Approvals and policy stay visible at every step.' }
    ]}
    footer="Regent Agent · Component gallery demo"
    locale={locale}
    localeLabel="Display language"
    onLocaleChange={changeLocale}
  >
    <Stack spacing={2}>
      <Text variant="h2">Welcome back</Text>
      <Text color="text.secondary">Sign in to continue to your workspace.</Text>
      <FormField label="Email"><TextInput placeholder="owner@example.test" /></FormField>
      <Button>Sign in</Button>
    </Stack>
  </AuthLayout>;
}
