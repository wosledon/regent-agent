import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Button as MuiButton, CircularProgress } from '@mui/material';

export type ButtonTone = 'primary' | 'secondary' | 'danger' | 'neutral';

export interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'color'> {
  tone?: ButtonTone;
  loading?: boolean;
  startIcon?: ReactNode;
}

export function Button({ tone = 'primary', loading, startIcon, children, disabled, ...props }: ButtonProps) {
  const color = tone === 'danger' ? 'error' : tone === 'secondary' ? 'secondary' : tone === 'neutral' ? 'inherit' : 'primary';
  const variant = tone === 'neutral' ? 'outlined' : 'contained';
  return (
    <MuiButton color={color} variant={variant} startIcon={startIcon} disabled={disabled || loading} {...props}>
      {loading ? <CircularProgress size={18} color="inherit" /> : children}
    </MuiButton>
  );
}
