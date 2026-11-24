import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

await i18n.use(initReactI18next).init({
  lng: 'zh-CN',
  fallbackLng: 'zh-CN',
  resources: {
    'zh-CN': { translation: {
      login: '登录', dashboard: '总览', butler: '管家', projects: '项目与交付', organization: '组织与员工', organizationManage: '组织管理', collaboration: '组织协作可视化',
      messages: '消息与协作', workflows: '工作流', knowledge: '知识与公司志', journal: '公司志', performance: '绩效',
      evaluations: '模型评测', performanceActions: '绩效动作', recruitment: '招聘治理', agentGovernance: 'Agent 治理', models: '模型与预算', budget: '预算与成本', runtimePolicy: '成本与调度', billing: '套餐与计费',
      governance: '治理与升级', actionCenter: '行动中心', notifications: '通知中心', security: '安全与事件',
      lifecycle: '数据生命周期', compliance: '合规', settings: '设置', components: '组件展示'
    } },
    'en-US': { translation: {
      login: 'Sign in', dashboard: 'Overview', butler: 'Butler', projects: 'Projects', organization: 'Organization', organizationManage: 'Organization management', collaboration: 'Collaboration workspace',
      messages: 'Messages', workflows: 'Workflows', knowledge: 'Knowledge', journal: 'Company journal', performance: 'Performance',
      evaluations: 'Evaluations', performanceActions: 'Performance actions', recruitment: 'Recruitment', agentGovernance: 'Agent governance', models: 'Models & Budget', budget: 'Budget & Cost', runtimePolicy: 'Cost & Scheduling', billing: 'Plan & Billing',
      governance: 'Governance', actionCenter: 'Action center', notifications: 'Notifications', security: 'Security & Incidents',
      lifecycle: 'Data lifecycle', compliance: 'Compliance', settings: 'Settings', components: 'Components'
    } }
  },
  interpolation: { escapeValue: false }
});

export default i18n;
