# Regent-Agent 文档导航

## 文档结构

| 文档 | 职责 | 主要读者 |
|---|---|---|
| [产品需求文档（PRD）](./Regent-Agent-PRD.md) | 定义为什么做、为谁做、做什么、业务规则和验收标准 | 股东、产品、设计、研发、测试 |
| [技术设计总览](./Regent-Agent-技术设计.md) | 定义架构总览、技术选型、模块边界和落地阶段 | 架构、研发、运维 |
| [后端技术文档](./Regent-Agent-后端技术文档.md) | 说明后端技术栈、术语、16 个 .NET 项目和 3 个 TypeScript 前端模块、依赖规则、数据边界、API、安全和测试 | 后端、前端、架构、QA、SRE |
| [前端技术文档](./Regent-Agent-前端技术文档.md) | 说明 React 19、TypeScript、MUI、应用设计、主题、状态管理、SignalR 和封装组件 | 前端、设计、QA、架构 |
| [架构决策记录 ADR](./adr/README.md) | 记录已接受的后端、前端、数据、模型、部署、可靠性和治理技术决策 | 架构、研发、安全、运维 |
| [工程规范 AGENTS.md](../AGENTS.md) | 文档查阅、后端抽象复用封装、前端组件化、文档更新和小步提交规范 | 全体 Agent 与研发 |
| [完整架构流程图](./Regent-Agent-架构流程图.md) | 用 Mermaid 展示四部分拓扑、工具调用、项目交付、模型预算和绩效闭环 | 全体研发与评审者 |
| [待确认项](./Regent-Agent-待确认项.md) | 按优先级记录尚未确认的产品、架构、安全、运营和商业化决策 | 全体讨论参与者 |
| [详细设计索引与缺口清单](./design/README.md) | 覆盖未闭环项、全局不变量和领域详细设计导航 | 全体研发与评审者 |
| [01 基础契约与状态模型](./design/01-contracts-and-state.md) | 租户、身份、API、实体、状态机和事件 | 后端、安全、测试 |
| [02 运行时可靠性与持久化](./design/02-runtime-reliability.md) | 调度、租约、消息、恢复、工作区和成果 | 后端、Runner、运维 |
| [03 模型网关、计量与预算](./design/03-model-metering-budget.md) | BYOK、路由、用量、账本和预算 | 网关、后端、财务 |
| [04 安全、治理与恢复](./design/04-security-governance-recovery.md) | 威胁模型、工具/容器安全、审批、审计和备份 | 安全、运维、测试 |
| [05 产品交付与组织启动](./design/05-product-delivery-loop.md) | 主控启动、项目门禁、Git/CI、体验、发布和复盘 | 产品、研发、测试、运维 |
| [06 主控与动态组织](./design/06-controller-and-organization.md) | 主控、自建岗位、能力授权、组织调整和继任 | 产品、架构、研发、安全 |
| [07 运营平台](./design/07-operations-platform.md) | 提供方管理端、租户账户、资源池、支持访问和运维 | 运营、SRE、安全、财务 |
| [08 工作流系统](./design/08-workflow-system.md) | 工作流捕获、组织范围、版本、审查、执行与度量 | 产品、研发、流程负责人 |
| [09 记忆系统](./design/09-memory-system.md) | 分层记忆、知识库、RAG、图谱多跳、遗忘与删除 | 研发、知识管理、安全 |
| [10 绩效与自我改进](./design/10-performance-and-self-improvement.md) | 员工自驱动、KPI、AI 自主绩效动作、人类解雇与事后审查 | 产品、研发、组织管理 |
| [11 项目管理中心](./design/11-project-management.md) | 项目、需求、任务、缺陷、自动认领、讨论、对抗评审与决策 | 产品、研发、测试、项目管理 |
| [12 通信与穿透路由](./design/12-communication-and-routing.md) | 岗位标签、管理链/专业链、向上穿透、项目覆盖与通信审计 | 产品、研发、组织管理、安全 |
| [13 Agent 聊天与协作](./design/13-agent-chat-and-collaboration.md) | 私人/群组聊天、@、委派交接、打断、加权召回与渐进披露 | 产品、研发、组织管理、安全 |
| [14 模型评测与回归](./design/14-model-evaluation-and-regression.md) | 版本化评测集、密封防泄漏、公司内复用、回归与质量门槛 | 模型评估、QA、数据治理、安全 |
| [15 数据 Schema 与事务边界](./design/15-data-schema-and-transactions.md) | Postgres、强制 RLS、RustFS、本地工作区、事务与补偿 | 后端、数据、安全、运维 |
| [16 部署、升级与迁移](./design/16-deployment-upgrade-migration.md) | Compose/systemd、版本集、Expand/Contract、混版本与回滚 | 研发、运维、安全 |
| [17 可观测性与测试基线](./design/17-observability-and-test-baseline.md) | OpenTelemetry、可插拔 Profile、可选 Elastic Stack、SLO、告警与测试门禁 | 研发、运维、QA、安全 |
| [18 注入防御与任务授权](./design/18-prompt-injection-defense.md) | 不可信内容、TaskGrant、taint、检测、审批与自主性 | 安全、Agent、Tool、QA |
| [19 记忆投毒与纠错](./design/19-memory-poisoning-and-correction.md) | claim 隔离、CorrectionTask、影响图、传播与申诉 | Memory、安全、QA、项目管理 |
| [20 工具与插件供应链](./design/20-tool-and-plugin-supply-chain.md) | 不可变制品、签名、SBOM、准入、版本锁定与撤销 | 安全、Tool、运维、QA |
| [21 PII 与秘密脱敏](./design/21-pii-and-secret-redaction.md) | 数据分类、秘密隔离、PII 令牌化、DLP、审计与删除 | 安全、数据、Memory、合规 |
| [22 审计完整性](./design/22-audit-integrity.md) | 哈希链、Merkle 检查点、签名、WORM 与外部锚定 | 安全、合规、运维 |
| [23 离职、调岗与再入职](./design/23-employee-offboarding-and-identity.md) | 唯一工号、任职周期、待驱逐、Boss 决策、交接与私有数据 | 组织、绩效、Memory、安全 |
| [24 内容安全](./design/24-content-safety.md) | 内容政策、检测管线、分级处置、申诉、证据与合规 | 安全、产品、Agent、合规 |
| [25 安全事件响应](./design/25-security-incident-response.md) | SEV1～SEV4、IC、遏制、取证、通知、恢复与复盘 | 安全、SRE、运营、合规 |
| [26 账户身份与找回](./design/26-account-identity-and-recovery.md) | 账号密码、GitHub OAuth、已验证邮箱合并、恢复码自助找回与会话撤销 | 账户、UI、安全 |
| [27 企业 OIDC 与认证策略](./design/27-enterprise-oidc-and-authentication-policy.md) | 租户 OIDC、issuer+sub、邀请、组映射、IdP MFA 与紧急恢复 | 身份、企业自托管、安全 |
| [28 账户、租户与所有权](./design/28-account-tenancy-and-ownership.md) | 账户多租户、Primary/Co-Owner、邀请、限时代理和租户切换 | 账户、租户、权限、安全 |
| [29 账户与租户软删除](./design/29-account-and-tenant-soft-deletion.md) | 逻辑删除、加密隔离、限时恢复、物理清除、备份和法律保留 | 账户、数据、Operations、合规 |
| [30 在线保留与归档](./design/30-retention-and-archiving.md) | 保留类、Hot/Archive/Cold、月度归档、manifest、恢复与冷检索 | 数据、Memory、审计、Operations |
| [31 可选备份与恢复](./design/31-backup-profiles-and-recovery.md) | BackupProfile、组件开关、有效 RPO/RTO、异地和演练 | Operations、SRE、安全 |
| [32 产出权利与来源](./design/32-tenant-output-rights-and-provenance.md) | 租户归属、平台有限许可、第三方权利、来源和发布门禁 | 法务、产品、交付、安全 |
| [33 公平招聘与申诉](./design/33-recruitment-fairness-and-appeals.md) | 岗位相关标准、盲评、偏差审计、合理便利和候选人申诉 | 招聘、组织、法务、QA |
| [34 开源许可与分发](./design/34-open-source-licensing-and-distribution.md) | Apache-2.0、NOTICE、SBOM、贡献者、商标和商业服务 | 法务、发布、商业 |
| [35 租户仪表盘信息架构](./design/35-tenant-dashboard-information-architecture.md) | Boss 首屏、两级导航、项目健康、行动中心和新鲜度 | 产品、UX、研发、QA |
| [36 审批委托与缺席](./design/36-approval-delegation-and-absence.md) | 缺席协商、分级审批、委托、备用审批人和 SLA | 治理、组织、安全、UX |
| [37 国际化与本地化](./design/37-internationalization-and-localization.md) | zh-CN/en-US、时区、格式、翻译治理；无障碍后置 | 产品、UX、研发、QA |
| [38 公司作息与调度](./design/38-company-work-schedule-and-dispatch.md) | 全天可配置、低价窗口、成本感知调度和紧急唤醒 | 调度、组织、消息、QA |
| [39 GitHub CI/CD 与本地部署](./design/39-github-actions-and-local-compose.md) | GitHub 版本 Tag、Actions 检查和本地 Compose 构建 | 交付、集成、安全 |
| [40 成本归属、分摊与异常](./design/40-cost-allocation-and-anomaly.md) | 直接归属、公共成本、项目预算、结转、异常和原币记账 | 账本、预算、绩效 |
| [41 内部通知中心](./design/41-internal-notification-center.md) | 站内分级通知、持久化、SignalR、去重和已读状态 | 通知、租户仪表盘 |
| [42 内部 API 与扩展边界](./design/42-internal-api-and-extension-boundary.md) | 内部 API、Tool、内部 MCP 和公共生态边界 | 平台、集成、安全 |
| [43 自托管安装与离线升级](./design/43-self-hosted-install-and-offline-upgrade.md) | Tag 源码、Compose、脚本、离线缓存和升级恢复 | 自托管、运维 |
| [44 容量与性能基线](./design/44-capacity-and-performance-baseline.md) | 单机聚合容量、API P95、队列上限、背压和租户公平 | 容量、调度、SLO |
| [45 企业合规与支持边界](./design/45-enterprise-compliance-and-support-boundary.md) | 可配置合规、fail-closed、技术证据和支持边界 | 合规、Operations、法务 |
| [46 模型评测 QA 实施细则](./design/46-evaluation-qa-implementation.md) | 密封集、污染、Judge 员工、校准、统计和失败处置 | 评测、QA、数据治理 |
| [47 OpenAPI 基线](./design/47-openapi-baseline.md) | 内部 API 认证、幂等、分页、错误、版本和代码生成 | 后端、前端、契约 |
| [48 前端页面与路由地图](./design/48-frontend-route-map.md) | 两个前端的页面、路由、权限、布局和组件映射 | 前端、设计、QA |
| [49 核心 ERD 与数据字典](./design/49-core-data-model-and-erd.md) | 核心实体关系、Schema 所有权、RLS、保留和数据字典 | 后端、数据、架构 |

## 拆分原则

- PRD 只描述产品要求和验收，不写进程拓扑、数据库表、HTTP 端点或框架选型。
- 技术总览描述整体形状；详细设计描述可实施的契约、算法、状态机和失败处理。
- 总览与详细设计冲突时，以详细设计为准，并同步修订总览。
- 新增功能先更新 PRD；实现方案先更新对应详细设计；架构形状变化再更新总览。

## 更新规则

1. 产品目标、用户需求、范围、业务规则或验收标准变化：先更新 PRD。
2. 身份、实体、API、状态机或事件变化：更新 01 号详细设计。
3. 调度、消息、工作区或故障恢复变化：更新 02 号详细设计。
4. 模型、密钥引用、计量或预算变化：更新 03 号详细设计。
5. 安全策略、审批、审计或备份变化：更新 04 号详细设计。
6. 项目流程、CI、体验、发布或复盘变化：更新 05 号详细设计。
7. 主控、岗位、能力、组织调整或继任变化：更新 06 号详细设计。
8. 租户账户、运营权限、资源池、支持访问或运维流程变化：更新 07 号详细设计。
9. 工作流范围、版本、审查、执行或度量变化：更新 08 号详细设计。
10. 记忆范围、召回、知识库、遗忘或删除变化：更新 09 号详细设计。
11. 自驱动、KPI、绩效评估、组织动作或申诉变化：更新 10 号详细设计。
12. 项目、需求、任务、缺陷、讨论、评审、认领或项目度量变化：更新 11 号详细设计。
13. 岗位标签、通信画像、管理链、专业链、穿透路由或项目覆盖变化：更新 12 号详细设计。
14. 聊天会话、@、委派交接、打断、聊天召回或渐进披露变化：更新 13 号详细设计。
15. 未决项必须保留明确状态、默认值和评审门槛。
16. 文档之间使用相对链接，并在发生变化后检查链接与术语一致性。
