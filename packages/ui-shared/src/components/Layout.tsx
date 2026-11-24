import { Children, type PropsWithChildren, type ReactNode } from 'react';
import { Stack as MuiStack, Grid as MuiGrid, type SxProps, type Theme } from '@mui/material';
import { Text } from './Text';

export interface StackProps extends PropsWithChildren {
  direction?: 'row' | 'column';
  spacing?: number;
  wrap?: boolean;
  alignItems?: 'flex-start' | 'center' | 'flex-end' | 'stretch';
  sx?: SxProps<Theme>;
}

export function Stack({ direction = 'column', spacing = 2, wrap = false, alignItems, sx, children }: StackProps) {
  const styles = Array.isArray(sx) ? sx : sx ? [sx] : [];
  return <MuiStack direction={direction} spacing={spacing} useFlexGap={wrap} alignItems={alignItems} sx={[{ minWidth: 0 }, wrap ? { flexWrap: 'wrap' } : {}, ...styles]}>{children}</MuiStack>;
}

export interface GridProps extends PropsWithChildren {
  columns?: number;
  spacing?: number;
}

export function Grid({ columns = 2, spacing = 2, children }: GridProps) {
  return (
    <MuiGrid container spacing={spacing}>
      {Children.map(children, child => <MuiGrid size={{ xs: 12, md: 12 / columns }}>{child}</MuiGrid>)}
    </MuiGrid>
  );
}

export interface PageHeaderProps {
  title: string;
  description?: string;
  actions?: ReactNode;
}

export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <Stack direction="row" wrap spacing={2}>
      <Stack spacing={0.5} sx={{ minWidth: 0, flex: '1 1 320px' }}>
        <Text variant="h1" sx={{ fontSize: { xs: '2.2rem', sm: '3rem', md: '4rem' }, overflowWrap: 'normal' }}>{title}</Text>
        {description ? <Text color="secondary">{description}</Text> : null}
      </Stack>
      {actions}
    </Stack>
  );
}

export interface FormFieldProps extends PropsWithChildren {
  label: string;
  hint?: string;
}

export function FormField({ label, hint, children }: FormFieldProps) {
  return (
    <Stack spacing={0.5}>
      <Text variant="label">{label}</Text>
      {children}
      {hint ? <Text variant="caption">{hint}</Text> : null}
    </Stack>
  );
}
