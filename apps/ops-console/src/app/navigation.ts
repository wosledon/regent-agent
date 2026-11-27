import type { WorkspaceNavGroup } from '@regent/ui-shared';

export interface OperationsNavigationOptions {
  translate: (key: string) => string;
}

export function buildOperationsNavigation({ translate }: OperationsNavigationOptions): WorkspaceNavGroup[] {
  const t = translate;
  return [
    {
      key: 'operate',
      label: t('shellLogin.groups.operate'),
      items: [
        { key: 'overview', label: t('shellLogin.links.overview'), path: '/', icon: 'overview' },
        { key: 'tenants', label: t('shellLogin.links.tenants'), path: '/tenants', icon: 'tenants' },
        { key: 'support', label: t('shellLogin.links.support'), path: '/support', icon: 'support' },
        { key: 'backups', label: t('shellLogin.links.backups'), path: '/backups', icon: 'backups' },
        { key: 'observability', label: t('shellLogin.links.observability'), path: '/observability', icon: 'observability' }
      ]
    },
    {
      key: 'assure',
      label: t('shellLogin.groups.assure'),
      items: [
        { key: 'audit', label: t('shellLogin.links.audit'), path: '/audit', icon: 'audit' },
        { key: 'platform', label: t('shellLogin.links.platform'), path: '/platform', icon: 'platform' },
        { key: 'status', label: t('shellLogin.links.status'), path: '/status', icon: 'status' }
      ]
    }
  ];
}
