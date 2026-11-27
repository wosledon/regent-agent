import { useId } from 'react';
import { FormControl, FormHelperText, InputLabel, MenuItem, Select as MuiSelect, type SelectChangeEvent } from '@mui/material';
import { useFormFieldContext } from './formFieldContext';

export interface SelectOption<T extends string = string> {
  value: T;
  label: string;
}

export interface SelectProps<T extends string = string> {
  label: string;
  value: T;
  options: ReadonlyArray<SelectOption<T>>;
  onChange: (value: T) => void;
  disabled?: boolean;
  error?: boolean;
  required?: boolean;
  id?: string;
  helperText?: string;
}

export function Select<T extends string = string>({ label, value, options, onChange, disabled, error, required, id, helperText }: SelectProps<T>) {
  const generatedId = useId();
  const field = useFormFieldContext();
  const controlId = id ?? field?.controlId ?? generatedId;
  const labelId = field?.labelId ?? `${controlId}-label`;
  const descriptionId = field?.descriptionId ?? (helperText ? `${controlId}-description` : undefined);
  const invalid = error ?? field?.invalid ?? false;
  const isRequired = required ?? field?.required ?? false;
  const isDisabled = disabled ?? field?.disabled ?? false;
  const showLabel = !field;
  return (
    <FormControl fullWidth size="small" disabled={isDisabled} error={invalid} required={isRequired}>
      {showLabel ? <InputLabel id={labelId}>{label}</InputLabel> : null}
      <MuiSelect
        id={controlId}
        labelId={labelId}
        label={showLabel ? label : undefined}
        notched={showLabel}
        value={value}
        onChange={(event: SelectChangeEvent<T>) => onChange(event.target.value as T)}
        SelectDisplayProps={{
          'aria-labelledby': labelId,
          'aria-describedby': descriptionId,
          'aria-invalid': invalid || undefined,
          'aria-required': isRequired || undefined
        }}
      >
        {options.map(option => <MenuItem key={option.value} value={option.value}>{option.label}</MenuItem>)}
      </MuiSelect>
      {!field && helperText ? <FormHelperText id={descriptionId}>{helperText}</FormHelperText> : null}
    </FormControl>
  );
}
