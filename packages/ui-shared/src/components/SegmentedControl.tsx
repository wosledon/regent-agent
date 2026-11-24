import { Box } from '@mui/material';
import { Button } from './Button';

export interface SegmentedOption<T extends string> {
  value: T;
  label: string;
}

export interface SegmentedControlProps<T extends string> {
  value: T;
  options: ReadonlyArray<SegmentedOption<T>>;
  onChange: (value: T) => void;
  ariaLabel: string;
}

export function SegmentedControl<T extends string>({ value, options, onChange, ariaLabel }: SegmentedControlProps<T>) {
  return (
    <Box role="group" aria-label={ariaLabel} sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
      {options.map(option => (
        <Button
          key={option.value}
          tone={value === option.value ? 'primary' : 'neutral'}
          aria-pressed={value === option.value}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </Button>
      ))}
    </Box>
  );
}
