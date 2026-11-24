import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Button as MuiButton, CircularProgress, type SxProps, type Theme } from '@mui/material';

export type ButtonTone = 'primary' | 'secondary' | 'danger' | 'neutral';

export interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'color'> {
  tone?: ButtonTone;
  loading?: boolean;
  startIcon?: ReactNode;
  sx?: SxProps<Theme>;
}

export function Button({ tone = 'primary', loading, startIcon, children, disabled, sx, ...props }: ButtonProps) {
  const color = tone === 'danger' ? 'error' : tone === 'secondary' ? 'secondary' : tone === 'neutral' ? 'inherit' : 'primary';
  const variant = tone === 'neutral' ? 'outlined' : 'contained';
  return (
    <MuiButton color={color} variant={variant} startIcon={startIcon} disabled={disabled || loading} sx={sx} {...props}>
      {loading ? <CircularProgress size={18} color="inherit" /> : children}
    </MuiButton>
  );
}
