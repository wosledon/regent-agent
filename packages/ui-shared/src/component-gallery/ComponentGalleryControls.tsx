import { Search } from '@mui/icons-material';
import { Stack, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { TextInput } from '../components/TextInput';
import type { ComponentCategory } from './registry';

const categories: Array<{ value: ComponentCategory | 'all'; label: string }> = [
  { value: 'all', label: 'All' },
  { value: 'mui', label: 'MUI' },
  { value: 'layout', label: 'Layout' },
  { value: 'data', label: 'Data' },
  { value: 'form-feedback', label: 'Form' },
  { value: 'tenant', label: 'Tenant' },
  { value: 'operations', label: 'Operations' },
  { value: 'project-employee', label: 'Project' }
];

export interface ComponentGalleryControlsProps {
  category: ComponentCategory | 'all';
  onCategoryChange: (category: ComponentCategory | 'all') => void;
  search: string;
  onSearchChange: (search: string) => void;
}

export function ComponentGalleryControls({ category, onCategoryChange, search, onSearchChange }: ComponentGalleryControlsProps) {
  return (
    <Stack spacing={2}>
      <TextInput
        value={search}
        onChange={onSearchChange}
        placeholder="Search components"
        startAdornment={<Search />}
        inputProps={{ 'aria-label': 'Search components' }}
      />
      <ToggleButtonGroup value={category} exclusive size="small" onChange={(_, value) => value && onCategoryChange(value)}>
        {categories.map(item => <ToggleButton key={item.value} value={item.value}>{item.label}</ToggleButton>)}
      </ToggleButtonGroup>
    </Stack>
  );
}
