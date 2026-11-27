import { useRef, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { Button, Card, FormField, Grid, Select, Stack, Text, TextInput } from '@regent/ui-shared';
import { useTranslation } from 'react-i18next';
import { LocalizedMutationFeedback } from '../common/LocalizedMutationFeedback';
import { api } from '../../lib/api-client/api';
import { localizedNumberRangeError, localizedRequiredTextError } from '../../lib/localizedValidation';
import { useDomainLabels } from '../../lib/domainLabels';

interface MemoryCaptureFormProps {
  onCaptured: () => void | Promise<void>;
}

export function MemoryCaptureForm({ onCaptured }: MemoryCaptureFormProps) {
  const { t } = useTranslation();
  const domainLabel = useDomainLabels();
  const [scope, setScope] = useState('organization');
  const [content, setContent] = useState('');
  const [sourceRef, setSourceRef] = useState('');
  const [confidence, setConfidence] = useState('0.8');
  const [taint, setTaint] = useState('trusted');
  const [submitted, setSubmitted] = useState(false);
  const submitting = useRef(false);
  const contentError = localizedRequiredTextError(content, t('common.content'), t);
  const sourceError = localizedRequiredTextError(sourceRef, t('memoryPage.sourceReference'), t);
  const confidenceError = localizedNumberRangeError(confidence, t('memoryPage.confidence'), 0, 1, t);
  const captureError = contentError ?? sourceError ?? confidenceError;
  const capture = useMutation({
    mutationFn: () => api.captureMemory({ scope, content: content.trim(), sourceRef: sourceRef.trim(), confidence: Number(confidence), taint }),
    onSuccess: async () => {
      setContent('');
      setSourceRef('');
      await onCaptured();
    },
    onSettled: () => { submitting.current = false; }
  });
  const submitCapture = () => {
    setSubmitted(true);
    if (submitting.current || capture.isPending || captureError) return;
    submitting.current = true;
    capture.mutate();
  };

  return (
    <Card title={t('memoryPage.captureClaim')} headingLevel="h2">
      <form onSubmit={event => { event.preventDefault(); submitCapture(); }}>
        <Stack spacing={2}>
          <Text variant="label">{t('memoryPage.claim')}</Text>
          <Select label={t('common.scope')} value={scope} disabled={capture.isPending} onChange={setScope} options={[
            { value: 'personal', label: domainLabel('personal') },
            { value: 'organization', label: domainLabel('organization') },
            { value: 'knowledge_base', label: domainLabel('knowledge_base') }
          ]} />
          <FormField label={t('common.content')} error={submitted ? contentError ?? undefined : undefined}>
            <TextInput multiline minRows={3} value={content} disabled={capture.isPending} onChange={setContent} placeholder={t('memoryPage.aTraceableFactualClaim')} />
          </FormField>
          <Text variant="label">{t('common.evidence')}</Text>
          <FormField label={t('memoryPage.sourceReference')} error={submitted ? sourceError ?? undefined : undefined}>
            <TextInput value={sourceRef} disabled={capture.isPending} onChange={setSourceRef} placeholder={t('memoryPage.decisionAuthPolicy')} />
          </FormField>
          <Grid columns={2}>
            <FormField label={t('memoryPage.confidence')} error={submitted ? confidenceError ?? undefined : undefined}>
              <TextInput type="number" value={confidence} disabled={capture.isPending} onChange={setConfidence} inputProps={{ min: 0, max: 1, step: 0.05 }} />
            </FormField>
            <Select label={t('memoryPage.taint')} value={taint} disabled={capture.isPending} onChange={setTaint} options={[
              { value: 'trusted', label: domainLabel('trusted') },
              { value: 'untrusted', label: domainLabel('untrusted') },
              { value: 'poisoned', label: domainLabel('poisoned') }
            ]} />
          </Grid>
          <Stack direction="row" spacing={1.5} wrap alignItems="center">
            <Button type="submit" size="small" disabled={!!captureError || capture.isPending} loading={capture.isPending}>{t('memoryPage.captureClaim')}</Button>
            <LocalizedMutationFeedback mutation={capture} successMessage={t('memoryPage.memoryClaimCaptured')} />
          </Stack>
        </Stack>
      </form>
    </Card>
  );
}
