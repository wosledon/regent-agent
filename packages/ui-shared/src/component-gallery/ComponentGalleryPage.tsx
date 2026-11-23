import { useMemo, useState } from 'react';
import { Container, Stack } from '@mui/material';
import { Card } from '../components/Card';
import { Text } from '../components/Text';
import { ComponentCatalog } from './ComponentCatalog';
import { ComponentDemo } from './ComponentDemo';
import { ComponentGalleryControls } from './ComponentGalleryControls';
import { componentRegistry } from './componentRegistry';
import type { ComponentCategory } from './registry';

export function ComponentGalleryPage() {
  const [category, setCategory] = useState<ComponentCategory | 'all'>('all');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState(componentRegistry[0]?.id ?? '');
  const entries = useMemo(() => componentRegistry.filter(entry =>
    (category === 'all' || entry.category === category) &&
    `${entry.name} ${entry.description} ${entry.tags.join(' ')}`.toLowerCase().includes(search.toLowerCase())
  ), [category, search]);
  const current = componentRegistry.find(entry => entry.id === selected) ?? entries[0];

  return (
    <Container maxWidth="lg" sx={{ py: 4 }}>
      <Stack spacing={3}>
        <Text variant="h1">Component gallery</Text>
        <ComponentGalleryControls category={category} onCategoryChange={setCategory} search={search} onSearchChange={setSearch} />
        {current ? <Card title={current.name}><ComponentDemo entry={current} /></Card> : null}
        <ComponentCatalog entries={entries} onSelect={setSelected} />
      </Stack>
    </Container>
  );
}
