import { Box } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { Stack, StatusChip, Text, type ChipTone } from '@regent/ui-shared';

export interface LocalizedTimelineEntryProps {
  timestampLabel: string;
  title: string;
  description?: string;
  status: string;
  nextAction?: string;
  tone?: ChipTone;
  onClick?: () => void;
}

export function LocalizedTimelineEntry({ timestampLabel, title, description, status, nextAction, tone = 'info', onClick }: LocalizedTimelineEntryProps) {
  const { t } = useTranslation();
  return (
    <Box
      onClick={onClick}
      role={onClick ? 'button' : undefined}
      tabIndex={onClick ? 0 : undefined}
      onKeyDown={onClick ? event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onClick(); } } : undefined}
      sx={onClick ? { cursor: 'pointer', borderRadius: 1, '&:hover': { bgcolor: 'action.hover' } } : undefined}
    >
      <Stack spacing={0.5}>
        <Text variant="caption">{timestampLabel}</Text>
        <Text variant="h3">{title}</Text>
        {description ? <Text variant="caption">{description}</Text> : null}
        <StatusChip tone={tone} label={status} />
        {nextAction ? <Text variant="caption">{t('common.nextAction', { action: nextAction })}</Text> : null}
      </Stack>
    </Box>
  );
}
