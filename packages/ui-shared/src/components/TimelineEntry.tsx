import MuiTimelineItem from '@mui/lab/TimelineItem';
import TimelineContent from '@mui/lab/TimelineContent';
import TimelineDot from '@mui/lab/TimelineDot';
import TimelineOppositeContent from '@mui/lab/TimelineOppositeContent';
import TimelineSeparator from '@mui/lab/TimelineSeparator';
import { StatusChip, type ChipTone } from './StatusChip';
import { Text } from './Text';

export interface TimelineEntryProps {
  timestamp: string;
  title: string;
  description?: string;
  status: string;
  nextAction?: string;
  tone?: ChipTone;
  onClick?: () => void;
}

export function TimelineEntry({ timestamp, title, description, status, nextAction, tone = 'info', onClick }: TimelineEntryProps) {
  return (
    <MuiTimelineItem>
      <TimelineOppositeContent variant="body2" color="text.secondary">{new Date(timestamp).toLocaleString()}</TimelineOppositeContent>
      <TimelineSeparator><TimelineDot color="primary" /></TimelineSeparator>
      <TimelineContent
        onClick={onClick}
        role={onClick ? 'button' : undefined}
        tabIndex={onClick ? 0 : undefined}
        onKeyDown={onClick ? event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onClick(); } } : undefined}
        sx={onClick ? { cursor: 'pointer', borderRadius: 1, '&:hover': { bgcolor: 'action.hover' } } : undefined}
      >
        <Text variant="h3">{title}</Text>
        {description ? <Text variant="caption">{description}</Text> : null}
        <StatusChip tone={tone} label={status} />
        {nextAction ? <Text variant="caption">Next: {nextAction}</Text> : null}
      </TimelineContent>
    </MuiTimelineItem>
  );
}
