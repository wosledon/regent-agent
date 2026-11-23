import type { PropsWithChildren } from 'react';
import MuiTimeline from '@mui/lab/Timeline';

export function Timeline({ children }: PropsWithChildren) {
  return <MuiTimeline position="right">{children}</MuiTimeline>;
}
