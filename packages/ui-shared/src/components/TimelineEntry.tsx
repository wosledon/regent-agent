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
}

export function TimelineEntry({ timestamp, title, description, status, nextAction, tone = 'info' }: TimelineEntryProps) {
  return (
    <MuiTimelineItem>
      <TimelineOppositeContent variant="body2" color="text.secondary">{new Date(timestamp).toLocaleString()}</TimelineOppositeContent>
      <TimelineSeparator><TimelineDot color="primary" /></TimelineSeparator>
      <TimelineContent>
        <Text variant="h3">{title}</Text>
        {description ? <Text variant="caption">{description}</Text> : null}
        <StatusChip tone={tone} label={status} />
        {nextAction ? <Text variant="caption">Next: {nextAction}</Text> : null}
      </TimelineContent>
    </MuiTimelineItem>
  );
}
