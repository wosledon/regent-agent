# Regent-Agent 详细设计索引与缺口清单

本文目录用于把技术总览中的未闭环项转化为可实施设计。产品范围以 [PRD](../Regent-Agent-PRD.md) 为准，架构总览见 [技术设计](../Regent-Agent-技术设计.md)。

## 1. 详细设计文档

| 文档 | 覆盖内容 |
|---|---|
| [01 基础契约与状态模型](./01-contracts-and-state.md) | 租户边界、身份与 JWT、API 约定、核心实体、状态机、事件与 Outbox |
| [02 运行时可靠性与持久化](./02-runtime-reliability.md) | 任务租约、消息投递、幂等、故障恢复、工作区与成果持久化 |
| [03 模型网关、计量与预算](./03-model-metering-budget.md) | BYOK 配置、模型路由、用量权威源、账本对账、预算预留与硬限制 |
| [04 安全、治理与恢复](./04-security-governance-recovery.md) | 威胁模型、工具与容器安全、升级审批、审计、通知、备份恢复 |
| [05 产品交付与组织启动](./05-product-delivery-loop.md) | 主控启动、项目阶段门禁、Git/CI、体验验收、发布运行、终止复盘 |
| [06 主控与动态组织](./06-controller-and-organization.md) | 主控运行时、自建岗位、能力授权、组织调整、继任与治理 |
| [07 运营平台](./07-operations-platform.md) | 提供方管理端、租户账户、资源池、支持访问、事件、计费、备份与运营审计 |
| [08 工作流系统](./08-workflow-system.md) | 工作流捕获、动态组织范围、版本、提案审查、执行、度量与改进 |
| [09 记忆系统](./09-memory-system.md) | 分层记忆、知识库、混合 RAG、图谱多跳、遗忘、删除与评估 |
| [10 绩效与自我改进](./10-performance-and-self-improvement.md) | 员工自驱动、KPI、绩效评估、组织动作、反作弊、申诉与审计 |
| [11 项目管理中心](./11-project-management.md) | 项目、需求、任务、缺陷、自动认领、讨论、对抗评审、决策与复盘 |
| [12 通信与穿透路由](./12-communication-and-routing.md) | 岗位标签、管理链/专业链、向上穿透、项目覆盖、路由与审计 |
| [13 Agent 聊天与协作](./13-agent-chat-and-collaboration.md) | 私人/群组聊天、@、委派交接、打断、加权召回与渐进披露 |
| [14 模型评测与回归](./14-model-evaluation-and-regression.md) | 评测集、密封数据、EvaluationRun、复用、回归与质量门槛 |
| [15 数据 Schema 与事务边界](./15-data-schema-and-transactions.md) | Postgres Schema、RLS、RustFS、本地工作区、事务、补偿与状态包装 |
| [16 部署、升级与迁移](./16-deployment-upgrade-migration.md) | Compose/systemd、ReleaseManifest、Expand/Contract、混版本、回滚与演练 |
| [17 可观测性与测试基线](./17-observability-and-test-baseline.md) | OpenTelemetry、off/local/otlp/elastic Profile、可选 Elastic Stack、SLO、测试与容量/混沌门禁 |
| [18 注入防御与任务授权](./18-prompt-injection-defense.md) | 不可信内容、TaskGrant、taint、检测、审批、审计与自主性 |
| [19 记忆投毒与纠错](./19-memory-poisoning-and-correction.md) | MemoryClaim、Finding、CorrectionTask、影响图、传播与申诉 |
| [20 工具与插件供应链](./20-tool-and-plugin-supply-chain.md) | SupplyChainArtifact、签名、SBOM、准入、版本锁定、撤销与影响 |
| [21 PII 与秘密脱敏](./21-pii-and-secret-redaction.md) | SensitiveDataPolicy、DLP、秘密隔离、令牌化、审计与删除传播 |
| [22 审计完整性](./22-audit-integrity.md) | AuditEvent 链、Merkle 检查点、签名、WORM、外部锚定与验证 |
| [23 离职、调岗与再入职](./23-employee-offboarding-and-identity.md) | employee_no、EmploymentPeriod、PendingRemoval、Boss 决策、交接与私有数据 |
| [24 内容安全](./24-content-safety.md) | ContentSafetyPolicy、检测、分级处置、复核、申诉、证据与合规 |
| [25 安全事件响应](./25-security-incident-response.md) | SecurityIncident、ContainmentAction、EvidenceItem、通知、恢复与复盘 |
| [26 账户身份与找回](./26-account-identity-and-recovery.md) | 账号密码、GitHub OAuth、EmailClaim、已验证邮箱合并、恢复码与会话撤销 |
| [27 企业 OIDC 与认证策略](./27-enterprise-oidc-and-authentication-policy.md) | OidcProviderConfig、SsoInvitation、issuer+sub、组映射、IdP MFA 与紧急恢复 |
| [28 账户、租户与所有权](./28-account-tenancy-and-ownership.md) | TenantMembership、TenantOwnership、OwnerInvitation、OwnerDelegation 和租户切换 |
| [29 账户与租户软删除](./29-account-and-tenant-soft-deletion.md) | DeletionRequest、软删除、恢复、物理清除、DeletionProof 和 tombstone |
| [30 在线保留与归档](./30-retention-and-archiving.md) | RetentionPolicy、ArchiveManifest、ArchiveObject、ArchiveRestoreJob 和 LegalHold |
| [31 可选备份与恢复](./31-backup-profiles-and-recovery.md) | BackupProfile、BackupJob、RestoreJob、开关依赖和有效 RPO/RTO |
| [32 产出权利与来源](./32-tenant-output-rights-and-provenance.md) | RightsDeclaration、ProvenanceRecord、ThirdPartyNotice、HumanAuthorshipRecord |
| [33 公平招聘与申诉](./33-recruitment-fairness-and-appeals.md) | FairnessPolicy、CandidateAssessment、FairnessAudit、BiasFinding、CandidateAppeal |
| [34 开源许可与分发](./34-open-source-licensing-and-distribution.md) | LicenseInventory、ReleaseLicenseReport、ContributionAttestation |
| [35 租户仪表盘信息架构](./35-tenant-dashboard-information-architecture.md) | DashboardSnapshot、ProjectHealth、ActionCenterItem |
| [36 审批委托与缺席](./36-approval-delegation-and-absence.md) | NegotiationRecord、ApprovalTierPolicy、ApprovalDelegation、ApprovalRoute |
| [37 国际化与本地化](./37-internationalization-and-localization.md) | LocalePolicy、TranslationResource、TranslationReview |
| [38 公司作息与调度](./38-company-work-schedule-and-dispatch.md) | WorkSchedule、CostWindow、HolidayCalendar、WakePolicy、ScheduleOverride |
| [39 GitHub CI/CD 与本地部署](./39-github-actions-and-local-compose.md) | CiRun、LocalBuild、LocalDeployment |
| [40 成本归属、分摊与异常](./40-cost-allocation-and-anomaly.md) | CostAllocationRule、BudgetEnvelope、BudgetCarryover、CostAnomaly |
| [41 内部通知中心](./41-internal-notification-center.md) | Notification、NotificationDelivery、NotificationPreference、NotificationSuppression |
| [42 内部 API 与扩展边界](./42-internal-api-and-extension-boundary.md) | InternalApiClient、ToolManifest、McpToolSurface、SupplyChainArtifact |
| [43 自托管安装与离线升级](./43-self-hosted-install-and-offline-upgrade.md) | InstallRun、OfflineCacheManifest、UpgradeRun |
| [44 容量与性能基线](./44-capacity-and-performance-baseline.md) | CapacityPolicy、QueueLimit、CapacityReport |
| [45 企业合规与支持边界](./45-enterprise-compliance-and-support-boundary.md) | ComplianceProfile、ComplianceControl、ComplianceEvaluation、ComplianceViolation |
| [46 模型评测 QA 实施细则](./46-evaluation-qa-implementation.md) | JudgeAssignment、JudgeCalibration、ContaminationFinding、StatisticalGate |

## 2. 设计缺口登记

| 编号 | 缺口 | 闭环文档 | 状态 | 完成标准 |
|---|---|---|---|---|
| G-01 | “设计封闭”与未决项冲突 | 全部 | 已细化 | 每个未决项有默认方案、影响范围或明确评审门槛 |
| G-02 | 控制面调用网关的矛盾 | 01、03 | 已细化 | 管家与管理调用具有独立服务身份、端点和计量链路 |
| G-03 | 共享 Runner 的多租户身份 | 01 | 已细化 | Runner 凭据与租户任务令牌分离，网关可验证租户和权限 |
| G-04 | BYOK 元数据与密钥同步 | 03 | 已细化 | 创建、轮换、删除具有 saga、状态机和补偿机制 |
| G-05 | Runner 无状态与本地缓冲冲突 | 02 | 已细化 | 任务、成果和工作区不依赖 Runner 进程内存或本地磁盘 |
| G-06 | 孤儿重调度缺少租约与 fencing | 02 | 已细化 | 任务具备租约、确认、重试、幂等和副作用保护 |
| G-07 | 消息表缺少租户字段和投递语义 | 01、02 | 已细化 | 租户级 seq、Outbox/Inbox、游标和毒消息处理完整 |
| G-08 | Runner 与网关都上报成本 | 03 | 已细化 | 模型成本以网关为权威源，工具成本以 Runner 为权威源，可对账 |
| G-09 | 硬预算缺少调用前约束 | 03 | 已细化 | 预算预留、并发扣减、实际结算和超额拒绝可测试 |
| G-10 | 升级队列缺少审批契约 | 04 | 已细化 | 审批绑定动作参数、有效期、权限、超时和审计 |
| G-11 | 安全原则缺少威胁模型 | 04 | 已细化 | 信任边界、资产、攻击面和强制执行点明确 |
| G-12 | 备份涉及网关密钥但责任不清 | 04 | 已细化 | Postgres keyvault schema 密文备份、主密钥分离、RPO/RTO 和恢复演练明确 |
| G-13 | 启动岗位与招聘依赖冲突 | 05、06 | 已细化 | 主控先自建岗位，再按能力缺口规划组织，不依赖固定 HRBP/CTO |
| G-14 | 产品开发后缺少发布运营闭环 | 05 | 已细化 | 项目从调研到发布、观测、迭代和终止均有阶段门禁 |
| G-15 | 共享 repo 缺少并发协作协议 | 02、05 | 已细化 | 工作区、分支/worktree、评审、合并和成果提交有明确规则 |
| G-16 | 验收只覆盖happy path | 全部 | 已细化 | 每份设计包含故障、安全、跨租户和预算异常验收场景 |
| G-17 | 管家会话与定时任务缺少隔离和调度契约 | 01 | 已细化 | 会话租户/用户隔离、cron 时区、租约、幂等和重试明确 |
| G-18 | 招聘面试缺少状态、评分和占槽并发规则 | 05 | 已细化 | 面试流程、评分证据、录用/拒绝和槽位释放可追踪 |
| G-19 | 公司记忆缺少来源、失效和访问控制 | 05 | 已细化 | 记忆可溯源、可策展、可失效，检索带租户和项目过滤 |
| G-20 | 数据保留、删除和导出流程不完整 | 04 | 已细化 | 保留类别、删除宽限期、备份清除和导出校验明确 |
| G-21 | 可观测性和服务目标不可验收 | 04/17 | 已细化 | OpenTelemetry + 可插拔 Profile、可选 Elastic Stack、SLO、错误预算、告警和测试门禁明确 |
| G-22 | 模型评估、本地并发和 Provider 错误语义不足 | 03/14 | 已细化 | benchmark 版本、密封防泄漏、复用、回归、单并发排队、流式中断和熔断明确 |
| G-23 | 固定角色谱系与主控自建岗位冲突 | 06 | 已细化 | 主控不是预设 CEO 岗位；角色、能力和汇报关系动态规划 |
| G-24 | 提供方管理端与租户控制面边界缺失 | 07 | 已细化 | 运营平台具有独立身份、数据、API、支持授权和审计 |
| G-25 | 重复工作无法沉淀为可审查的组织流程 | 08 | 已细化 | 工作流可捕获、版本化、沿组织树向上提案、审查采纳并度量改进 |
| G-26 | 记忆缺少分层召回、遗忘和删除传播 | 09 | 已细化 | 个人/组织/知识库分层，支持混合 RAG、多跳、衰减、归档和彻底删除 |
| G-27 | 员工缺少自驱动、KPI 和绩效动作闭环 | 10 | 已细化 | 所有员工可提出改进，KPI 基于证据，动作受复核、申诉和审计约束 |
| G-28 | 内部缺少项目、需求、任务、缺陷和讨论决策闭环 | 11 | 已细化 | 工作项具备状态门禁、自动认领、讨论、对抗评审、决策与审计 |
| G-29 | 管理链与专业问题升级链混用，技术标签中断后无法路由 | 12 | 已细化 | 管理链与专业链分离，支持向上穿透、项目覆盖、版本失效和审计 |
| G-30 | 员工缺少私人/群组聊天、结构化协作和聊天召回机制 | 13 | 已细化 | 支持 @、委派交接、打断、加权召回、渐进披露、隐私和审计 |
| G-31 | 提示词和工具输出注入缺少来源、taint、TaskGrant 和自主性边界 | 18 | 已细化 | 不可信内容不提升权限，Grant 内自主，越界审批，检测与审计可验收 |
| G-32 | 错误或恶意记忆缺少 claim 级隔离、纠错和引用影响处置 | 19 | 已细化 | 版本化纠错、影响图、传播、申诉和受影响任务处置可验收 |
| G-33 | 工具、MCP、Skill 和镜像缺少签名、SBOM、准入和撤销机制 | 20 | 已细化 | 不可变 digest、准入审核、版本锁定、影响分析和撤销可验收 |
| G-34 | PII 和秘密缺少统一分类、DLP、令牌化和删除传播 | 21 | 已细化 | 秘密隔离、PII 最小化、审计元数据、例外和删除证明可验收 |
| G-35 | 审计不可变但缺少数据库篡改检测、WORM 和外部锚定 | 22 | 已细化 | 哈希链、签名 Merkle 检查点、对象保留和独立验证可验收 |
| G-36 | 员工离职缺少永久工号、再入职、待驱逐、Boss 决策和私有数据处置 | 23 | 已细化 | 身份连续、权限矩阵、交接、隔离、删除证明和审计可验收 |
| G-37 | 内容安全缺少统一检测、分级处置、误报申诉和合规证据 | 24 | 已细化 | 正常内容放行、模糊复核、红线阻止、申诉和取证可验收 |
| G-38 | 安全事件缺少分级、IC、预授权遏制、取证、恢复和复盘闭环 | 25 | 已细化 | SEV1～SEV4、自动遏制、证据链、通知、恢复和行动跟踪可验收 |
| G-39 | 无邮件环境下缺少安全的密码登录、外部登录和账户自助找回基线 | 26 | 已细化 | 账号密码、GitHub OAuth、已验证邮箱合并、恢复码和会话撤销可验收 |
| G-40 | 企业 SSO 缺少租户 OIDC、身份映射、邀请、能力映射和紧急恢复基线 | 27 | 已细化 | OIDC、issuer+sub、IdP MFA、required 模式和紧急恢复可验收 |
| G-41 | 账户与租户耦合，缺少多所有者、代理委托和多租户切换基线 | 28 | 已细化 | 多租户成员、唯一 Primary Owner、限时代理和切换隔离可验收 |
| G-42 | 账户与租户删除缺少软删除、恢复、物理清除和防复活闭环 | 29 | 已细化 | 逻辑隔离、30/7 天恢复、备份 tombstone 和删除证明可验收 |
| G-43 | 各类数据缺少统一在线期、归档格式、冷检索和到期清除闭环 | 30 | 已细化 | 保留类、三层存储、manifest 校验、授权恢复和清除可验收 |
| G-44 | 备份能力与 SLA 强耦合，缺少开关、有效覆盖和风险接受 | 31 | 已细化 | 默认关闭、组件开关、有效 RPO/RTO 和可选演练可验收 |
| G-45 | AI 产出缺少租户归属、第三方权利、来源和发布许可边界 | 32 | 已细化 | 租户归属、有限平台许可、来源记录和未知权利发布阻断可验收 |
| G-46 | 绩效动作未明确 AI 自主、人类解雇批准和事后审查边界 | 10 | 已细化 | 非解雇动作无需人类批准，解雇必须人类批准，事后审查可验收 |
| G-47 | 招聘缺少偏差审计、合理便利和候选人申诉 | 33 | 已细化 | 岗位相关、盲评、分群调查、复测和能力门槛保持可验收 |
| G-48 | 平台开源缺少统一许可证、NOTICE、贡献者和商业边界 | 34 | 已细化 | Apache-2.0、SBOM、DCO、第三方通知和服务型商业可验收 |
| G-49 | 租户仪表盘缺少 Boss 首屏、确定性健康和行动中心 | 35 | 已细化 | 首屏、两级导航、项目健康、下钻和新鲜度可验收 |
| G-50 | 审批缺少缺席协商、分级、委托和 SLA | 36 | 已细化 | 协商不越权、委托限时、超时拒绝和审计可验收 |
| G-51 | UI 和 Agent 输出缺少语言、时区、格式和翻译治理 | 37 | 已细化 | zh-CN/en-US、locale、UTC/IANA 和翻译复核可验收 |
| G-52 | 完整无障碍缺少 WCAG 目标、辅助技术测试和生成内容门禁 | 37（后置） | 已延后 | 不阻塞 MVP，后续按 WCAG 2.2 AA 实施 |
| G-53 | 公司缺少全天可配置窗口、低价调度、假期和紧急唤醒策略 | 38 | 已细化 | 用户窗口、成本感知调度、检查点延期和 KPI 可验收 |
| G-54 | CI/CD 缺少最简 GitHub 版本、检查和本地 Compose 构建契约 | 39 | 已细化 | 版本 Tag、Actions 门禁、本地构建记录和回滚可验收 |
| G-55 | 成本缺少明确归属、公共成本分摊、项目预算结转和确定性异常契约 | 40 | 已细化 | 不重复计费、显式分摊、异常可复现且不自动处罚 |
| G-56 | 通知中心缺少内部持久化、分级路由、去重和实时增强契约 | 41 | 已细化 | 仅站内通知，SignalR 失败可重拉，外部渠道后置 |
| G-57 | 内部 API、MCP 与公共生态之间缺少明确开放边界 | 42 | 已细化 | 仅内部 API/Tool/MCP，公共 API、SDK、Webhook 和市场后置 |
| G-58 | 自托管缺少最简安装、离线缓存、更新和失败恢复契约 | 43 | 已细化 | Tag 源码 + Compose，无激活/自动更新，离线缺失快速失败 |
| G-59 | 容量基线缺少 API 延迟、队列上限、背压和 noisy neighbor 量化契约 | 44 | 已细化 | 单机聚合容量可测试，队列不静默丢弃，租户公平有门槛 |
| G-60 | 企业合规缺少可配置策略、fail-closed、证据和商业支持边界 | 45 | 已细化 | ComplianceProfile 可执行，不宣称认证/SLA，必需项失败即拒绝 |
| G-61 | 模型评测缺少密封集轮换、污染检测、Judge 校准、统计门槛和失败处置细则 | 46 | 已细化 | Judge 员工独立运行，校准/样本/统计可复现，失败不静默通过 |

## 3. 全局不变量

详细设计不得突破以下不变量：

1. 任何模型密钥都不进入员工、管家上下文或控制面业务数据库。
2. 所有租户业务查询必须携带 `tenant_id`；系统级实体使用显式 scope，不能复用租户表绕过隔离。
3. Runner 进程崩溃不得丢失已确认的任务状态、成果或账本事件。
4. 消息和任务至少投递一次，所有业务副作用必须幂等。
5. 模型费用以网关用量事件为权威源，工具费用以 Runner 用量事件为权威源。
6. 硬预算必须在调用前生效；仅靠调用后记账不构成预算控制。
7. 对外动作、超预算和红线动作默认进入人工升级，超时默认拒绝。
8. 不可信内容永远不能获得高于其来源的工具权限。
9. 产品发布必须经过自动化测试和独立体验门禁，并支持回滚。
10. 主技术总览与详细设计冲突时，以详细设计为准，并同步修订总览。
11. 公司岗位不预设；主控持续存在但自建岗位，权限按能力授予，不按岗位名称判断。
12. 租户控制面与运营平台分离身份、权限、界面和审计；运营访问默认不包含业务内容和密钥。
13. Boss 是人类所有者，展示后缀不参与权限；员工 Agent 是独立运行时，不是主控内部函数。
14. Sentinel 是纯程序算法，不调用 LLM、不占编制、不做开放式经营决策。
15. 岗位是组织节点，任职关系连接员工与岗位；组织树只有一个根、无环、层级不超过 4，管理授权按子树和期限委托。
16. LLM 操作统一通过 Tool；Tool 发现层过滤和执行层鉴权同时生效，未授权 Tool 不暴露 Schema。
17. 工作流是版本化制度资产；组织范围绑定岗位子树，只能沿组织树向上提案，采纳版本不可变且不提升执行者权限。
18. 记忆先按租户和范围过滤再召回；遗忘由确定性策略执行，彻底删除必须传播到所有索引、关系、缓存和派生内容。
19. 所有员工均可自驱动提出改进；KPI 基于事实证据，高影响绩效动作必须复核、可申诉且可审计。
20. Tool 权限上限与当前画像分离；Primary 完整披露，Auxiliary 渐进披露，激活不得突破权限上限。
21. 项目、需求、任务和缺陷构成确定性交付闭环；需求须有验收标准，认领受能力与 WIP 约束，高风险评审与作者分离。
22. 通信管理链与专业链分离；专业路由可向上穿透无目标标签的管理岗位，但不得跨租户、跨未授权分支或扩大操作权限。
23. 聊天是弱证据和协作入口；@ 不派工，委派交接必须显式确认，召回须权限过滤并渐进披露。
24. 评测数据和结果版本化；密封集不得进入生产上下文，复用必须完全匹配，评测通过不自动绑定员工。
25. 生产状态以 Postgres 为权威源并启用强制 RLS；二进制统一 RustFS；活动工作区使用本地隔离目录；状态代码使用引用类型包装。
26. 发布使用固定 digest 的版本集；数据库采用 Expand/Deploy/Contract；应用回滚不得突破数据兼容边界。
27. 遥测统一 OpenTelemetry，后端支持 off/local/otlp/elastic Profile；敏感内容不得进入遥测，测试和安全门禁不得随后端关闭而降低。
28. 不可信内容不得提升权限，taint 跨派生内容保留；TaskGrant 内不逐步审批，只有越界动作才暂停升级。
29. 记忆纠错以 claim 为单位并生成新版本；引用影响、索引、派生内容和通知传播必须可追踪且幂等。
30. 工具、MCP、Skill、依赖和镜像按不可变 digest 管理；签名、SBOM 和准入缺一不可，撤销必须按版本传播。
31. 秘密只存在于密钥系统；PII 默认最小化，DLP、审计元数据和删除传播不得被员工自行关闭。
32. 审计必须追加写并维护可验证的哈希链和签名检查点；WORM 与外部锚定按 Profile 启用，验证失败不得静默修复。
33. 员工工号永久唯一；主控只能否决候选，录用、再入职和移除订阅由 Boss 最终决定；离职保留公司成果并隔离私有数据。
34. 内容安全必须版本化并采用分级处置；分类器不是唯一判断，误报可申诉，对外发布不得绕过门禁。
35. 安全事件必须按等级响应并由 IC 负责；预授权自动遏制不得等待逐级审批，证据与恢复必须可审计。
36. 外部身份按不可变 provider subject 绑定；仅已验证邮箱可唯一匹配自动合并，恢复码单次使用，找回成功必须撤销旧会话。
37. OIDC 身份键为 issuer+sub，新用户必须先邀请或预置；required SSO 必须具有独立紧急恢复，IdP 故障不得回退本地密码。
38. 账户可拥有多个租户但第一梯队一租户一公司；每租户唯一 Primary Owner，最后所有者不可移除，代理不得转授权。
39. 账户和租户删除必须立即软删除并限时恢复；窗口结束后物理清除，备份恢复必须重放 tombstone，软删除不是永久保留。
40. 数据按版本化保留类进入 Hot/Archive/Cold；归档必须校验水位、数量和哈希，普通 Agent 不得检索 Cold，删除优先于归档。
41. 备份必须开关化且默认关闭；未启用组件不得承诺 RPO/RTO，启用后按实际证据计算有效目标，升级跳过必须记录风险接受。
42. 租户任务产出归租户所有，平台仅持有限运营许可；第三方权利不得被覆盖，UnknownRights 不得对外发布，租户内容默认不得用于平台训练。
43. 非解雇绩效动作由 AI 自主决定并经独立 AI 复核；只有解雇需人类 Boss 最终批准，人类只参与解雇和事后审查。
44. 招聘只按岗位相关能力评分，必须盲评、等价案例和分群偏差审计；偏差触发调查，候选人可申诉，能力门槛不得降低。
45. 第一方资产统一 Apache-2.0；核心和默认企业功能保持开源，商业收入来自服务，第三方权利、租户产出和商标保持独立。
46. Boss 仪表盘只突出健康、风险、预算、组织和待决策；项目健康由确定性规则计算，行动中心必须可下钻并显示新鲜度。
47. 人类缺席时优先由 Controller 与直接下一层协商；协商不得创造权限或突破红线，不能处理才进入委托或备用审批，超时仍拒绝。
48. 第一梯队支持 zh-CN/en-US；UTC 存储、IANA 时区和 locale 展示、币种显式，翻译保留来源与复核状态；完整无障碍后置。
49. 公司全天 24 小时可配置；普通任务只在用户禁用窗口排队，可延期任务利用低价窗口，紧急唤醒不得扩大权限，禁用窗口不计绩效扣分。
50. 源码和 CI 只使用 GitHub/GitHub Actions；仓库只打版本 Tag，Actions 只检查，本地从 Tag 用 Compose 构建，不发布镜像或引入多环境/Kubernetes。
51. 成本只在钱账记录一次；任务项目优先、公共成本默认不分摊、预算不自动结转，确定性异常不得自动改预算、换模型或惩罚员工。
52. 模型运行只支持 OpenAI 与 Anthropic 云端原生端点，不支持本地 GPU、Ollama、本地模型或 GPU 调度。
53. 第一梯队只实现仪表盘内部通知中心；通知必须持久化、去重并支持 SignalR 实时增强和失败重拉，外部渠道后置。
54. `/v1` 仅用于系统内部调用；不提供公共 API、公共 SDK、出站 Webhook、MCP/插件市场或跨租户分享。
55. 自托管只使用版本 Tag 源码、Compose 和仓库脚本；不提供激活或自动更新，离线缓存缺失必须快速失败，完全断网不承诺模型推理。
56. 当前只承诺单租户单机聚合容量；队列必须有上限和背压，noisy neighbor 不得使其他租户 API P95 劣化超过 20% 或等待超过 30 秒。
57. 合规通过版本化 ComplianceProfile 配置；必需控制不满足必须 fail closed，技术证据不等于认证，当前不提供商业 SLA 或专属支持。
58. 评测 holdout 与 canary 必须隔离；Judge 是独立员工运行时，模型可同但上下文不可共享，校准、样本、统计和失败处置必须可复现。
59. Gateway 模型密钥密文存 Postgres `keyvault` schema，使用信封加密和独立数据库角色；主密钥不进入数据库。
60. 前端使用 React 19、TypeScript、MUI 和 SignalR TypeScript 客户端，不使用 Blazor 或 C# 前端；两个前端不共享会话和权限。
