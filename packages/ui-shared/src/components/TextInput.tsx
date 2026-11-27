import type { InputHTMLAttributes, ReactNode } from 'react';
import { InputAdornment, OutlinedInput, type OutlinedInputProps } from '@mui/material';
import { useFormFieldContext } from './formFieldContext';

export interface TextInputProps extends Omit<OutlinedInputProps, 'value' | 'onChange'> {
  value?: string;
  onChange?: (value: string) => void;
  startAdornment?: ReactNode;
  endAdornment?: ReactNode;
  inputProps?: InputHTMLAttributes<HTMLInputElement>;
}

export function TextInput({ value, onChange, startAdornment, endAdornment, inputProps, id, required, disabled, ...props }: TextInputProps) {
  const field = useFormFieldContext();
  const controlId = id ?? field?.controlId;
  const describedBy = field?.descriptionId ?? inputProps?.['aria-describedby'];
  const invalid = field?.invalid ?? false;
  const isRequired = required ?? field?.required ?? false;
  const isDisabled = disabled ?? field?.disabled ?? false;
  const explicitAriaLabel = inputProps?.['aria-label'];
  return (
    <OutlinedInput
      {...props}
      id={controlId}
      value={value ?? ''}
      onChange={event => onChange?.(event.target.value)}
      required={isRequired}
      disabled={isDisabled}
      error={invalid}
      startAdornment={startAdornment ? <InputAdornment position="start">{startAdornment}</InputAdornment> : undefined}
      endAdornment={endAdornment ? <InputAdornment position="end">{endAdornment}</InputAdornment> : undefined}
      inputProps={{
        'aria-label': explicitAriaLabel ?? (field ? undefined : props.placeholder),
        'aria-labelledby': explicitAriaLabel ? undefined : field?.labelId,
        'aria-describedby': describedBy,
        'aria-invalid': invalid || undefined,
        'aria-required': isRequired || undefined,
        ...inputProps
      }}
    />
  );
}
