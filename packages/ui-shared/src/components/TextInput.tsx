import type { InputHTMLAttributes } from 'react';
import { Input as MuiInput, InputAdornment, type InputProps } from '@mui/material';

export interface TextInputProps extends Omit<InputProps, 'value' | 'onChange'> {
  value?: string;
  onChange?: (value: string) => void;
  startAdornment?: React.ReactNode;
  inputProps?: InputHTMLAttributes<HTMLInputElement>;
}

export function TextInput({ value, onChange, startAdornment, inputProps, ...props }: TextInputProps) {
  return (
    <MuiInput
      value={value ?? ''}
      onChange={event => onChange?.(event.target.value)}
      startAdornment={startAdornment ? <InputAdornment position="start">{startAdornment}</InputAdornment> : null}
      inputProps={{ 'aria-label': props.placeholder, ...inputProps }}
      {...props}
    />
  );
}
