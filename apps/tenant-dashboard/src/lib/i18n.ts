import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

await i18n.use(initReactI18next).init({
  lng: 'zh-CN',
  fallbackLng: 'zh-CN',
  resources: {
    'zh-CN': { translation: { login: '登录', dashboard: '总览', projects: '项目与交付', organization: '组织与员工', messages: '消息与协作', models: '模型与预算', knowledge: '知识与公司志', governance: '治理与升级', settings: '设置' } },
    'en-US': { translation: { login: 'Sign in', dashboard: 'Overview', projects: 'Projects', organization: 'Organization', messages: 'Messages', models: 'Models & Budget', knowledge: 'Knowledge', governance: 'Governance', settings: 'Settings' } }
  },
  interpolation: { escapeValue: false }
});
