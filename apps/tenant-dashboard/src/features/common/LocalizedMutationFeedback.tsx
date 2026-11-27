import { StatusChip, type ChipTone } from '@regent/ui-shared';
import { LocalizedErrorState } from './LocalizedErrorState';

interface MutationFeedbackState {
  isSuccess: boolean;
  error: unknown;
}

interface LocalizedMutationFeedbackProps {
  mutation: MutationFeedbackState;
  successMessage?: string;
  successTone?: ChipTone;
  showSuccess?: boolean;
  errorMessage?: string;
}

export function LocalizedMutationFeedback({
  mutation,
  successMessage,
  successTone = 'success',
  showSuccess = true,
  errorMessage
}: LocalizedMutationFeedbackProps) {
  return <>
    {showSuccess && mutation.isSuccess && successMessage
      ? <StatusChip label={successMessage} tone={successTone} />
      : null}
    {mutation.error ? <LocalizedErrorState error={errorMessage ?? mutation.error} /> : null}
  </>;
}
