import DashboardIcon from '@mui/icons-material/Dashboard';
import GroupsIcon from '@mui/icons-material/Groups';
import ScienceIcon from '@mui/icons-material/Science';
import SettingsIcon from '@mui/icons-material/Settings';
import NotificationsIcon from '@mui/icons-material/Notifications';
import SearchIcon from '@mui/icons-material/Search';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import WarningIcon from '@mui/icons-material/Warning';
import ErrorIcon from '@mui/icons-material/Error';
import ScheduleIcon from '@mui/icons-material/Schedule';
import type { SvgIconProps } from '@mui/material';

const icons = {
  dashboard: DashboardIcon,
  organization: GroupsIcon,
  evaluation: ScienceIcon,
  settings: SettingsIcon,
  notification: NotificationsIcon,
  search: SearchIcon,
  success: CheckCircleIcon,
  warning: WarningIcon,
  error: ErrorIcon,
  schedule: ScheduleIcon
} as const;

export type IconName = keyof typeof icons;

export interface IconProps extends Omit<SvgIconProps, 'component'> {
  name: IconName;
}

export function Icon({ name, ...props }: IconProps) {
  const Component = icons[name];
  return <Component aria-hidden="true" fontSize="small" {...props} />;
}
