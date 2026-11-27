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
import MenuIcon from '@mui/icons-material/Menu';
import CloseIcon from '@mui/icons-material/Close';
import FolderIcon from '@mui/icons-material/Folder';
import ChatIcon from '@mui/icons-material/Chat';
import WorkflowIcon from '@mui/icons-material/AccountTree';
import MemoryIcon from '@mui/icons-material/Psychology';
import JournalIcon from '@mui/icons-material/MenuBook';
import PerformanceIcon from '@mui/icons-material/TrendingUp';
import RecruitmentIcon from '@mui/icons-material/PersonAdd';
import AgentIcon from '@mui/icons-material/SmartToy';
import EvaluationIcon from '@mui/icons-material/FactCheck';
import ModelIcon from '@mui/icons-material/Hub';
import WalletIcon from '@mui/icons-material/AccountBalanceWallet';
import BillingIcon from '@mui/icons-material/ReceiptLong';
import GovernanceIcon from '@mui/icons-material/Policy';
import ApprovalIcon from '@mui/icons-material/TaskAlt';
import SecurityIcon from '@mui/icons-material/Shield';
import LifecycleIcon from '@mui/icons-material/RotateRight';
import ComplianceIcon from '@mui/icons-material/Verified';
import ComponentsIcon from '@mui/icons-material/Widgets';
import OverviewIcon from '@mui/icons-material/SpaceDashboard';
import TenantsIcon from '@mui/icons-material/Apartment';
import SupportIcon from '@mui/icons-material/Handshake';
import BackupIcon from '@mui/icons-material/Backup';
import AuditIcon from '@mui/icons-material/History';
import PlatformIcon from '@mui/icons-material/Dns';
import ObservabilityIcon from '@mui/icons-material/MonitorHeart';
import StatusIcon from '@mui/icons-material/Public';
import LogoutIcon from '@mui/icons-material/Logout';
import UserIcon from '@mui/icons-material/Person';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import ChevronDownIcon from '@mui/icons-material/ExpandMore';
import RefreshIcon from '@mui/icons-material/Refresh';
import FilterIcon from '@mui/icons-material/FilterList';
import MoreIcon from '@mui/icons-material/MoreHoriz';
import ExternalIcon from '@mui/icons-material/OpenInNew';
import CheckIcon from '@mui/icons-material/Check';
import ClockIcon from '@mui/icons-material/AccessTime';
import DatabaseIcon from '@mui/icons-material/Storage';
import CloudIcon from '@mui/icons-material/Cloud';
import ActivityIcon from '@mui/icons-material/Insights';
import ArrowLeftIcon from '@mui/icons-material/ArrowBack';
import PlusIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/DeleteOutline';
import VisibilityIcon from '@mui/icons-material/Visibility';
import DownloadIcon from '@mui/icons-material/Download';
import UploadIcon from '@mui/icons-material/Upload';
import LinkIcon from '@mui/icons-material/Link';
import InfoIcon from '@mui/icons-material/Info';
import type { SvgIconProps } from '@mui/material';

const icons = {
  dashboard: DashboardIcon,
  overview: OverviewIcon,
  organization: GroupsIcon,
  people: GroupsIcon,
  evaluation: ScienceIcon,
  settings: SettingsIcon,
  notification: NotificationsIcon,
  notifications: NotificationsIcon,
  search: SearchIcon,
  success: CheckCircleIcon,
  warning: WarningIcon,
  error: ErrorIcon,
  schedule: ScheduleIcon,
  clock: ClockIcon,
  menu: MenuIcon,
  close: CloseIcon,
  projects: FolderIcon,
  folder: FolderIcon,
  messages: ChatIcon,
  chat: ChatIcon,
  workflows: WorkflowIcon,
  workflow: WorkflowIcon,
  knowledge: MemoryIcon,
  memory: MemoryIcon,
  journal: JournalIcon,
  performance: PerformanceIcon,
  recruitment: RecruitmentIcon,
  agentGovernance: AgentIcon,
  agent: AgentIcon,
  evaluations: EvaluationIcon,
  models: ModelIcon,
  model: ModelIcon,
  budget: WalletIcon,
  wallet: WalletIcon,
  billing: BillingIcon,
  governance: GovernanceIcon,
  approvals: ApprovalIcon,
  approval: ApprovalIcon,
  security: SecurityIcon,
  lifecycle: LifecycleIcon,
  compliance: ComplianceIcon,
  components: ComponentsIcon,
  tenants: TenantsIcon,
  support: SupportIcon,
  backups: BackupIcon,
  backup: BackupIcon,
  audit: AuditIcon,
  platform: PlatformIcon,
  observability: ObservabilityIcon,
  status: StatusIcon,
  logout: LogoutIcon,
  user: UserIcon,
  chevronRight: ChevronRightIcon,
  chevronDown: ChevronDownIcon,
  refresh: RefreshIcon,
  filter: FilterIcon,
  more: MoreIcon,
  external: ExternalIcon,
  check: CheckIcon,
  database: DatabaseIcon,
  cloud: CloudIcon,
  activity: ActivityIcon,
  arrowLeft: ArrowLeftIcon,
  plus: PlusIcon,
  edit: EditIcon,
  delete: DeleteIcon,
  visibility: VisibilityIcon,
  download: DownloadIcon,
  upload: UploadIcon,
  link: LinkIcon,
  info: InfoIcon
} as const;

export type IconName = keyof typeof icons;

export interface IconProps extends Omit<SvgIconProps, 'component'> {
  name: IconName;
}

export function Icon({ name, ...props }: IconProps) {
  const Component = icons[name];
  return <Component aria-hidden="true" fontSize="small" {...props} />;
}
