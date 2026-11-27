import type { ComponentRegistryEntry } from './registry';

export interface ComponentDemoProps {
  entry: ComponentRegistryEntry;
}

export function ComponentDemo({ entry }: ComponentDemoProps) {
  const Demo = entry.demo;
  return (
    <div data-component={entry.id} style={{ padding: 24, border: '1px dashed var(--regent-border-default, #D8DEE9)', borderRadius: 8, maxWidth: '100%', overflowX: 'auto' }}>
      <Demo />
    </div>
  );
}
