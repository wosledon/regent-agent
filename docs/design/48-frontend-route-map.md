# 48 前端页面与路由地图

- 版本：v0.1（待评审）
- 范围：Tenant Dashboard 与 Ops Console 的页面、路由、权限、布局和组件映射
- 关联设计：[35 租户仪表盘信息架构](./35-tenant-dashboard-information-architecture.md)、[41 内部通知中心](./41-internal-notification-center.md)、[前端技术文档](../Regent-Agent-前端技术文档.md)

## 1. 范围

- 两个应用使用独立路由、认证和部署。
- Tenant Dashboard 不包含运营页面。
- Ops Console 不包含租户公司日常页面。
- 路由守卫只改善体验，服务端 API 仍逐次鉴权。
- 两个应用等待服务端 Cookie 会话引导完成后放行；未认证深链接仅保留经校验的同源相对 path/query/hash 返回目标，401 和退出失败清除本地认证外壳与查询缓存。403 保留 PermissionDenied，网络/5xx 保留可重试错误；Ops `/status` 的公开访问不受匿名引导或全局 401 导航影响。
- 运营后台不进入租户导航。
- 开发期组件页 `/dev/components` 不进入生产导航；生产深链接显示仅开发可用提示。组件目录仅展示摘要，选中 Demo 单独挂载。
- 两个应用共享路由无关的 `WorkspaceShell`：桌面为 248px 深墨蓝持久导航，移动端为 280px 临时抽屉；导航数据、权限过滤和用户操作仍由各应用提供。

## 2. Tenant Dashboard 站点地图

```mermaid
flowchart TB
    Login["登录 / OIDC / 找回"] --> Shell["AppShell"]
    Shell --> Home["总览"]
    Shell --> Projects["项目与交付"]
    Shell --> Organization["组织与员工"]
    Shell --> Messages["消息与协作"]
    Shell --> Models["模型与预算"]
    Shell --> Knowledge["知识与公司志"]
    Shell --> Governance["治理与升级"]
    Shell --> Settings["设置"]

    Projects --> ProjectList["项目列表"]
    Projects --> ProjectDetail["项目详情"]
    ProjectDetail --> Requirements["需求树"]
    ProjectDetail --> Tasks["任务 / 看板"]
    ProjectDetail --> Releases["发布门禁"]

    Organization --> EmployeeList["员工列表"]
    Organization --> EmployeeDetail["员工详情 / 工作时间线"]
    Organization --> Positions["岗位与组织树"]

    Governance --> ActionCenter["行动中心"]
    Governance --> Approvals["升级与审批"]
    Governance --> Incidents["安全事件"]
    Governance --> Audit["审计与合规"]
    Governance --> Reviews["人事事后审查"]

    Settings --> Profile["个人设置"]
    Settings --> Tenant["租户设置"]
    Settings --> Locale["语言与时区"]
    Settings --> Notifications["通知偏好"]
```

## 3. Tenant Dashboard 路由表

| 路由 | 页面 | 主要 Capability | 布局 | 核心组件 |
|---|---|---|---|---|
| `/login` | 登录 | 匿名 | AuthLayout | 认证方式表单、LocaleSwitch、恢复码/紧急凭据确认 |
| `/auth/callback` | OIDC 回调 | 匿名 | 最小布局 | 状态提示、错误恢复 |
| `/account/recovery` | 恢复码找回 | 匿名 | AuthLayout | 恢复码表单 |
| `/` | Boss 首屏 | `dashboard.read` | AppShell | 七类首屏卡片 |
| `/projects` | 项目列表 | `project.read` | ListLayout | ProjectListCard、FilterBar、DataTable |
| `/projects/:projectId` | 项目详情 | `project.read` | DetailLayout | ProjectDetailHeader、ProjectStageStepper |
| `/projects/:projectId/requirements` | 需求树 | `requirement.read` | DetailLayout | RequirementTree、EvidencePanel |
| `/projects/:projectId/tasks` | 任务看板 | `task.read` | DetailLayout | TaskBoard、FilterBar |
| `/projects/:projectId/releases` | 发布门禁 | `release.read` | DetailLayout | ReleaseChecklist、AuditTimeline |
| `/organization` | 组织与员工 | `organization.read` | ListLayout | OrganizationTree、EmployeeStateBadge |
| `/organization/collaboration` | 组织协作可视化 | `effective_access.can_read_collaboration` | WorkspaceLayout | OfficeView、OrgChartView、NetworkView、ActivityTimeline、CoordinationPanel、TracePanel |
| `/organization/employees/:employeeId` | 员工详情 | `employee.read` | DetailLayout | EmployeeWorkTimeline、WorkLedgerTable |
| `/organization/positions` | 岗位管理 | `position.read` | DetailLayout | 岗位树、能力矩阵 |
| `/messages` | Agent 消息只读列表 | `effective_access.can_read_chat` | SplitLayout | ConversationList、NotificationMenu、AgentActorBadge |
| `/messages?conversation=:conversationId` | 会话详情 | `effective_access.can_read_chat` | SplitLayout | 消息流、委派/交接面板 |
| `/models` | 模型池 | `models.read` | DetailLayout | ModelBindingCard、ProviderRegionSelect |
| `/budget` | 预算与成本 | `budget.read` | DetailLayout | BudgetCostCard、CostBreakdownChart |
| `/billing` | 套餐与计费 | Owner | DetailLayout | PlanQuotaTable、InvoiceTable、BillingLedgerTable |
| `/knowledge` | 知识与记忆 | `memory.read` | DetailLayout | 搜索结果、来源和权限 |
| `/journal` | 公司志 | `journal.read` | DetailLayout | AuditTimeline、RecentDecisionList |
| `/governance/action-center` | 行动中心 | `action_center.read` | ListLayout | ActionCenterList |
| `/governance/approvals` | 升级与审批 | `escalations.read` | DetailLayout | ApprovalPanel |
| `/governance/incidents` | 安全事件 | `incident.read` | DetailLayout | IncidentTimeline |
| `/governance/audit` | 审计与合规 | `audit.read` | DetailLayout | ComplianceStatusCard、AuditTimeline |
| `/governance/performance-reviews` | 人事事后审查 | `performance.review.read` | DetailLayout | 审查列表、证据面板 |
| `/settings/profile` | 个人设置 | `self.manage` | SettingsLayout | 表单、会话管理 |
| `/settings/tenant` | 租户设置 | `tenant.manage` | SettingsLayout | 租户信息、成员和所有权、OIDC 策略、紧急凭据、Owner 数据导出 |
| `/settings/locale` | 语言与时区 | `self.manage` | SettingsLayout | LocaleSelect、DateTimeField |
| `/settings/notifications` | 通知偏好 | `self.manage` | SettingsLayout | 通知级别和静默设置 |
| `/dev/components` | 组件展示页 | 开发环境 | GalleryLayout | ComponentGalleryPage |

## 4. Ops Console 站点地图

```mermaid
flowchart TB
    OpsLogin["Operator 本地演示登录"] --> OpsShell["Ops AppShell"]
    OpsShell --> Overview["运营总览"]
    OpsShell --> Tenants["租户账户"]
    OpsShell --> Resources["资源池"]
    OpsShell --> Incidents["事件与值班"]
    OpsShell --> Support["SupportGrant"]
    OpsShell --> Backups["备份与恢复"]
    OpsShell --> Audit["运营审计"]
    OpsShell --> Settings["运营设置"]
```

## 5. Ops Console 路由表

| 路由 | 页面 | 主要角色 | 布局 | 核心组件 |
|---|---|---|---|---|
| `/login` | Operator 登录 | 匿名 | AuthLayout | 本地/demo Operator 入口、LocaleSwitch、安全错误反馈 |
| `/` | 运营总览 | Ops Viewer | AppShell | 容量、事件、SLO 卡片 |
| `/tenants` | 租户列表 | Tenant Admin | ListLayout | TenantAccountTable |
| `/tenants/:tenantId` | 租户详情 | Tenant Admin | DetailLayout | 配额、状态、SupportGrant 历史 |
| `/resources` | Runner/Gateway 池 | SRE | DetailLayout | RunnerPoolPanel |
| `/incidents` | 事件与值班 | SRE/Security | ListLayout | IncidentTimeline |
| `/support-grants` | 支持授权 | Security Auditor | ListLayout | SupportGrantPanel |
| `/backups` | 备份与恢复 | SRE | DetailLayout | BackupStatusCard |
| `/audit` | 运营审计 | Security Auditor | ListLayout | OpsAuditTable |
| `/settings` | 运营设置 | Operator Admin | SettingsLayout | 角色、策略和值班配置 |

Ops Viewer 满足 `OperatorReader`，可读取全部 `/ops` GET 和 `/ops/auth/me`；所有 `/ops` 状态变更端点另要求 `OperatorWriter`，因此 Viewer 的写入由服务端返回 403。前端禁用或隐藏写入口只改善体验，不替代服务端授权。

两个登录页的 demo 入口都要求服务端显式 opt-in：Tenant 使用默认 `false` 的 `Identity:AllowDemoLogin`，Ops 使用默认 `false` 的 `Operations:AllowDemoLogin`，且必须同时处于 `Development`。CI e2e 只对临时 Development 进程启用，生产配置不启用。

## 6. 页面布局类型

| 布局 | 适用页面 | 结构 |
|---|---|---|
| `AuthLayout` | 登录、OIDC、找回 | 桌面品牌/营销侧栏 + 移动品牌 + 语言切换 + 表单 + 页脚 |
| `WorkspaceShell` | 大多数业务页面 | 桌面深色侧栏/移动抽屉 + 顶栏 + ContentContainer |
| `ListLayout` | 项目、员工、行动中心 | PageHeader + FilterBar + DataTable |
| `DetailLayout` | 项目、员工、审批 | PageHeader + 状态条 + DetailList/证据区 + 操作区 |
| `SplitLayout` | 消息、支持工单 | 响应式列表 → 详情 |
| `SettingsLayout` | 租户和个人设置 | 分区卡片 + 紧凑表单/表格 |
| `GalleryLayout` | 组件页 | 分类 + 搜索 + 摘要目录 + 选中 Demo |
| `WorkspaceLayout` | 组织协作可视化 | 视图切换 + 图/办公室/时间线 + 追溯面板 |

## 7. 路由与状态规则

- URL 保存可分享的筛选、分页、标签页和选中对象。
- 下钻保留来源筛选和返回位置。
- 页面刷新后恢复路由和 Query 状态。
- 无权限页面显示 PermissionDenied，不泄露资源细节；Ops API 403 清除能力相关写入口并显示只读指导。
- 会话过期返回登录并保存安全的目标路径。
- 两个登录页使用已保存展示偏好 → 浏览器语言 → `zh-CN` 的 locale 解析；切换语言持久化到 `regent.ui.locale` 并更新文档 lang，不改变认证或租户策略。
- 登录页只显示本地化的状态与安全错误；Tenant 已登录状态提供 Continue/Logout，Ops 已登录状态继续按校验后的返回目标重定向。
- 租户切换后清空旧租户 Query 缓存和 SignalR 订阅。
- SignalR 重连后重新拉取当前页面快照。
- 移动端优先支持首屏、告警确认和审批。

## 8. 导航与权限

- `/auth/me` 返回 `capabilities` 和 `effective_access`，Tenant Dashboard 客户端从这些值派生可见导航，并使用 `effective_access.can_read_chat`、`effective_access.can_read_collaboration` 与 `effective_access.collaboration_scope`（`none|participant|observer`）。
- `/messages` 与 `/organization/collaboration` 必须同时过滤导航并守卫直接路由；缺失或损坏 `effective_access` 时按 `none` 拒绝，显示本地化 PermissionDenied。
- `OwnerObserver` 只读展示 Company/Project/Topic 共享会话、图、trace 与消息，不显示私聊，也不显示发送、创建或成员管理指导；`EmployeeMember` 继续使用授权列表并保持成员范围。
- 页面查询和跨链接按有效访问开关禁用；消息页只能从当前授权会话列表派生选中项，未知 URL 会话 ID 不得触发消息请求。租户切换或重新登录时清除访问相关 Query 缓存并刷新 `/auth/me`。
- 前端只做展示过滤，不替代 API 鉴权。
- 租户导航最多两级，当前分组为 Overview、Delivery、Organization & People、Communication、Models & Knowledge、Governance & Compliance、Settings；消息分组仅在 `canReadChat` 时出现。
- Ops Console 使用独立导航树，当前分组为 Operate 与 Assure。
- 个人绩效、私人记忆和秘密不进入通用导航。

## 9. 页面验收

1. Tenant Dashboard 与 Ops Console 路由完全分离。
2. 所有关键页面有明确 Capability。
3. 所有列表页使用统一筛选和分页。
4. 所有详情页支持证据和审计下钻。
5. 员工详情包含工作时间线。
6. 项目详情包含需求、任务和发布门禁。
7. 行动中心和审批入口清晰。
8. 组件页只在开发环境启用。
9. 路由刷新、深链接和租户切换行为可测试。
