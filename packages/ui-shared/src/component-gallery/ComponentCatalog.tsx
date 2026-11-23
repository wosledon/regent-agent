import { Button } from '../components/Button';
import { Card } from '../components/Card';
import { Text } from '../components/Text';
import { ComponentDemo } from './ComponentDemo';
import { ComponentStatusBadge } from './ComponentStatusBadge';
import type { ComponentRegistryEntry } from './registry';

export interface ComponentCatalogProps {
  entries: ComponentRegistryEntry[];
  onSelect: (id: string) => void;
}

export function ComponentCatalog({ entries, onSelect }: ComponentCatalogProps) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: 16 }}>
      {entries.map(entry => (
        <Card key={entry.id} title={entry.name} action={<ComponentStatusBadge status={entry.status} />}>
          <Text variant="caption">{entry.description}</Text>
          <ComponentDemo entry={entry} />
          <Button tone="neutral" onClick={() => onSelect(entry.id)}>Open demo</Button>
        </Card>
      ))}
    </div>
  );
}
