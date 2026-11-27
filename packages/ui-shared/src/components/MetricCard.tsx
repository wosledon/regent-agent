import type { ReactNode } from 'react';
import { Stack } from '@mui/material';
import { Card, type CardTone } from './Card';
import { Icon, type IconName } from './Icon';
import { Text } from './Text';

export interface MetricCardProps {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  icon?: IconName;
  tone?: CardTone;
  action?: ReactNode;
}

export function MetricCard({ label, value, hint, icon, tone = 'default', action }: MetricCardProps) {
  return (
    <Card
      tone={tone}
      action={action ?? (icon ? <Icon name={icon} sx={{ color: tone === 'default' ? 'text.secondary' : `${tone}.main` }} /> : undefined)}
      sx={{ height: '100%' }}
    >
      <Stack spacing={1}>
        <Text variant="caption" color="text.secondary">{label}</Text>
        <Text variant="metric">{value}</Text>
        {hint ? <Text variant="secondary">{hint}</Text> : null}
      </Stack>
    </Card>
  );
}
