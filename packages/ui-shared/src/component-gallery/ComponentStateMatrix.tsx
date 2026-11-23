import { useState } from 'react';
import { Select } from '../components/Select';
import { Text } from '../components/Text';
import { ComponentDemo } from './ComponentDemo';
import type { ComponentRegistryEntry } from './registry';

export interface ComponentStateMatrixProps {
  entry: ComponentRegistryEntry;
}

export function ComponentStateMatrix({ entry }: ComponentStateMatrixProps) {
  const [state, setState] = useState('default');
  return (
    <div>
      <Select label="State" value={state} onChange={setState} options={[
        { value: 'default', label: 'Default' },
        { value: 'loading', label: 'Loading' },
        { value: 'error', label: 'Error' },
        { value: 'disabled', label: 'Disabled' }
      ]} />
      <Text variant="caption">State: {state}</Text>
      <ComponentDemo entry={entry} />
    </div>
  );
}
