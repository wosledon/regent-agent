import { Typography, type TypographyProps } from '@mui/material';
import { designTokens } from '../theme/theme';

export type TextVariant =
  | 'display'
  | 'h1'
  | 'h2'
  | 'h3'
  | 'section'
  | 'cardTitle'
  | 'body'
  | 'bodyStrong'
  | 'secondary'
  | 'caption'
  | 'label'
  | 'overline'
  | 'metric'
  | 'mono';

export interface TextProps extends Omit<TypographyProps, 'variant'> {
  variant?: TextVariant;
  htmlFor?: string;
}

const variantMap: Record<TextVariant, TypographyProps['variant']> = {
  display: 'h1',
  h1: 'h1',
  h2: 'h2',
  h3: 'h3',
  section: 'h2',
  cardTitle: 'h3',
  body: 'body1',
  bodyStrong: 'body1',
  secondary: 'body2',
  caption: 'caption',
  label: 'subtitle2',
  overline: 'overline',
  metric: 'h2',
  mono: 'caption'
};

export function Text({ variant = 'body', children, sx, ...props }: TextProps) {
  const mapped = variantMap[variant];
  const variantStyles = variant === 'display'
    ? { fontSize: { xs: '2rem', md: '2.75rem' }, lineHeight: 1.1, letterSpacing: '-0.03em' }
    : variant === 'metric'
      ? { fontSize: '1.75rem', lineHeight: 1.2, fontWeight: 600, letterSpacing: '-0.02em', fontVariantNumeric: 'tabular-nums' }
      : variant === 'bodyStrong'
        ? { fontWeight: 600 }
        : variant === 'secondary'
          ? { color: 'text.secondary' }
          : variant === 'mono'
            ? { fontFamily: designTokens.font.mono, fontSize: '.75rem', fontVariantNumeric: 'tabular-nums' }
            : {};
  return <Typography variant={mapped} sx={[variantStyles, ...(Array.isArray(sx) ? sx : sx ? [sx] : [])]} {...props}>{children}</Typography>;
}
