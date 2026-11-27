import { useId, useState, type ReactNode } from 'react';
import { Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField } from '@mui/material';
import { Button as RegentButton } from './Button';
import { Text } from './Text';

export interface ConfirmDialogProps {
  open: boolean;
  title: string;
  description?: ReactNode;
  confirmLabel: string;
  cancelLabel?: string;
  tone?: 'primary' | 'danger';
  loading?: boolean;
  requireText?: string;
  onConfirm: () => void;
  onClose: () => void;
  children?: ReactNode;
}

export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel,
  cancelLabel = 'Cancel',
  tone = 'primary',
  loading = false,
  requireText,
  onConfirm,
  onClose,
  children
}: ConfirmDialogProps) {
  const titleId = useId();
  const descriptionId = useId();
  const [confirmation, setConfirmation] = useState('');
  const blocked = Boolean(requireText && confirmation.trim() !== requireText);
  const close = () => {
    if (loading) return;
    setConfirmation('');
    onClose();
  };
  return (
    <Dialog open={open} onClose={loading ? undefined : close} maxWidth="xs" fullWidth aria-labelledby={titleId} aria-describedby={description ? descriptionId : undefined}>
      <DialogTitle id={titleId}>{title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 0.5 }}>
          {description ? <Text id={descriptionId} variant="secondary">{description}</Text> : null}
          {children}
          {requireText ? <TextField autoFocus fullWidth size="small" label={requireText} value={confirmation} onChange={event => setConfirmation(event.target.value)} disabled={loading} /> : null}
        </Stack>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}>
        <RegentButton tone="neutral" onClick={close} disabled={loading}>{cancelLabel}</RegentButton>
        <RegentButton tone={tone === 'danger' ? 'danger' : 'primary'} onClick={onConfirm} loading={loading} disabled={blocked}>{confirmLabel}</RegentButton>
      </DialogActions>
    </Dialog>
  );
}
