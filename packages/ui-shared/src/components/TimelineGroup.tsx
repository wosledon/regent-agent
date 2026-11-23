import type { PropsWithChildren } from 'react';
import { Text } from './Text';

export function TimelineGroup({ label, children }: PropsWithChildren<{ label: string }>) {
  return (
    <section>
      <Text variant="h2">{label}</Text>
      {children}
    </section>
  );
}
