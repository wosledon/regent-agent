import { Card, StatusChip, Text } from '@regent/ui-shared';

export function ModelsPage() {
  return (
    <div>
      <Text variant="h1">Models</Text>
      <Card title="OpenAI" action={<StatusChip label="Local adapter" tone="success" />}><Text>Native Chat Completions schema · local-cheap-v1</Text></Card>
      <Card title="Anthropic" action={<StatusChip label="Local adapter" tone="success" />}><Text>Native Messages schema · local-cheap-v1</Text></Card>
      <Card title="Local GPU"><Text>Not supported.</Text></Card>
    </div>
  );
}
