import { Children, useId, type PropsWithChildren, type ReactNode } from 'react';
import { Stack as MuiStack, Grid as MuiGrid, type SxProps, type Theme } from '@mui/material';
import { Text } from './Text';
import { FormFieldContext } from './formFieldContext';
import { designTokens } from '../theme/theme';

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

export interface ContentContainerProps extends PropsWithChildren {
  maxWidth?: number | string;
  sx?: SxProps<Theme>;
}

export function ContentContainer({ maxWidth = designTokens.layout.contentMaxWidth, sx, children }: ContentContainerProps) {
  return (
    <MuiStack
      component="div"
      spacing={3}
      sx={[{ width: '100%', maxWidth, mx: 'auto', minWidth: 0, px: { xs: 2, sm: 3, lg: 4 }, py: { xs: 3, sm: 4 } }, ...(Array.isArray(sx) ? sx : sx ? [sx] : [])]}
    >
      {children}
    </MuiStack>
  );
}

export interface PageHeaderProps {
  title: string;
  description?: string;
  actions?: ReactNode;
  eyebrow?: string;
  meta?: ReactNode;
}

export function PageHeader({ title, description, actions, eyebrow, meta }: PageHeaderProps) {
  return (
    <MuiStack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: { sm: 'flex-start' }, justifyContent: 'space-between' }}>
      <MuiStack spacing={0.75} sx={{ minWidth: 0, flex: '1 1 420px' }}>
        {eyebrow ? <Text variant="overline" color="text.secondary">{eyebrow}</Text> : null}
        <Text variant="h1" component="h1">{title}</Text>
        {description ? <Text variant="secondary" sx={{ maxWidth: '72ch' }}>{description}</Text> : null}
        {meta ? <MuiStack direction="row" spacing={1.5} sx={{ flexWrap: 'wrap', pt: 0.5 }}>{meta}</MuiStack> : null}
      </MuiStack>
      {actions ? <MuiStack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ width: { xs: '100%', sm: 'auto' }, flexShrink: 0 }}>{actions}</MuiStack> : null}
    </MuiStack>
  );
}

export interface SectionHeaderProps {
  title: string;
  description?: string;
  action?: ReactNode;
}

export function SectionHeader({ title, description, action }: SectionHeaderProps) {
  return (
    <MuiStack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ justifyContent: 'space-between', alignItems: { sm: 'center' } }}>
      <MuiStack spacing={0.25} sx={{ minWidth: 0 }}>
        <Text variant="h2" component="h2">{title}</Text>
        {description ? <Text variant="secondary">{description}</Text> : null}
      </MuiStack>
      {action}
    </MuiStack>
  );
}

export interface FormFieldProps extends PropsWithChildren {
  label: string;
  hint?: string;
  error?: string;
  required?: boolean;
  disabled?: boolean;
}

export function FormField({ label, hint, error, required = false, disabled = false, children }: FormFieldProps) {
  const generatedId = useId();
  const child = Children.toArray(children)[0];
  const childProps = child && typeof child === 'object' && 'props' in child ? child.props as Record<string, unknown> : {};
  const controlId = typeof childProps.id === 'string' && childProps.id ? childProps.id : generatedId;
  const labelId = `${controlId}-label`;
  const descriptionId = hint || error ? `${controlId}-description` : undefined;
  return (
    <FormFieldContext.Provider value={{ controlId, labelId, descriptionId, label, invalid: Boolean(error), required, disabled }}>
      <MuiStack spacing={0.75}>
        <Text component="label" id={labelId} htmlFor={controlId} variant="label" sx={{ color: error ? 'error.main' : 'text.primary' }}>
          {label}{required ? <Text component="span" color="error" aria-hidden="true"> *</Text> : null}
        </Text>
        {child}
        {error ? <Text id={descriptionId} variant="caption" color="error" role="alert">{error}</Text> : hint ? <Text id={descriptionId} variant="caption" color="text.secondary">{hint}</Text> : null}
      </MuiStack>
    </FormFieldContext.Provider>
  );
}
