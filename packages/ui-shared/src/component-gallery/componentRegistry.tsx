import { Avatar } from '../components/Avatar';
import { Button } from '../components/Button';
import { Card } from '../components/Card';
import { DataTable } from '../components/DataTable';
import { DetailList } from '../components/DetailList';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { FilterBar } from '../components/FilterBar';
import { LoadingState } from '../components/LoadingState';
import { ContentContainer, FormField, Grid, PageHeader, SectionHeader, Stack } from '../components/Layout';
import { MetricCard } from '../components/MetricCard';
import { FreshnessBadge } from '../components/FreshnessBadge';
import { GraphCanvas } from '../components/GraphCanvas';
import { Icon } from '../components/Icon';
import { SemanticLink } from '../components/Link';
import { SemanticIconButton } from '../components/IconButton';
import { Select } from '../components/Select';
import { SegmentedControl } from '../components/SegmentedControl';
import { StatusChip } from '../components/StatusChip';
import { Text } from '../components/Text';
import { TextInput } from '../components/TextInput';
import { Timeline } from '../components/Timeline';
import { TimelineEntry } from '../components/TimelineEntry';
import { Tooltip } from '../components/Tooltip';
import { AuthLayoutDemo, LocaleSwitchDemo } from './authDemos';
import { ConfirmDialogDemo, WorkspaceShellDemo } from './componentDemos';
import type { ComponentRegistryEntry } from './registry';

export const componentRegistry: ComponentRegistryEntry[] = [
  { id: 'auth-layout', name: 'AuthLayout', category: 'layout', description: 'Responsive split-screen authentication surface with localized language switching.', tags: ['auth', 'login', 'layout', 'locale'], status: 'candidate', demo: AuthLayoutDemo },
  { id: 'locale-switch', name: 'LocaleSwitch', category: 'layout', description: 'Controlled zh-CN/en-US display-language switch.', tags: ['locale', 'language', 'i18n'], status: 'candidate', demo: LocaleSwitchDemo },
  { id: 'avatar', name: 'Avatar', category: 'data', description: 'Employee initials with semantic state color.', tags: ['avatar', 'employee', 'status'], status: 'stable', demo: () => <Avatar name="Engineering Lead" status="Busy" /> },
  { id: 'segmented-control', name: 'SegmentedControl', category: 'layout', description: 'Accessible switch between equivalent views.', tags: ['view', 'switch', 'navigation'], status: 'stable', demo: () => <SegmentedControl ariaLabel="Demo view" value="office" options={[{ value: 'office', label: 'Office' }, { value: 'org', label: 'Org chart' }]} onChange={() => undefined} /> },
  { id: 'graph-canvas', name: 'GraphCanvas', category: 'data', description: 'Deterministic SVG graph with keyboard selection and zoom.', tags: ['graph', 'organization', 'network'], status: 'stable', demo: () => <GraphCanvas nodes={[{ id: 'employee:1', label: 'Controller', subtitle: 'Company lead', x: 80, y: 80, kind: 'Employee', status: 'Active' }, { id: 'conversation:1', label: 'Delivery Room', subtitle: '3 members', x: 420, y: 80, kind: 'Conversation', status: 'Active' }]} edges={[{ id: 'member:1', source: 'employee:1', target: 'conversation:1', kind: 'member_of', weight: 1, label: 'Member' }]} onSelect={() => undefined} height={280} /> },
  { id: 'button', name: 'Button', category: 'mui', description: 'Semantic action button.', tags: ['action', 'button'], status: 'stable', demo: () => <Button>Run task</Button> },
  { id: 'text', name: 'Text', category: 'mui', description: 'Single-line and heading text.', tags: ['text', 'typography'], status: 'stable', demo: () => <Text variant="h3">Company status</Text> },
  { id: 'icon', name: 'Icon', category: 'mui', description: 'Shared icon registry.', tags: ['icon'], status: 'stable', demo: () => <Icon name="dashboard" /> },
  { id: 'icon-button', name: 'IconButton', category: 'mui', description: 'Accessible icon-only action.', tags: ['icon', 'button'], status: 'stable', demo: () => <SemanticIconButton label="Refresh"><Icon name="schedule" /></SemanticIconButton> },
  { id: 'link', name: 'Link', category: 'mui', description: 'Safe internal or external link.', tags: ['link', 'navigation'], status: 'stable', demo: () => <SemanticLink href="/projects">Open projects</SemanticLink> },
  { id: 'card', name: 'Card', category: 'mui', description: 'Surface container with header, status rail and footer.', tags: ['card', 'surface'], status: 'stable', demo: () => <Card title="Budget" footer={<Text variant="caption">Updated just now</Text>}>CNY 612,000 / 800,000</Card> },
  { id: 'data-table', name: 'DataTable', category: 'data', description: 'Typed table with stable row IDs and density controls.', tags: ['table', 'data'], status: 'stable', demo: () => <DataTable rows={[{ id: '1', name: 'Project A' }]} columns={[{ id: 'name', header: 'Name', render: row => row.name }]} getRowId={row => row.id} density="compact" /> },
  { id: 'metric-card', name: 'MetricCard', category: 'data', description: 'Compact KPI with semantic tone, icon and hint.', tags: ['metric', 'kpi', 'status'], status: 'stable', demo: () => <MetricCard label="Active projects" value="12" hint="2 at risk" icon="projects" tone="info" /> },
  { id: 'detail-list', name: 'DetailList', category: 'data', description: 'Responsive read-only key/value details.', tags: ['detail', 'key-value', 'metadata'], status: 'stable', demo: () => <DetailList columns={2} dense items={[{ label: 'Owner', value: 'Controller' }, { label: 'Version', value: 'v1.4.2', mono: true }, { label: 'Status', value: <StatusChip label="Active" tone="success" /> }]} /> },
  { id: 'input', name: 'TextInput', category: 'mui', description: 'Controlled text input.', tags: ['input', 'form'], status: 'stable', demo: () => <TextInput placeholder="Search projects" /> },
  { id: 'select', name: 'Select', category: 'mui', description: 'Controlled select input.', tags: ['select', 'form'], status: 'stable', demo: () => <Select label="Priority" value="p1" options={[{ value: 'p0', label: 'P0' }, { value: 'p1', label: 'P1' }]} onChange={() => undefined} /> },
  { id: 'tooltip', name: 'Tooltip', category: 'mui', description: 'Auxiliary explanation.', tags: ['tooltip', 'help'], status: 'stable', demo: () => <Tooltip title="Refresh data"><Button>Refresh</Button></Tooltip> },
  { id: 'status-chip', name: 'StatusChip', category: 'data', description: 'Semantic status label.', tags: ['status', 'chip'], status: 'stable', demo: () => <StatusChip tone="success" label="On track" /> },
  { id: 'freshness-badge', name: 'FreshnessBadge', category: 'data', description: 'Live, fresh, stale or unknown data state.', tags: ['freshness', 'status'], status: 'stable', demo: () => <FreshnessBadge freshness="stale" /> },
  { id: 'timeline', name: 'Timeline', category: 'project-employee', description: 'Employee and project event timeline.', tags: ['timeline', 'employee', 'project'], status: 'stable', demo: () => <Timeline><TimelineEntry timestamp="2026-11-23T08:00:00Z" title="Task submitted" status="InReview" nextAction="Independent review" /></Timeline> },
  { id: 'loading-state', name: 'LoadingState', category: 'form-feedback', description: 'Accessible loading indicator.', tags: ['loading', 'feedback'], status: 'stable', demo: () => <LoadingState /> },
  { id: 'empty-state', name: 'EmptyState', category: 'form-feedback', description: 'Actionable empty state.', tags: ['empty', 'feedback'], status: 'stable', demo: () => <EmptyState title="No projects" description="Create your first project." /> },
  { id: 'error-state', name: 'ErrorState', category: 'form-feedback', description: 'Error feedback with retry.', tags: ['error', 'feedback'], status: 'stable', demo: () => <ErrorState message="Something went wrong." /> },
  { id: 'stack', name: 'Stack', category: 'layout', description: 'Semantic horizontal or vertical layout.', tags: ['layout', 'stack'], status: 'stable', demo: () => <Stack direction="row" spacing={2}><Text>One</Text><Text>Two</Text></Stack> },
  { id: 'grid', name: 'Grid', category: 'layout', description: 'Responsive equal-width content grid.', tags: ['layout', 'grid'], status: 'stable', demo: () => <Grid columns={2}><Card>Left</Card><Card>Right</Card></Grid> },
  { id: 'page-header', name: 'PageHeader', category: 'layout', description: 'Consistent page title, description, metadata and actions.', tags: ['layout', 'header'], status: 'stable', demo: () => <PageHeader title="Organization" description="People, positions and governance." actions={<Button>Invite member</Button>} /> },
  { id: 'section-header', name: 'SectionHeader', category: 'layout', description: 'Section title, description and inline action.', tags: ['layout', 'section'], status: 'stable', demo: () => <SectionHeader title="Recent decisions" description="Auditable outcomes and evidence." action={<Button tone="neutral">View all</Button>} /> },
  { id: 'content-container', name: 'ContentContainer', category: 'layout', description: 'Bounded responsive page gutter and vertical rhythm.', tags: ['layout', 'container'], status: 'stable', demo: () => <ContentContainer maxWidth={720} sx={{ px: 0 }}><Card>Page content</Card></ContentContainer> },
  { id: 'workspace-shell', name: 'WorkspaceShell', category: 'layout', description: 'Router-agnostic desktop sidebar, mobile drawer and top bar.', tags: ['layout', 'shell', 'navigation'], status: 'candidate', demo: WorkspaceShellDemo },
  { id: 'filter-bar', name: 'FilterBar', category: 'form-feedback', description: 'Responsive search and filter action row.', tags: ['filter', 'search', 'toolbar'], status: 'stable', demo: () => <FilterBar action={<Button tone="neutral">Reset</Button>}><TextInput placeholder="Search records" inputProps={{ 'aria-label': 'Search records' }} /><Select label="Status" value="all" options={[{ value: 'all', label: 'All' }, { value: 'active', label: 'Active' }]} onChange={() => undefined} /></FilterBar> },
  { id: 'confirm-dialog', name: 'ConfirmDialog', category: 'form-feedback', description: 'Accessible confirmation for risky or destructive actions.', tags: ['dialog', 'confirm', 'danger'], status: 'stable', demo: ConfirmDialogDemo },
  { id: 'form-field', name: 'FormField', category: 'form-feedback', description: 'Label, input and hint wrapper.', tags: ['form', 'label'], status: 'stable', demo: () => <FormField label="Company name" hint="Visible to tenant members"><TextInput placeholder="Regent Company" /></FormField> }
];
