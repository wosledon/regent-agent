import type { PropsWithChildren, ReactNode } from 'react';
import { Card as MuiCard, CardContent, CardHeader, Stack } from '@mui/material';
import { Text } from './Text';

export interface CardProps extends PropsWithChildren {
  title?: string;
  action?: ReactNode;
  padding?: 'none' | 'small' | 'normal';
}

export function Card({ title, action, padding = 'normal', children }: CardProps) {
  return (
    <MuiCard>
      {title ? <CardHeader title={<Text variant="h3">{title}</Text>} action={action} /> : null}
      <CardContent sx={{ p: padding === 'none' ? 0 : padding === 'small' ? 1.5 : 2.5 }}>
        <Stack spacing={2}>{children}</Stack>
      </CardContent>
    </MuiCard>
  );
}
