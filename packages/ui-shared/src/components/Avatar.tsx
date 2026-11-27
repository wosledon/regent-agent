import { Avatar as MuiAvatar } from '@mui/material';

export interface AvatarProps {
  name: string;
  status?: string;
  size?: 'small' | 'medium' | 'large';
}

export function Avatar({ name, status = 'Active', size = 'medium' }: AvatarProps) {
  const initials = name.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]).join('').toUpperCase();
  const color = status === 'Blocked' || status === 'Terminated' || status === 'Unavailable'
    ? 'error'
    : status === 'Busy' || status === 'RateLimited' || status === 'Degraded'
      ? 'warning'
      : 'primary';
  return <MuiAvatar sx={{ bgcolor: theme => theme.palette[color].main, width: size === 'large' ? 56 : size === 'small' ? 28 : 40, height: size === 'large' ? 56 : size === 'small' ? 28 : 40, fontWeight: 600, fontSize: size === 'small' ? '.75rem' : '.875rem' }}>{initials}</MuiAvatar>;
}
