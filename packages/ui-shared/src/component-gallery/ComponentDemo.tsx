import type { ComponentRegistryEntry } from './registry';

export interface ComponentDemoProps {
  entry: ComponentRegistryEntry;
}

export function ComponentDemo({ entry }: ComponentDemoProps) {
  const Demo = entry.demo;
  return (
    <div data-component={entry.id} style={{ padding: 24, border: '1px dashed #c7cde0', borderRadius: 12 }}>
      <Demo />
    </div>
  );
}
