import type { IconName, WorkspaceNavGroup, WorkspaceNavItem } from '@regent/ui-shared';

export interface TenantNavigationOptions {
  translate: (key: string) => string;
  canReadChat: boolean;
  canReadCollaboration: boolean;
  development: boolean;
}

export function buildTenantNavigation({ translate, canReadChat, canReadCollaboration, development }: TenantNavigationOptions): WorkspaceNavGroup[] {
  const t = translate;
  const item = (key: string, label: string, path: string, icon: IconName): WorkspaceNavItem => ({ key, label, path, icon });
  return [
    { key: 'overview', items: [item('dashboard', t('dashboard'), '/', 'overview')] },
    {
      key: 'delivery',
      label: t('shell.groups.delivery'),
      items: [
        item('projects', t('projects'), '/projects', 'projects'),
        item('butler', t('butler'), '/butler', 'agent')
      ]
    },
    {
      key: 'organization',
      label: t('shell.groups.organization'),
      items: [
        item('organization', t('organization'), '/organization', 'organization'),
        item('organizationManage', t('organizationManage'), '/organization/manage', 'people'),
        ...(canReadCollaboration ? [item('collaboration', t('collaboration'), '/organization/collaboration', 'activity')] : []),
        item('workflows', t('workflows'), '/workflows', 'workflow'),
        item('recruitment', t('recruitment'), '/recruitment', 'recruitment'),
        item('performance', t('performance'), '/performance', 'performance'),
        item('performanceActions', t('performanceActions'), '/performance/actions', 'approval')
      ]
    },
    ...(canReadChat ? [{
      key: 'communication',
      label: t('shell.groups.communication'),
      items: [item('messages', t('messages'), '/messages', 'messages')]
    }] : []),
    {
      key: 'intelligence',
      label: t('shell.groups.intelligence'),
      items: [
        item('models', t('models'), '/models', 'models'),
        item('budget', t('budget'), '/budget', 'budget'),
        item('runtimePolicy', t('runtimePolicy'), '/budget/runtime', 'schedule'),
        item('billing', t('billing'), '/billing', 'billing'),
        item('knowledge', t('knowledge'), '/knowledge', 'knowledge'),
        item('journal', t('journal'), '/journal', 'journal')
      ]
    },
    {
      key: 'governance',
      label: t('shell.groups.governance'),
      items: [
        item('agentGovernance', t('agentGovernance'), '/agent-governance', 'agentGovernance'),
        item('evaluations', t('evaluations'), '/evaluations', 'evaluations'),
        item('governance', t('governance'), '/governance/approvals', 'governance'),
        item('actionCenter', t('actionCenter'), '/governance/action-center', 'approvals'),
        item('notifications', t('notifications'), '/governance/notifications', 'notifications'),
        item('security', t('security'), '/governance/security', 'security'),
        item('lifecycle', t('lifecycle'), '/governance/lifecycle', 'lifecycle'),
        item('compliance', t('compliance'), '/governance/compliance', 'compliance')
      ]
    },
    {
      key: 'settings',
      label: t('shell.groups.settings'),
      items: [
        item('settings', t('settings'), '/settings', 'settings'),
        ...(development ? [item('components', t('components'), '/dev/components', 'components')] : [])
      ]
    }
  ];
}
