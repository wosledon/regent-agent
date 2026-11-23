import type { TypographyProps } from '@mui/material';
import { Typography } from '@mui/material';

export type TextVariant = 'h1' | 'h2' | 'h3' | 'body' | 'caption' | 'label';

export interface TextProps extends Omit<TypographyProps, 'variant'> {
  variant?: TextVariant;
}

export function Text({ variant = 'body', children, ...props }: TextProps) {
  const mapped = variant === 'label' ? 'subtitle2' : variant === 'body' ? 'body1' : variant;
  return <Typography variant={mapped} {...props}>{children}</Typography>;
}
