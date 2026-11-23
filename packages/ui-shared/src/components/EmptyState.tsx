import type { ReactNode } from 'react';
import { Text } from './Text';

export function EmptyState({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return <div><Text variant="h3">{title}</Text>{description ? <Text>{description}</Text> : null}{action}</div>;
}
