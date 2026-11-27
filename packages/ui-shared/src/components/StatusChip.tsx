import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import WarningIcon from '@mui/icons-material/Warning';
import ErrorIcon from '@mui/icons-material/Error';
import InfoIcon from '@mui/icons-material/Info';
import CircleIcon from '@mui/icons-material/Circle';
import type { ChipProps } from '@mui/material';
import { Chip as MuiChip } from '@mui/material';
import { designTokens } from '../theme/theme';

export type ChipTone = 'neutral' | 'success' | 'warning' | 'error' | 'info';

export interface StatusChipProps extends Omit<ChipProps, 'color'> {
  tone?: ChipTone;
  soft?: boolean;
}

const toneStyles: Record<ChipTone, { color: string; background: string; border: string; icon: typeof CircleIcon }> = {
  neutral: { color: designTokens.color.textSecondary, background: '#F2F4F7', border: '#D0D5DD', icon: CircleIcon },
  success: { color: '#0F5C37', background: designTokens.color.successSoft, border: '#B7E1C8', icon: CheckCircleIcon },
  warning: { color: '#6E4100', background: designTokens.color.warningSoft, border: '#E7C46A', icon: WarningIcon },
  error: { color: designTokens.color.error, background: designTokens.color.errorSoft, border: '#FECDCA', icon: ErrorIcon },
  info: { color: designTokens.color.info, background: designTokens.color.infoSoft, border: '#B2DDFF', icon: InfoIcon }
};

export function StatusChip({ tone = 'neutral', soft = true, icon, sx, ...props }: StatusChipProps) {
  const style = toneStyles[tone];
  const Icon = style.icon;
  return (
    <MuiChip
      size="small"
      variant="outlined"
      icon={icon ?? <Icon sx={{ fontSize: 14 }} />}
      sx={[{
        borderRadius: designTokens.radius.xs,
        color: style.color,
        backgroundColor: soft ? style.background : 'transparent',
        borderColor: style.border,
        '& .MuiChip-icon': { color: style.color }
      }, ...(Array.isArray(sx) ? sx : sx ? [sx] : [])]}
      {...props}
    />
  );
}
