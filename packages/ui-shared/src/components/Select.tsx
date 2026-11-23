import { FormControl, InputLabel, MenuItem, Select as MuiSelect, type SelectChangeEvent } from '@mui/material';

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
}

export function Select<T extends string = string>({ label, value, options, onChange, disabled, error }: SelectProps<T>) {
  return (
    <FormControl fullWidth size="small" disabled={disabled} error={error}>
      <InputLabel id={`${label}-label`}>{label}</InputLabel>
      <MuiSelect
        labelId={`${label}-label`}
        label={label}
        SelectDisplayProps={{ 'aria-label': label }}
        value={value}
        onChange={(event: SelectChangeEvent<T>) => onChange(event.target.value as T)}
      >
        {options.map(option => <MenuItem key={option.value} value={option.value}>{option.label}</MenuItem>)}
      </MuiSelect>
    </FormControl>
  );
}
