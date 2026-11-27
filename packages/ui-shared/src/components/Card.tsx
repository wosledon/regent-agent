import type { PropsWithChildren, ReactNode } from 'react';
import { Card as MuiCard, CardContent, Divider, Stack, type SxProps, type Theme } from '@mui/material';
import { Text } from './Text';
import { designTokens } from '../theme/theme';

export type CardTone = 'default' | 'info' | 'success' | 'warning' | 'error';

export interface CardProps extends PropsWithChildren {
  title?: string;
  description?: string;
  action?: ReactNode;
  footer?: ReactNode;
  tone?: CardTone;
  padding?: 'none' | 'small' | 'normal';
  headingLevel?: 'h2' | 'h3' | 'div';
  sx?: SxProps<Theme>;
}

const railColor: Record<CardTone, string> = {
  default: 'transparent',
  info: designTokens.color.info,
  success: designTokens.color.success,
  warning: designTokens.color.warning,
  error: designTokens.color.error
};

export function Card({ title, description, action, footer, tone = 'default', padding = 'normal', headingLevel = 'h3', sx, children }: CardProps) {
  return (
    <MuiCard sx={[{ borderLeft: tone === 'default' ? undefined : `3px solid ${railColor[tone]}` }, ...(Array.isArray(sx) ? sx : sx ? [sx] : [])]}>
      {title || description || action ? (
        <Stack direction="row" spacing={2} sx={{ px: padding === 'none' ? 0 : { xs: 2, sm: 2.5 }, pt: padding === 'none' ? 0 : { xs: 2, sm: 2.5 }, pb: title || description ? 1.5 : padding === 'none' ? 0 : 2.5, alignItems: 'flex-start', justifyContent: 'space-between' }}>
          <Stack spacing={0.25} sx={{ minWidth: 0 }}>
            {title ? <Text variant="cardTitle" component={headingLevel}>{title}</Text> : null}
            {description ? <Text variant="secondary">{description}</Text> : null}
          </Stack>
          {action ? <Stack direction="row" spacing={0.5} sx={{ flexShrink: 0 }}>{action}</Stack> : null}
        </Stack>
      ) : null}
      <CardContent sx={{ p: padding === 'none' ? 0 : padding === 'small' ? { xs: 1.5, sm: 2 } : { xs: 2, sm: 2.5 }, '&:last-child': { pb: padding === 'none' ? 0 : footer ? 1.5 : { xs: 2, sm: 2.5 } } }}>
        <Stack spacing={2}>{children}</Stack>
      </CardContent>
      {footer ? <><Divider /><Stack sx={{ px: padding === 'none' ? 0 : { xs: 2, sm: 2.5 }, py: 1.5 }}>{footer}</Stack></> : null}
    </MuiCard>
  );
}
