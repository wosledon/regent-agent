import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { Button, Card, FormField, Select, Stack, Text, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

interface MemoryCaptureFormProps {
  onCaptured: () => void | Promise<void>;
}

export function MemoryCaptureForm({ onCaptured }: MemoryCaptureFormProps) {
  const [scope, setScope] = useState('organization');
  const [content, setContent] = useState('');
  const [sourceRef, setSourceRef] = useState('');
  const [confidence, setConfidence] = useState('0.8');
  const [taint, setTaint] = useState('trusted');
  const capture = useMutation({
    mutationFn: () => api.captureMemory({ scope, content, sourceRef, confidence: Number(confidence), taint }),
    onSuccess: async () => {
      setContent('');
      setSourceRef('');
      await onCaptured();
    }
  });

  return (
    <Card title="Capture claim">
      <form onSubmit={event => { event.preventDefault(); capture.mutate(); }}>
        <Stack spacing={2}>
          <Select label="Scope" value={scope} onChange={setScope} options={[
            { value: 'personal', label: 'Personal' },
            { value: 'organization', label: 'Organization' },
            { value: 'knowledge_base', label: 'Knowledge base' }
          ]} />
          <FormField label="Content">
            <TextInput multiline minRows={3} value={content} onChange={setContent} placeholder="A traceable factual claim" />
          </FormField>
          <FormField label="Source reference">
            <TextInput value={sourceRef} onChange={setSourceRef} placeholder="decision:auth-policy" />
          </FormField>
          <FormField label="Confidence">
            <TextInput type="number" value={confidence} onChange={setConfidence} inputProps={{ min: 0, max: 1, step: 0.05 }} />
          </FormField>
          <Select label="Taint" value={taint} onChange={setTaint} options={[
            { value: 'trusted', label: 'Trusted' },
            { value: 'untrusted', label: 'Untrusted' },
            { value: 'poisoned', label: 'Poisoned' }
          ]} />
          <Button type="submit" disabled={!content.trim() || !sourceRef.trim()} loading={capture.isPending}>Capture claim</Button>
          {capture.error ? <Text color="error">{capture.error.message}</Text> : null}
        </Stack>
      </form>
    </Card>
  );
}
