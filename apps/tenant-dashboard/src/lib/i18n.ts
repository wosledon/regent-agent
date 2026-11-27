import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { businessCopy, businessEnUS, businessZhCN, type BusinessCopyKey } from './businessTranslations';
import {
  applyDocumentLocale,
  applyLocalePreference,
  initialLocale,
  resolveLocale,
  supportedLocales
} from '@regent/ui-shared';

const initial = initialLocale();
applyDocumentLocale(initial);

i18n.on('languageChanged', language => {
  applyLocalePreference(resolveLocale(language));
});

await i18n.use(initReactI18next).init({
  lng: initial,
  fallbackLng: 'zh-CN',
  supportedLngs: [...supportedLocales],
  resources: {
    'zh-CN': { translation: {
      ...businessZhCN,
      login: '登录', dashboard: '总览', butler: '管家', projects: '项目与交付', organization: '组织与员工', organizationManage: '组织管理', collaboration: '组织协作可视化',
      messages: '消息与协作', workflows: '工作流', knowledge: '知识与公司志', journal: '公司志', performance: '绩效',
      evaluations: '模型评测', performanceActions: '绩效动作', recruitment: '招聘治理', agentGovernance: 'Agent 治理', models: '模型与预算', budget: '预算与成本', runtimePolicy: '成本与调度', billing: '套餐与计费',
      governance: '治理与升级', actionCenter: '行动中心', notifications: '通知中心', security: '安全与事件',
      lifecycle: '数据生命周期', compliance: '合规', settings: '设置', components: '组件展示',
      shell: {
        openNavigation: '打开导航',
        closeNavigation: '关闭导航',
        navigationLabel: '主导航',
        skipNavigation: '跳到主要内容',
        logout: '退出登录',
        signedInAs: '已登录：{{name}}',
        connection: {
          connected: '实时连接',
          reconnecting: '正在重连',
          disconnected: '连接已断开',
          offline: '离线'
        },
        groups: {
          delivery: '交付',
          organization: '组织与人员',
          communication: '沟通',
          intelligence: '模型与知识',
          governance: '治理与合规',
          settings: '设置'
        }
      },
      auth: {
        audience: '租户工作空间',
        headline: '让自主工作，始终由人掌控',
        description: '把项目、审批和公司知识汇聚在一个安全的工作空间，让团队目标清晰、执行可追溯。',
        highlightDeliveryTitle: '目标一致的团队',
        highlightDeliveryDescription: '明确责任人、交付状态和下一步行动。',
        highlightGovernanceTitle: '可信的执行边界',
        highlightGovernanceDescription: '权限、预算与审批在每一步保持可见。',
        highlightKnowledgeTitle: '持续积累的公司知识',
        highlightKnowledgeDescription: '让决策依据和协作经验可以复用。',
        footer: 'Regent Agent · 安全访问您的工作空间',
        localeLabel: '显示语言',
        title: '登录 Regent Agent',
        intro: '选择适合您当前情况的访问方式。',
        modeLabel: '认证方式',
        modes: {
          dev: '本地开发',
          password: '密码',
          register: '创建账户',
          emergency: '紧急访问'
        },
        devNotice: '本地开发登录仅在开发构建中提供，不会出现在生产环境。',
        email: '邮箱',
        emailPlaceholder: 'owner@example.test',
        password: '密码',
        passwordPlaceholder: '至少 10 个字符',
        tenantId: '租户 ID',
        tenantIdPlaceholder: '租户 UUID',
        emergencyCredential: '紧急凭据',
        purpose: '用途',
        purposePlaceholder: '恢复不可用的身份提供商',
        displayName: '显示名称',
        displayNamePlaceholder: '显示名称',
        companyName: '公司名称',
        companyNamePlaceholder: '公司名称',
        submit: {
          signIn: '登录',
          createAccount: '创建账户',
          emergency: '使用紧急访问'
        },
        loading: {
          checking: '正在检查您的会话…',
          submitting: '正在登录…',
          loggingOut: '正在退出…'
        },
        recovery: {
          title: '保存恢复码',
          description: '这些恢复码仅显示一次，请妥善保存到安全的密码管理器中。',
          continue: '继续'
        },
        emergencyResult: {
          title: '保存轮换后的紧急凭据',
          description: '紧急会话仅限用于 SSO 恢复和账户访问，有效期为 15 分钟。',
          continue: '继续前往恢复设置'
        },
        signedIn: {
          title: '已登录：{{name}}',
          description: '您可以继续进入工作空间，或退出当前浏览器会话。',
          continue: '继续',
          logout: '退出登录'
        },
        errors: {
          invalidCredentials: '邮箱或密码不正确，请重试。',
          emailInUse: '该邮箱已注册，请直接登录。',
          invalidRequest: '请检查填写的信息后重试。',
          forbidden: '当前账户不允许使用此登录方式。',
          notFound: '找不到请求的账户或租户。',
          rateLimited: '尝试次数过多，请稍后再试。',
          unavailable: '认证服务暂时不可用，请稍后重试。',
          network: '无法连接认证服务，请检查网络后重试。',
          unexpected: '无法完成登录，请重试。'
        }
      }
    } },
    'en-US': { translation: {
      ...businessEnUS,
      login: 'Sign in', dashboard: 'Overview', butler: 'Butler', projects: 'Projects', organization: 'Organization', organizationManage: 'Organization management', collaboration: 'Collaboration workspace',
      messages: 'Messages', workflows: 'Workflows', knowledge: 'Knowledge', journal: 'Company journal', performance: 'Performance',
      evaluations: 'Evaluations', performanceActions: 'Performance actions', recruitment: 'Recruitment', agentGovernance: 'Agent governance', models: 'Models & Budget', budget: 'Budget & Cost', runtimePolicy: 'Cost & Scheduling', billing: 'Plan & Billing',
      governance: 'Governance', actionCenter: 'Action center', notifications: 'Notifications', security: 'Security & Incidents',
      lifecycle: 'Data lifecycle', compliance: 'Compliance', settings: 'Settings', components: 'Components',
      shell: {
        openNavigation: 'Open navigation',
        closeNavigation: 'Close navigation',
        navigationLabel: 'Main navigation',
        skipNavigation: 'Skip to main content',
        logout: 'Logout',
        signedInAs: 'Signed in as {{name}}',
        connection: {
          connected: 'Live',
          reconnecting: 'Reconnecting',
          disconnected: 'Disconnected',
          offline: 'Offline'
        },
        groups: {
          delivery: 'Delivery',
          organization: 'Organization & people',
          communication: 'Communication',
          intelligence: 'Models & knowledge',
          governance: 'Governance & compliance',
          settings: 'Settings'
        }
      },
      auth: {
        audience: 'TENANT WORKSPACE',
        headline: 'Autonomous work, with people in control',
        description: 'Bring projects, approvals and company knowledge into one secure workspace where goals stay clear and execution remains accountable.',
        highlightDeliveryTitle: 'Aligned teams',
        highlightDeliveryDescription: 'Clear owners, delivery status and next actions.',
        highlightGovernanceTitle: 'Governed execution',
        highlightGovernanceDescription: 'Permissions, budgets and approvals stay visible at every step.',
        highlightKnowledgeTitle: 'Company knowledge that compounds',
        highlightKnowledgeDescription: 'Reuse decision context and collaboration experience.',
        footer: 'Regent Agent · Secure access to your workspace',
        localeLabel: 'Display language',
        title: 'Sign in to Regent Agent',
        intro: 'Choose the access method that fits your situation.',
        modeLabel: 'Authentication mode',
        modes: {
          dev: 'Local development',
          password: 'Password',
          register: 'Create account',
          emergency: 'Emergency access'
        },
        devNotice: 'Local development sign-in is available only in development builds and never appears in production.',
        email: 'Email',
        emailPlaceholder: 'owner@example.test',
        password: 'Password',
        passwordPlaceholder: 'At least 10 characters',
        tenantId: 'Tenant ID',
        tenantIdPlaceholder: 'Tenant UUID',
        emergencyCredential: 'Emergency credential',
        purpose: 'Purpose',
        purposePlaceholder: 'Restore unavailable identity provider',
        displayName: 'Display name',
        displayNamePlaceholder: 'Display name',
        companyName: 'Company name',
        companyNamePlaceholder: 'Company name',
        submit: {
          signIn: 'Sign in',
          createAccount: 'Create account',
          emergency: 'Use emergency access'
        },
        loading: {
          checking: 'Checking your session…',
          submitting: 'Signing in…',
          loggingOut: 'Signing out…'
        },
        recovery: {
          title: 'Save recovery codes',
          description: 'These codes are shown once. Store them in a secure password manager.',
          continue: 'Continue'
        },
        emergencyResult: {
          title: 'Store the rotated emergency credential',
          description: 'The emergency session is restricted to SSO recovery and account access for 15 minutes.',
          continue: 'Continue to recovery settings'
        },
        signedIn: {
          title: 'Signed in as {{name}}',
          description: 'You can continue to your workspace or sign out of this browser.',
          continue: 'Continue',
          logout: 'Logout'
        },
        errors: {
          invalidCredentials: 'The email or password is incorrect. Please try again.',
          emailInUse: 'An account with this email already exists. Try signing in instead.',
          invalidRequest: 'Check the information you entered and try again.',
          forbidden: 'This sign-in method is not allowed for this account.',
          notFound: 'The requested account or tenant could not be found.',
          rateLimited: 'Too many attempts. Wait a moment before trying again.',
          unavailable: 'Authentication is temporarily unavailable. Please try again later.',
          network: 'We could not reach the authentication service. Check your connection and try again.',
          unexpected: 'We could not complete sign-in. Please try again.'
        }
      }
    } }
  },
  interpolation: { escapeValue: false },
  returnNull: false,
  // Business keys always have a zh-CN source value. Unknown keys render nothing rather than leaking key names.
  parseMissingKeyHandler: key => {
    const copy = businessCopy[key as BusinessCopyKey]?.zh;
    return typeof copy === 'string' ? copy : copy?.other ?? '';
  }
});

export default i18n;
