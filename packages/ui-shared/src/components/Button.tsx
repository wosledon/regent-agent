import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Button as MuiButton, CircularProgress, type SxProps, type Theme } from '@mui/material';

export type ButtonTone = 'primary' | 'secondary' | 'danger' | 'neutral';

export interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'color'> {
  tone?: ButtonTone;
  loading?: boolean;
  startIcon?: ReactNode;
  size?: 'small' | 'medium' | 'large';
  sx?: SxProps<Theme>;
}

export function Button({ tone = 'primary', loading, startIcon, size = 'medium', children, disabled, sx, ...props }: ButtonProps) {
  const color = tone === 'danger' ? 'error' : tone === 'secondary' ? 'primary' : tone === 'neutral' ? 'inherit' : 'primary';
  const variant = tone === 'neutral' || tone === 'secondary' ? 'outlined' : 'contained';
  return (
    <MuiButton
      color={color}
      variant={variant}
      size={size}
      startIcon={loading ? <CircularProgress size={16} color="inherit" aria-hidden="true" /> : startIcon}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      sx={[{ minWidth: 96 }, ...(Array.isArray(sx) ? sx : sx ? [sx] : [])]}
      {...props}
    >
      <span style={{ opacity: loading ? 0.72 : 1 }}>{children}</span>
    </MuiButton>
  );
}
