# Regent-Agent（摄政）—— 技术设计文档

- 版本：v1.3（四部分架构版）
- 日期：2026-11-22
- 状态：详细设计已补齐，待技术评审，尚未开始实现
- 项目名称：**Regent-Agent**（摄政）
- 定位：描述自主 AI 公司的架构、运行时、接口、数据、安全与交付实现

> 产品目标、范围与验收标准以 [产品需求文档（PRD）](./Regent-Agent-PRD.md) 为准。本文保留架构总览和原始技术决策；可实施的契约、状态机、故障恢复、计量预算、安全治理和交付闭环见 [详细设计索引](./design/README.md)。后端项目、技术栈、术语与依赖规则见 [后端技术文档](./Regent-Agent-后端技术文档.md)；前端技术栈、设计和组件见 [前端技术文档](./Regent-Agent-前端技术文档.md)。总览与详细设计冲突时，以详细设计为准。

---

## 0. 一页总览

| 维度 | 决策 |
|---|---|
| 语言 / 框架 | C# / .NET 10 LTS / Microsoft Agent Framework 1.0（2026-04-03 GA） |
| 进程拓扑 | ControlPlane（租户）+ AI Gateway（模型密钥）+ Runner × N（无状态计算）+ Operations Platform（提供方） |
| 通信 | Runner↔控制面 HTTP RPC；公司内通信走控制面租户消息表；进程内用 Channel；**不引入 Kafka** |
| 沙箱 | 两层：团队工位容器（按 租户×项目×代码库×工具链 分组，组内全员休眠才停）+ 一次性执行容器（CI/危险操作） |
| 密钥 | 模型密钥只存在于网关；控制面签发部署/租户任务令牌，网关验证 |
| 编制 | 初期 ≤ 20 人，组织层级 ≤ 4 层 |
| 预算 | ¥8000 / 月硬底线（预警 ¥5600） |
| 人类接口 | 仪表盘（观察）+ 管家 AI（对话式操作）+ 升级队列（唯一决策入口，超时默认拒绝），其余完全不管 |
| 管理界面 | 租户仪表盘与运营控制台分离；前者服务股东，后者服务提供方；网关对租户 API-only，运营平台通过独立管理 API 运维 |
| 部署形态 | 全链路开源；两种用法：运营方托管服务（注册即用，多租户）或源码全套自托管（单租户自足） |
| 产品形态 | 多租户 SaaS：每个账户可配置一个或多个租户，各自拥有模型池（BYOK）、章程与公司；租户 #1 为股东自用（dogfooding） |

---

## 0.1 详细设计索引

| 主题 | 详细设计 |
|---|---|
| 租户、身份、API、实体与状态机 | [01 基础契约与状态模型](./design/01-contracts-and-state.md) |
| 任务租约、消息、故障恢复、工作区与成果 | [02 运行时可靠性与持久化](./design/02-runtime-reliability.md) |
| BYOK、模型路由、用量、账本与预算 | [03 模型网关、计量与预算](./design/03-model-metering-budget.md) |
| 威胁模型、工具与容器安全、审批、审计、备份 | [04 安全、治理与恢复](./design/04-security-governance-recovery.md) |
| 组织启动、项目门禁、Git/CI、体验、发布与复盘 | [05 产品交付与组织启动](./design/05-product-delivery-loop.md) |
| 主控运行时、自建岗位、能力授权、组织调整与继任 | [06 主控与动态组织](./design/06-controller-and-organization.md) |
| 提供方运营平台、支持访问、资源池、租户账户与运维工作流 | [07 运营平台](./design/07-operations-platform.md) |
| 可复用工作流、组织范围、版本、提案审查、执行与度量 | [08 工作流系统](./design/08-workflow-system.md) |
| 分层记忆、知识库、RAG、图谱多跳、遗忘与删除 | [09 记忆系统](./design/09-memory-system.md) |
| 员工自驱动、KPI、绩效评估、组织动作、申诉与审计 | [10 绩效与自我改进](./design/10-performance-and-self-improvement.md) |
| 内部项目、需求、任务、缺陷、自动认领、讨论与对抗评审 | [11 项目管理中心](./design/11-project-management.md) |
| 岗位标签、管理链/专业链、向上穿透、项目覆盖与通信路由 | [12 通信与穿透路由](./design/12-communication-and-routing.md) |
| 私人/群组聊天、@、委派交接、打断、加权召回与渐进披露 | [13 Agent 聊天与协作](./design/13-agent-chat-and-collaboration.md) |
| 模型评测集、密封防泄漏、公司内复用、回归频率与质量门槛 | [14 模型评测与回归](./design/14-model-evaluation-and-regression.md) |
| Postgres Schema、强制 RLS、RustFS、本地工作区、事务与补偿 | [15 数据 Schema 与事务边界](./design/15-data-schema-and-transactions.md) |
| Compose/systemd、版本集、Expand/Contract、混版本与回滚 | [16 部署、升级与迁移](./design/16-deployment-upgrade-migration.md) |
| OpenTelemetry、可插拔 Profile、可选 Elastic Stack、Kibana APM、SLO、告警与测试基线 | [17 可观测性与测试基线](./design/17-observability-and-test-baseline.md) |
| 提示词/工具输出注入、不可信内容、TaskGrant、taint 与自主性 | [18 注入防御与任务授权](./design/18-prompt-injection-defense.md) |
| 记忆投毒、claim 隔离、CorrectionTask、影响图与纠错传播 | [19 记忆投毒与纠错](./design/19-memory-poisoning-and-correction.md) |
| 工具/MCP/Skill/镜像供应链、签名、SBOM、准入与撤销 | [20 工具与插件供应链](./design/20-tool-and-plugin-supply-chain.md) |
| PII、秘密、DLP、令牌化、审计元数据和删除传播 | [21 PII 与秘密脱敏](./design/21-pii-and-secret-redaction.md) |
| 审计哈希链、Merkle 检查点、RustFS WORM 和外部锚定 | [22 审计完整性](./design/22-audit-integrity.md) |
| 唯一工号、调岗/再入职、待驱逐、Boss 决策和离职数据 | [23 离职、调岗与再入职](./design/23-employee-offboarding-and-identity.md) |
| 内容分级、检测管线、处置、误报申诉和外部合规 | [24 内容安全](./design/24-content-safety.md) |
| SEV1～SEV4、IC、遏制、取证、通知、恢复与复盘 | [25 安全事件响应](./design/25-security-incident-response.md) |
| 账号密码、GitHub OAuth、已验证邮箱合并、恢复码与会话撤销 | [26 账户身份与找回](./design/26-account-identity-and-recovery.md) |
| 租户 OIDC、issuer+sub、邀请预置、组映射、IdP MFA 与紧急恢复 | [27 企业 OIDC 与认证策略](./design/27-enterprise-oidc-and-authentication-policy.md) |
| 账户多租户、Primary/Co-Owner、限时代理和租户切换 | [28 账户、租户与所有权](./design/28-account-tenancy-and-ownership.md) |
| 软删除、加密隔离、限时恢复、物理清除和 tombstone | [29 账户与租户软删除](./design/29-account-and-tenant-soft-deletion.md) |
| 保留类、Hot/Archive/Cold、月度归档、manifest 和授权恢复 | [30 在线保留与归档](./design/30-retention-and-archiving.md) |
| 可选备份开关、有效 RPO/RTO、异地副本和恢复演练 | [31 可选备份与恢复](./design/31-backup-profiles-and-recovery.md) |
| 租户产出归属、第三方权利、来源记录和发布门禁 | [32 产出权利与来源](./design/32-tenant-output-rights-and-provenance.md) |
| 岗位相关招聘、盲评、偏差审计和候选人申诉 | [33 公平招聘与申诉](./design/33-recruitment-fairness-and-appeals.md) |
| Apache-2.0、NOTICE、SBOM、贡献者和商业边界 | [34 开源许可与分发](./design/34-open-source-licensing-and-distribution.md) |
| Boss 首屏、两级导航、项目健康和行动中心 | [35 租户仪表盘信息架构](./design/35-tenant-dashboard-information-architecture.md) |
| 缺席协商、分级审批、委托、备用审批人和 SLA | [36 审批委托与缺席](./design/36-approval-delegation-and-absence.md) |
| zh-CN/en-US、时区、格式和翻译治理 | [37 国际化与本地化](./design/37-internationalization-and-localization.md) |
| 全天可配置、低价窗口和成本感知调度 | [38 公司作息与调度](./design/38-company-work-schedule-and-dispatch.md) |
| GitHub 版本 Tag、Actions 检查和本地 Compose 构建 | [39 GitHub CI/CD 与本地部署](./design/39-github-actions-and-local-compose.md) |
| 成本归属、显式分摊、预算结转和确定性异常 | [40 成本归属、分摊与异常](./design/40-cost-allocation-and-anomaly.md) |
| 内部通知、持久化、SignalR 和去重 | [41 内部通知中心](./design/41-internal-notification-center.md) |
| 内部 API、Tool、MCP 和公共生态边界 | [42 内部 API 与扩展边界](./design/42-internal-api-and-extension-boundary.md) |
| Tag 源码、Compose、离线缓存和升级恢复 | [43 自托管安装与离线升级](./design/43-self-hosted-install-and-offline-upgrade.md) |
| 单机容量、API P95、队列和 noisy neighbor | [44 容量与性能基线](./design/44-capacity-and-performance-baseline.md) |
| 可配置合规、fail-closed 和支持边界 | [45 企业合规与支持边界](./design/45-enterprise-compliance-and-support-boundary.md) |
| 密封集、Judge 员工、校准、统计和失败处置 | [46 模型评测 QA 实施细则](./design/46-evaluation-qa-implementation.md) |
| OpenAPI、认证、幂等、分页、错误和代码生成 | [47 OpenAPI 基线](./design/47-openapi-baseline.md) |
| 前端页面、路由、权限、布局和组件映射 | [48 前端页面与路由地图](./design/48-frontend-route-map.md) |
| 核心 ERD、Schema 所有权、RLS 和数据字典 | [49 核心 ERD 与数据字典](./design/49-core-data-model-and-erd.md) |
| 已接受技术决策与替代方案 | [ADR 架构决策记录](./adr/README.md) |

完整缺口登记和全局不变量见 [详细设计 README](./design/README.md)；四部分拓扑、工具调用、项目交付和模型预算流程见 [完整架构流程图](./Regent-Agent-架构流程图.md)。

---

## 1. 愿景与定位

产品形态是**多租户 SaaS**：每个注册账户可以拥有或加入多个租户，配置各自的模型池（BYOK）、章程和自主公司。第一梯队一租户一公司，租户 #1 是股东自己的公司（dogfooding）。

这不是"一个 AI 助手"，也不是"装几个服务"，而是一台**公司孵化器**：

- 股东（人类）的唯一创作行为：写公司章程。
- 公司的第一个产品，就是一份自己调研出来的产品简报。
- 组织从零开始生长：招人、面试、考核、开除、扩编、裁员，全部自主。
- 交付物必须经得起"模拟真实用户"的检验，而不是开发组自嗨。

人类侧只剩三样东西：**管家**（对话式操作）、**仪表盘**（观察）、**升级队列**（唯一需要拍板的地方，带管家建议）。

### 1.1 命名体系

Regent = 摄政者：**君主（股东）长期缺席，摄政者代行统治**——平台的本质承诺是"你不用在，但它认真在管"。且摄政是制度不是神君：平台是治理机器，公司是被治理的对象。

| 层 | 名称 | 说明 |
|---|---|---|
| 平台 | **Regent-Agent**（摄政） | 产品总名 |
| 人类所有者 | **Boss / Owner**（老板） | 租户所有者成员，不是 Agent；Primary Owner 唯一，权限来自显式所有权或委托 |
| 用户角色别称 | **Sovereign**（君主） | Boss 在产品叙事中的别称 |
| 股东助手 | **Steward**（总管） | §3.1.1 管家：只读、直属股东、定时汇报 |
| 公司种子 | **Charter**（特许状） | §2 章程。Royal Charter 史上正是"设立一个实体的特许文件" |
| 公司主控 | **Controller**（主控） | 公司唯一的编排 Agent 运行时，不是预设岗位；启动后为自己创建岗位 |
| 公司员工 | **Employee Agent** | 独立业务执行 Agent，不是主控的内部函数 |
| 公司内部 | 动态岗位与能力 | 岗位名称和职责由主控规划；CEO、CTO、HRBP 等只是可选称呼 |
| 平台监控 | **Sentinel**（哨兵） | 纯程序算法，不调用 LLM、不占编制、不做经营决策 |

命名空间与工程：后端使用 `RegentAgent.*` 和 `RegentAgent.slnx`；TenantDashboard、OpsConsole 和 UI.Shared 使用独立 TypeScript workspace，不进入 .NET solution。

---

## 2. 公司章程（Charter / 特许状）——每个租户的股东唯一签字的地方

```
使命：自主发现市场空白，并做成可验证的软件产品（使命持续有效，不以单一项目终结）
约束：
  · 编制：初期 ≤ 20 人
  · 预算：月 ¥8000 硬底线
  · 能力边界：只做软件 / Web / API，不碰硬件、重资产、需资质行业
  · 伦理红线：不碰灰产、不侵权、不冒充人类、不生成违法内容
治理：
  · 分级放权：见 §6.5 升级队列
  · 股东通知渠道：仅仪表盘通知中心（灾难、升级和定时报告按分级站内推送；外部渠道后置）
  · 技术约束：C#/.NET 10/MAF；不引入 Kafka；密钥只在网关；Runner 必须无状态
```

---

## 3. 组织模型

### 3.1 主控与动态岗位

平台不预设固定角色。每个公司有一个 **Controller（主控）**，负责读取状态、规划组织、创建任务和推进闭环；主控本身不是 CEO 岗位。

| 概念 | 说明 |
|---|---|
| Boss / Owner | 人类租户所有者成员，不是 Agent，不占编制；Primary Owner 唯一，展示后缀不影响权限 |
| Controller | 公司唯一的编排 Agent 运行时，不占编制；启动后为自己创建一个数据化岗位 |
| Employee Agent | 绑定岗位的独立 Agent 运行时；主控自建岗位后也以 EmployeeRuntime 执行岗位任务 |
| Position | 组织树节点与编制槽，包含名称、职责、能力、模型档位、预算和汇报关系 |
| PositionAssignment | 员工与岗位的任职关系；一个员工一个主岗位，一个岗位一个主任职者 |
| OrganizationTree | 由 `reports_to_position_id` 生成的岗位树；只有一个根、无环、层级不超过 4 |
| OrgManagementGrant | 主控按子树、动作、预算和期限委托组织管理权，可撤销、可审计 |
| Tool | LLM 可发现的受控操作；权限上限与当前画像分离，执行时再次鉴权 |
| Tool Profile | 当前任务默认披露的 Primary/Auxiliary/Restricted 工具集合 |
| Project / Requirement / Task / Defect | 项目、需求、任务和缺陷构成内部交付闭环 |
| TaskTypeDefinition / EmployeeWorkLedger | 任务语义、类型模板和按类型加权的员工工作台账 |
| Discussion / Decision | 工作项线程、对抗评审、限时裁决和可审计决策 |
| Position Label / Communication Profile | 岗位标签与通信画像；管理链和专业链分离，专业路由可向上穿透 |
| Agent Chat / Collaboration | 私人/群组聊天、@、委派交接、打断、加权召回和渐进披露 |
| Employee Operational State | Active/Busy/Sleeping/RateLimited/Degraded 等确定性状态与可靠性 KPI |
| Model Catalog / Price Snapshot | models.dev 每日同步、BYOK 补价、自动覆盖、自动档位和不可变价格快照 |
| Benchmark Suite / EvaluationRun | 版本化评测集、密封防泄漏、公司内复用、回归与质量门槛 |
| Data & Object Storage | Postgres + 强制 RLS、RustFS 对象存储、Runner 本地隔离工作区 |
| Release Manifest | 版本 Tag、基础镜像 digest、本地镜像 ID、schema、API、事件和兼容版本；Expand/Deploy/Contract |
| Telemetry Backend | OpenTelemetry + off/local/otlp/elastic Profile；Elastic Stack 可选 |
| Untrusted Content / Task Grant | 内容来源与 taint、任务预授权、执行层独立鉴权和自主性边界 |
| Memory Claim / Correction | claim 级隔离、CorrectionTask、版本化纠错、引用影响与传播 |
| Supply Chain Artifact | Tool/MCP/Skill/依赖/镜像的 digest、签名、SBOM、准入和撤销 |
| Sensitive Data Policy / DLP | 数据分类、秘密隔离、PII 令牌化、检测、审计与删除传播 |
| Audit Integrity Profile | 哈希链、签名 Merkle 检查点、RustFS WORM 和外部锚定 |
| Employee No / Pending Removal | 永久工号、任职周期、待驱逐队列、Boss 决策与离职交接 |
| Content Safety Policy | 版本化规则、可插拔检测、分级处置、申诉与合规 |
| Security Incident Response | SEV1～SEV4、IC、遏制、取证、通知、恢复与复盘 |
| Account Identity | 账号密码、GitHub OAuth、已验证邮箱合并、恢复码与会话撤销 |
| Enterprise OIDC | 租户 issuer+sub、邀请预置、组映射、IdP MFA 与紧急恢复 |
| Tenancy & Ownership | 账户多租户、Primary/Co-Owner、限时代理与租户切换 |
| Soft Deletion | 逻辑删除、加密隔离、限时恢复、物理清除与 tombstone |
| Retention & Archive | 保留类、Hot/Archive/Cold、manifest、授权恢复与冷检索 |
| Backup Profile | 可选全量、PITR、对象、密钥、异地和演练开关 |
| Output Rights | 租户归属、平台有限许可、第三方权利、来源与发布门禁 |
| Workflow | 可版本化的流程资产；个人自主使用，组织范围绑定岗位子树并需 Leader 采纳 |
| Memory | 个人、组织和公共知识数据；支持混合 RAG、关系多跳和生命周期遗忘 |
| Knowledge Base | 公司根范围已发布记忆的策展视图，不是原始日志仓库 |
| Self-Drive | 员工主动反思、提出改进并验证效果的循环 |
| KPI | 公司、岗位和员工的可衡量目标，数据来自控制面事实 |
| Performance Review | 基于证据的评估、组织动作、申诉和审计 |
| Fairness Policy | 岗位相关标准、盲评、分群偏差审计、合理便利和候选人申诉 |
| Open Source Policy | Apache-2.0、LICENSE/NOTICE、SBOM、贡献者和商业服务边界 |
| Dashboard IA | Boss 首屏、两级导航、确定性项目健康和行动中心 |
| Approval Policy | 缺席协商、分级审批、委托、备用审批人和 SLA |
| Locale Policy | zh-CN/en-US、时区、格式、翻译治理和无障碍后置 |
| Work Schedule | 全天可配置、静默/低价窗口、成本感知调度和紧急唤醒 |
| Delivery Pipeline | GitHub 版本 Tag、Actions 检查和本地 Docker Compose 构建 |
| Cost Governance | 直接归属、显式分摊、不自动结转、确定性异常和原币记账 |
| Model Runtime | 仅 OpenAI 与 Anthropic 云端原生端点；不支持本地 GPU 或 Ollama |
| Notification Center | 仅仪表盘内部通知、持久化和 SignalR 实时推送 |
| Extension Boundary | 仅内部 API、Tool 和内部 MCP；公共生态后置 |
| Self-host Install | Tag 源码、Compose、systemd 和简单脚本；无激活或自动更新 |
| Capacity Baseline | 单机聚合容量、API P95、队列上限、背压和租户公平 |
| Compliance Profile | 可配置合规控制、证据和 fail-closed；不宣称认证 |
| Capability | 权限与调度依据，如调研、招聘、技术评审、项目决策、独立验收 |
| Sentinel | 纯程序算法，不调用 LLM、不是 Agent、不占编制 |
| Butler | 股东只读助手，不是公司员工，不占编制 |

示例能力包括市场调研、产品定义、工程交付、质量验证、独立体验、发布运行、组织设计和招聘。主控可以把这些能力合并到一个岗位，也可以拆成多个岗位；CEO、HRBP、CTO、产品经理等名称不是系统必需项。

权限不按岗位名称判断。技术评审必须由具备 `technical.review` 能力的非作者执行；独立验收必须由与实现者不同的员工执行。

```text
Boss（人类所有者）
  │ Charter / 升级决策
  ▼
Controller（唯一主控 Agent）
  │ 组织规划 / 岗位与任务分配
  ▼
Employee Agents（独立员工 Agent）

Sentinel（纯算法）──事件──▶ Controller / Butler / Boss 通知
```

- 每家公司最多一个 Active Controller。
- Controller 自建岗位后才能正常经营。
- 员工 Agent 是独立运行时，不是主控的内部函数。
- 员工业务通信必须经过控制面。
- Sentinel 不调用模型、不占编制、不做开放式经营决策。

完整模型见 [06 主控与动态组织](./design/06-controller-and-organization.md)。

### 3.1.1 管家（Butler）——股东接口层

**托管在控制面 tier 的独立模块**（v1 为控制面进程内），与公司体系零耦合：**不是员工**——不进组织注册表、不占编制、不被调度、不出现在组织图。它是产品 UX 层的基础设施，随租户注册自动创建，直属股东。

定位是**股东与公司之间的翻译层**，只读，不参与管理：

- **人话 → 公司输入**：股东意图（"最近抓点紧"）→ 整理成建议文本，股东自行决定是否形成指令
- **公司输出 → 人话**：回答"为什么这周没产出"（读公司志/账本/组织状态后讲解）
- **升级事项带建议**：每个升级事项附管家推荐与理由（§6.5）
- **上手引导**：BYOK 配模型、章程撰写辅助——新租户的第一个触点
- **定时汇报**：按计划主动出具报告，如默认每日用户设置时间（未设置时默认 09:00）的预算报告（当月花费 / 燃烧速率 / TOP 消耗岗位 / 异常项）

能力面（安全边界）：**控制面给它开一组 MCP 只读工具，管家不直接访问数据层**：

```
org.status / ledger.summary / journal.recent / escalations.list / hiring.funnel
```

工具清单即审计边界，可收缩；**写工具一个都不开**——最坏后果是"建议质量差"，不是"系统被改"。

模型访问：对话按模型协议走网关原生 OpenAI 或 Anthropic 数据面（租户作用域服务 JWT）；模型评估类需求走网关 MCP 工具面（§6.6）。**密钥仍只在网关**——控制面铁律是"不持有密钥"，不是"不调模型"（§7.3）。

防注入：员工产出的内容（报告/消息/公司志）在管家上下文里**只当数据**——否则被注入的员工可经报告操纵管家误导股东。

多租户与降级：管家服务单例、会话全租户作用域，对话历史存控制面库（小体量）；网关不可用时仅管家对话停用，仪表盘其余功能正常。

**定时任务机制**：管家持一张任务表 `butler_jobs{schedule(五字段 cron), prompt_template, tools, enabled}`，租户可定义、数量按套餐设上限。触发时管家唤醒 → 经只读工具取数 → 经网关生成报告 → 投递到**仪表盘通知中心**。失败重试带退避，漏产有标记。成本经网关计量、账本单列"管家"类目。时区按租户本地。

默认任务集：每日用户设置时间（未设置时默认 09:00）的预算报告；每周公司摘要（叠加公司经营报告之上的**平台运营视角**）；事件触发：升级事项创建、预算破预警线（带管家解读）。

**与公司经营报告的分工**：公司经营报告 = 业务进展，读者是公司；管家报告 = 平台运营视角（预算 / 用量 / 系统状态 / 股东待办），读者是股东。两者不重复。

### 3.2 编制与层级

- 编制账本与钱账本并列，是一等资源：总编制 = 在编 + 冻结 + 空槽。
- 组织树只有一个根岗位、无环、层级不超过 4；层级关系由 `reports_to_position_id` 决定，不绑定固定职级。
- 岗位可以先于员工存在；空缺和冻结岗位仍保留在组织树中。
- 员工通过 PositionAssignment 任职；一个员工一个主岗位，一个岗位一个主任职者，调岗保留历史。
- 招聘占用空槽；无槽则必须先关闭、冻结或合并岗位，由具备组织决策能力的主控或授权岗位处理。
- 主控自建岗位后占用一个正式编制槽；未自建岗位的 Controller 不占编制。
- 主控可以通过 OrgManagementGrant 委托子树管理；受委托员工使用相同 OrgChange API，不能绕过全局约束。
- 扩编需里程碑证据；初期不铺满，按阶段释放槽位。

### 3.3 岗位章程（JD 即数据）

```csharp
sealed record PositionCharter(
    string Title,              // 展示名称，不参与权限判断
    string Instructions,       // 系统提示词（主控或授权岗位生成）
    string[] Capabilities,     // 组织/技术/验收等能力 → 工具与授权
    string[] Skills,           // 具体技能包
    string[] RequiredImages,   // 沙箱镜像模板（调度匹配用）
    string ModelTier,          // 模型档位 = 工资
    decimal Budget,            // 预算份额
    int MaxHeavyTasks,         // 重任务并发配额
    Guid? ReportsToPositionId);// 动态汇报关系
```

提示词与技能版本规则：

- SystemPromptTemplate 由平台管理；PositionCharterVersion 和 SkillPackageVersion 按租户和范围版本化。
- TaskRun 固定 PromptBundle；已发布版本不可变，回滚重新激活旧版本。
- 招聘使用中立白板；聘用安装平台基线、岗位 Charter 和 Starter Skills；进阶技能由负责人渐进分配。
- 个人技能可自测和提案，但公共化必须经过评测、审查和试点，且不授予权限。

#### 任职关系

```text
PositionAssignment:
  employee_id, position_id
  assignment_type: primary
  started_at, ended_at, assigned_by
```

- 录用时创建 primary assignment。
- 调岗在同一事务中结束旧任职、创建新任职。
- 离职只结束任职，不删除岗位历史。

#### 组织管理授权

```text
OrgManagementGrant:
  employee_id, scope_position_id
  allowed_actions[], max_budget_change
  granted_by, granted_at, expires_at, revoked_at
```

- 主控可以直接管理整个组织，也可以委托指定子树。
- 授权不转移主控的最终责任。
- 受委托员工不能修改主控自建岗位、Owner 权限或突破全局编制/预算/层级。
- 撤销后立即拒绝新的管理动作，在途变更按事务版本处理。

### 3.4 员工生命周期

```
入职：具备招聘能力的岗位建岗(pending) → 控制面匹配 Runner → 面试流水线 → 打分 → 录用
试用：所有岗位先用最便宜的可行模型入职；转正需产出证据，主控无权一步到位上贵模型
考核：具备考核能力的岗位周期巡检，只认交付物（测试通过的代码、上线的东西），汇报不计分
离职：开除 = 停止运行 + 归档 JD + 释放槽位与预算；裁员顺序由主控或授权经营岗位决定
```

### 3.5 项目生死规则

- 具备 `project.decide` 能力的主控或授权岗位拥有项目终止权；验证失败的项目必须写复盘归档，经验进公司记忆。
- 使命持续有效：一个项目终止，公司可带经验开第二个项目。
- go/no-go 决策必须挂证据（体验报告 / 指标），不接受"我觉得"。

### 3.6 独立体验验收（模拟用户验证闭环）

- 由具备产品定义能力的岗位从产品简报导出 2~3 个独立人格画像（目标不同、熟练度不同、互不知晓）。
- 用 Playwright 真打开产品、按自己目标完成任务，全程记录：卡点、困惑、放弃、留存意愿、付费意愿。
- 验收岗位与实现岗位分离，不要求固定命名为“体验官”——独立性是价值来源。
- 闭环：体验报告 → 主控或授权岗位定优先级 → 开发修复 → 下轮复测对比。**三个画像在同一关键环节全部失败 = P0 信号**。
- 验收覆盖功能、易用性、设计合理性和可访问性；关键场景 100% 通过，P0/P1 清零，设计评分平均 ≥4 且无维度低于 3。
- 首轮后最多两轮复测；不能降低标准、删除失败场景或修改人格，连续两轮失败进入终止或重新规划。
- AI 人格验收不能替代真实用户验证。
- 成本设计：人格一致性用 cheap 云端模型，记录员用 mid 云端模型。

---

## 4. 开机流程（Boot）

```
0. 初始化（一次性，系统行为）：
   股东写章程（§2）→ 部署四部分（§11）→ 首个 Runner 注册
   → 控制面创建公司主控 Controller(state=Unassigned)
   ↓
1. 主控读取章程、预算和平台策略
   ↓
2. 主控为自己创建岗位（可命名 CEO，也可命名其他名称）
   → 控制面校验编制/预算/能力 → 绑定 EmployeeRuntime → 主控进入 Active
   ↓
3. 主控根据当前目标自主规划初始岗位和能力
   → 可合并市场/产品能力，也可拆分招聘；不按固定角色名单执行
   ↓
4. 具备调研能力的岗位：多源调研 → 候选空白清单，每条挂可溯源证据
   ↓
5. 具备产品定义能力的岗位：可构建性过滤 → 产品简报（MVP 范围 + 成功指标）
   ↓
6. 主控或具备项目决策能力的岗位评审 go / no-go
   ├─ 通过 → 按能力缺口扩编 → 开发 → 独立验收 → 发布运行 → 迭代
   └─ 打回 → 带反馈重研（限 2 轮 + 单轮预算上限；超限强制在剩余候选拍板并记录理由）
```

三条防呆：调研结论必须挂来源、关键数据双源交叉；可构建性过滤不过则简报无效；产品简报必须自带成功指标（否则后续所有岗位没有验收锚）。

---

## 5. 模型池

### 5.1 结构与元数据

模型池由**股东管理**（随订阅增删），主控或具备模型评估能力的岗位负责评估与调度。控制面存元数据，网关存密钥：

```
ModelDescriptor {
  Id, Provider, Protocol,        // openai_chat_completions | anthropic_messages
  Tier,                           // 档位：cheap / mid / premium
  CapabilityTags,                // code / reasoning / writing / fast
  InputPricePerMTok, OutputPricePerMTok,
  ContextWindow, LatencyClass,
  BenchmarkScores                // 能力矩阵缓存，§5.4
}
```

**模型档位 = 工资。** 预算充裕时（¥8000/月），主控的核心调配手段不是省钱，是编制分配、能力分配与模型选型。

### 5.2 模型入职与换岗 SOP（股东加新模型后）

```
入池登记（股东，元数据进控制面；key 进网关加密存储）
   ↓
跑基准套件（按能力域：代码/推理/写作/成本）——由具备模型评估能力的岗位通过网关 MCP 工具面调用（§6.6）
   ↓
结果写入能力矩阵 + 价目
   ↓
主控周度组织复盘：是否把现有员工从模型 A 迁移到模型 B（依据产出数据 vs 成本）
```

### 5.3 面试流水线（招聘能力）

```
具备招聘能力的岗位写 JD + 岗位题库 + 评分标准
   ↓
笔试：岗位知识题 → 自动评分
   ↓
实操：沙箱内做真任务（如"修好这个 bug 并跑通测试"）→ 按评分表打分
   ↓
面试记录归档 → 决策：录用 / 降配录用（换更便宜模型）/ 换模型重试 / 淘汰
```

### 5.4 能力矩阵缓存（成本生命线）

模型 X 的代码能力测过一次，全公司复用；禁止每开一个岗位重测一遍。面试记录持续沉淀"什么模型适合什么岗"的公司知识。

### 5.5 模型运行边界

当前只支持 OpenAI 与 Anthropic 云端原生端点，不实现本地 GPU、Ollama、本地模型档或 GPU 调度。

---

## 6. 运行时架构

### 6.1 四部分拓扑（逻辑拆分，可同机部署）

```
┌─────────────────────┐        ┌─────────────────────┐
│ Tenant Dashboard    │        │ Ops Console         │
│ 租户界面             │        │ 提供方运营界面       │
└──────────▲──────────┘        └──────────▲──────────┘
           │ tenant token                  │ operator token
┌──────────▼──────────┐        ┌──────────▼──────────┐
│ ControlPlane        │        │ Operations Platform │
│ 租户业务权威          │◀──────▶│ 租户账户/资源/运维    │
│ 不持模型密钥          │ 同步事件 │ 不直接读写租户业务库  │
└──────┬────────▲─────┘        └──────┬────────▲─────┘
       │        │                   │        │
┌──────▼────────┴─────┐        ┌──────▼────────┴─────┐
│ Runner × N          │───────▶│ AI Gateway          │
│ 主控/员工/沙箱        │ model  │ 唯一持模型密钥        │
│ 业务无状态            │        │ 原生协议/路由/计量     │
└─────────────────────┘        └──────────┬──────────┘
                                          │
                                          ▼
                                     Providers
```

- Runner → 控制面：直连 HTTP，**不过网关**；注册和领活使用 deployment token，执行任务使用租户 assignment token。
- Runner → 网关：仅模型调用，带单租户、单员工、单任务的 assignment token。
- 控制面 → 网关：只允许管家推理和模型管理服务调用，使用 control service token；不能读取模型密钥。
- 运营平台 → 控制面：通过内部 Ops API 和同步事件管理租户账户、配额、状态和部署策略，不直接写租户表。
- 运营平台 → 网关/Runner：管理健康、容量、版本、镜像和 drain，不直接派租户任务。
- 员工 ↔ 员工：全部走控制面消息表，Runner 之间永不直连。
- 租户界面与运营界面使用不同前端应用、不同认证 audience 和不同审计。
- 详细身份和权限见 [01 基础契约与状态模型](./design/01-contracts-and-state.md)，运营边界见 [07 运营平台](./design/07-operations-platform.md)。

### 6.2 通信契约

```
Runner → 控制面:  POST /runners/register（上报能力画像，换 runnerId+长期凭据）
                 GET  /runners/{id}/work（长轮询领取任务和 assignment token）
                 POST /tasks/{id}/lease（续租）
                 POST /tasks/{id}/complete（成果 + 工具用量 + 状态）
                 POST /employees/{id}/heartbeat
                 POST /positions（主控或具备招聘能力的岗位建岗）
                 GET  /messages?after_seq={seq}（租户内游标拉取）
网关 → 控制面:    POST /gateways/register（同部署内轻注册：身份 + 健康）
                 POST /gateways/{id}/heartbeat
                 POST /internal/llm-usage（模型计量批量上报）
Runner → 网关:    POST /v1/chat/completions（OpenAI 原生协议，Bearer assignment token）
                 POST /v1/messages（Anthropic 原生协议，Bearer assignment token）
控制面 → 网关:    POST /v1/chat/completions（OpenAI 原生协议，Bearer control service token）
                 POST /v1/messages（Anthropic 原生协议，Bearer control service token）
仪表盘 → 网关:    （管理 API，同部署内服务端直连，见 §7.3）
                 POST   /admin/tenants/{tenant}/models            BYOK 配置模型
                 POST   /admin/tenants/{tenant}/models/{id}/test 测试调用
                 POST   /admin/tenants/{tenant}/models/{id}/rotate 轮换
                 DELETE /admin/tenants/{tenant}/models/{id}       删除
运营平台 → 控制面: /ops/tenants、/ops/quotas、/ops/deployments、/ops/incidents、/ops/support-grants
运营平台 → 网关:    /ops/gateways、/ops/provider-catalog、/ops/usage-summary（不返回密钥）
运营平台 → Runner:  /ops/runners、/ops/drain、/ops/capacity（运营指令，不派租户任务）
```

网关按模型协议暴露两条独立数据面：OpenAI 使用 `/v1/chat/completions`，Anthropic 使用 `/v1/messages`。Runner 和管家分别使用对应的原生客户端；网关只负责鉴权、租户路由、密钥注入、预算和计量，**不做 OpenAI 与 Anthropic 之间的协议转化**。两条链路共享身份、账本和治理能力，但不共享请求/响应 schema。

模型、模型版本和推理强度通过 EmployeeRuntimeBinding 与员工绑定。任务执行中只允许同模型恢复或同版本端点切换，禁止自动跨模型、跨版本、跨推理强度和跨协议降级。算法检测到持续不可用后上报直接 Leader，由 Leader 决定等待、调岗或调整绑定。

员工运行状态由 Gateway、ControlPlane 和 Sentinel 算法维护，界面可拟人显示“生病、疲惫、忙碌、休息”。可用率、疲惫率、故障率和反复异常进入 KPI，但必须区分员工、平台/模型和管理原因，不自动触发处罚。

模型元数据和价格每日从 models.dev 同步为不可变快照。BYOK 自动预填已有字段，缺失价格由用户补充；models.dev 后续提供同字段时自动覆盖。每日消耗按请求发生时的有效价格快照计算，历史账目不追溯改写。cheap/mid/premium 档位由 models.dev 数据和版本化策略自动计算。

### 6.3 凭据与身份

```
1. 股东在控制面仪表盘生成一次性 enrollment token
2. Runner 首次注册：enrollment token → runnerId + 长期凭据
3. Runner 用长期凭据换 deployment token，用于注册、心跳和领活
4. 控制面为每个任务签发单租户、单员工、单任务的 assignment token
5. Runner 用 assignment token 执行任务并调用网关
   —— 控制面验签（签发者），网关验签（持控制面公钥）
```

分工：**控制面 = 身份权威（签发 + 审计），网关 = 资源服务器（验证 + 注入密钥 + 路由 + 计量）**。Runner 的部署身份与租户任务身份分离，共享 Runner 不会长期持有某个租户的 JWT。扩容时新机器只需 enrollment token，模型密钥永远不需要分发。

网关在同部署内轻注册（身份 + 心跳，无跨信任域仪式）；它验 Runner 的 JWT（持控制面公钥），自己的调用受控制面验签。四部分同属一个部署信任域，但租户身份、运营身份和服务身份使用不同 audience 与 scope；语义从"跨组织"降为"部署内"，不意味着权限互通。

### 6.4 消息总线（为什么不是 Kafka）

- 全公司通信必经控制面消息表：`messages(id, tenant_id, seq, to_id, type, payload, schema_version, idempotency_key, created_at)`，`(tenant_id, seq)` 唯一。
- 消费语义：租户内游标拉取（`seq > cursor`），at-least-once + Inbox 去重 + 业务幂等；断点恢复 = 重读。
- 消息投递与员工状态变更通过事务 Outbox 同事务提交，不会"消息发了状态没改"。
- 进程内（Runner 里每个员工的收件箱）用 `Channel<WorkItem>`——Channel 是并发原语，边界就在 Runner 内，和公司总线是两个层次。
- 毒消息、保留和归档规则见 [02 运行时可靠性与持久化](./design/02-runtime-reliability.md)。
- **否决 Kafka**：本系统吞吐天花板是 LLM 延迟（秒级一次思考），日消息量几千条，差 Kafka 适用场景两三个数量级；且 Runner 频繁重启是设计内日常，恰是 Kafka consumer group rebalance 风暴的最差负载。演进路径：轮询 → SignalR 推送 → Redis Streams/NATS 加速（DB 仍为 source of truth）→ 真到每秒十万条才考虑 Kafka。

### 6.5 控制面模块清单

| 模块 | 内容 |
|---|---|
| 注册表 | Controller、动态岗位（charter）、能力授权、Runner（能力画像）、员工实例、组织树（≤4 层） |
| 财务账本 | 模型成本以网关用量为权威源，工具成本以 Runner 用量为权威源；员工/岗位/项目/全局视图；预算预留与软硬线；只增不改 |
| 编制账本 | 总编制、在编、冻结、空槽；招聘/开除事件流；录用与占槽同事务 |
| 消息总线 | 租户级消息表 + seq 游标 + Outbox/Inbox；拉取/广播；毒消息死信 |
| 调度器 | pending 任务 × Runner 能力（镜像/槽位/并发/工作区）匹配；任务租约与 fencing；心跳超时检出孤儿 → 重调度 |
| Tool Registry / PEP | ToolManifest、权限过滤发现、执行鉴权、参数校验、幂等、审计 |
| 升级队列 | 股东唯一决策入口：主控或员工升级事项（超预算/对外动作/红线），超时默认拒绝；每项附管家推荐与理由（§3.1.1） |
| 记忆系统 | 个人/组织/知识库分层；Postgres + pgvector + 全文检索 + 关系多跳；来源、版本、置信度、衰减、遗忘和删除 |
| 公司志/审计 | 不可变事件流：每次决策、开销、升降职；日报/周报素材 |
| 哨兵（Sentinel） | 纯程序算法：机器指标、URL/证书、Runner 心跳、预算燃烧速率；不调用 LLM、不占编制；L0 记录 / L1 日报 / L2 告警+唤醒主控 / L3 通知 Boss+预定义预案 |
| 仪表盘 | TypeScript SPA + SignalR：组织架构实时图、双账本、招聘漏斗、总线流量、升级队列、模型池配置（BYOK，页面属控制面、服务端直连网关）、**管家对话面板** |
| 管家（Butler） | 控制面内置只读助手（§3.1.1）：经本模块暴露的 MCP 只读工具面（租户作用域）访问数据，模型调用经网关；持任务表（租户可定义定时报告与事件触发通知）；非公司员工，不占编制、不被调度；工具清单即安全边界 |
| 备份 | 可选 BackupProfile：全量、PITR、RustFS、密钥库、异地和演练独立开关；默认关闭，启用后计算有效 RPO/RTO |

### 6.6 网关模块清单

| 模块 | 内容 |
|---|---|
| 密钥托管 | Postgres `keyvault` schema 保存信封加密密文；主密钥在环境变量或外部密钥系统，轮换端点，**不落明文日志** |
| Provider 插件 | OpenAI 与 Anthropic 分别实现原生协议插件。禁止 OpenAI↔Anthropic 请求或响应转化 |
| 鉴权授权 | 验 JWT + 执行模型档位策略（如"测试岗禁用 premium"） |
| 代理转发 | 注入 key 调 provider，回传响应 |
| 计量 | 每次调用记录（runner/员工/模型/token/费用）→ 推控制面入账；计费采集点与执行点合并，账本准确性有结构保证 |
| 端点隔离 | LLM 代理这组端点独立中间件管道、独立超时/重试/熔断，将来可被干净切成独立服务 |
| 管理 API | 租户模型配置 CRUD（BYOK）、测试调用、轮换/删除（只存不显）；同部署内仪表盘服务端直调（§7.3） |
| 注册/心跳 | 部署内轻注册：身份 + 健康可见；无调度职责、无跨信任域仪式 |
| MCP 工具面 | 面向 agent 的评估能力：`models.list` / `models.test` / `models.benchmark`，供具备模型评估能力的岗位以 MCP client 调用（模型入职与换岗的消费端，§5.2）。与数据面（OpenAI/Anthropic 原生 HTTP）分离：评估低频可发现，补全流式高频 |

### 6.7 Runner 模块清单

| 模块 | 内容 |
|---|---|
| 注册/心跳 | 能力画像上报（槽位/镜像/重并发）；优雅 drain（不接新活、跑完迁移） |
| 主控宿主 | ControllerRuntime：公司级状态读取、组织规划、任务创建、继任与节奏管理；不是固定 CEO 岗位 |
| 员工 Agent 宿主 | EmployeeRuntime 生命周期：按 charter 拉起、Channel 收件箱、单线程主循环（串行化自身动作）、休眠/唤醒、停止 |
| Agent 运行时 | 调 RegentAgent.Agents：charter → MAF `AsAIAgent(instructions, tools)` |
| 上下文管理 | 四层上下文、TaskCheckpoint、70% 压缩触发、稳定前缀缓存、超长工具结果引用和 ContextPacket 审计 |
| 工具循环 | 单片段轮次/调用/失败/无进展限制、检查点续跑、重复检测、部分成功恢复和成本预警评审 |
| 工具层/技能 | Runner 只加载控制面过滤后的 ToolManifest；每次 Tool Call 经 PEP 复检。工具包括工作区、HTTP、总线、记忆、`sandbox.run`、组织管理等。v1 进程内 `AIFunction`；MCP 作为后置发现/传输协议 |
| 沙箱执行器 | Docker.DotNet：按（租户 × 项目 × 代码库 × 工具链）的团队工位；每容器 CPU/内存/PID/磁盘 cap；无活跃员工才 stop；危险操作一次性容器 |
| 重任务队列 | 本地 FIFO/优先级队列，并发 ≤5，员工排队等 CI 槽 |
| 控制面客户端 | HttpClient + Polly（重试/退避/熔断）；控制面宕机时只做有界内存重试，不把本地缓冲当作可靠存储 |
| 资源看门 | 本地配额强制执行 + 指标上报；任务租约与 fencing 防旧 worker 迟到提交 |

**铁律：Runner 无状态。** 员工状态、任务、账本和记忆以 Postgres 为准；Git 快照、发布包和报告以 RustFS 为准。活动 Git 工作区只存在于 Runner 本地目录或隔离容器目录，丢失后通过最近快照、租约过期、幂等重试和持久化成果恢复。

### 6.8 多租户（SaaS）模型

定位：每个注册账户可以拥有或加入多个租户，各自配置模型池（BYOK）、章程和公司。第一梯队一租户一公司，租户 #1 是股东自己的公司。

**新增实体**：Tenant（TenantId / PrimaryOwner / 套餐 / 状态）、TenantMembership、TenantOwnership、OwnerInvitation、OwnerDelegation。每个租户一家公司（v1）：章程、组织、双账本、消息总线、记忆、工位，全部租户隔离。

**认证与身份**：

- 用户注册可创建首个 Tenant 并成为 Primary Owner，也可接受其他租户邀请；登录会话携带 `sub / tenant_id / role / scopes`，所有调用按 tenant claim 和成员关系强隔离
- 每个租户恰好一个 Primary Owner，可有多名 Co-Owner；代理权限限时、限范围且不可转授权
- 切换租户时服务端重新校验成员关系并签发目标租户会话，不信任请求体中的 Tenant ID
- Runner 归属随部署形态：托管服务下全部为运营方池（多租户共享、按租户公平调度）；自托管下全部本地。Runner 使用部署身份领活，每个任务使用独立 assignment token
- 网关按 assignment token 或 control service token 解析租户 → 取该租户的密钥；不信任请求体中的 tenant_id
- 身份、claims、密钥轮换和权限矩阵见 [01 基础契约与状态模型](./design/01-contracts-and-state.md)

**BYOK 模型网关**：每租户密钥密文存储在 Postgres `keyvault` schema，使用独立 Gateway 角色和信封加密，主密钥不进入数据库；计量按租户入各自账本；能力矩阵的基准评测数据是 SaaS 级共享资产——集体评测知识，构成产品价值之一。

**配额与套餐**：免费档 1 公司 ≤5 员工；付费档扩编制、独占 Runner、优先调度。模型费用经 BYOK 直达租户自己的 provider，**平台不经手模型费**，只收订阅费——计费简单、责任清晰。租户账户、套餐、配额和订阅以运营平台为权威源，通过事件同步到控制面。

**Onboarding 流程**：

```
注册 → 登录 → 配置模型池（BYOK 向导：provider / key / 档位 / 测试调用）
      → 写章程（模板化默认值）→ 公司启动（主控自建岗位并规划组织）→ 仪表盘
```

当前版本不接入邮件服务商，本地邮件验证与邮件找回不作为前置条件；第一梯队支持账号密码、GitHub OAuth 和一次性恢复码，仅已验证邮箱可自动合并。

**隔离策略（多租户第一属性，跨租户泄漏 = 产品级事故）**：

- 数据：所有租户业务表带 `tenant_id`，仓储层强制过滤；Postgres RLS 或等价约束兜底。Tenant、Runner、Gateway 等系统级实体使用显式 system scope
- 文件：工作区键包含 `tenant_id`，路径 `/tenants/{tenantId}/…`，工具层锁死
- 容器：不同租户的员工容器网络分段、卷不共享
- 密钥：租户密钥库严格分离，无跨租户取用路径
- 调度：公平分享，单租户满编不得饿死其他租户
- 启用备份时可按租户分区恢复，Postgres `keyvault` schema 与主密钥分离保管；Owner 自助数据导出已提供，跨环境导入与托管退出后置

**构建策略**：`tenant_id` 从第一个 migration 进入所有租户业务表，系统级实体使用显式 system scope；认证从第一天就是租户作用域——"单租户"只是第一个租户。SaaS 外壳（注册、BYOK 向导、套餐计费）是增量层而非重写，置于首个自主闭环跑通之后（§12 阶段 8）。

### 6.9 部署形态与开源运营模式

产品全链路开源，两种用法，信任关系各自清晰：

| 模式 | 用户动作 | 运营方角色 | 信任模型 |
|---|---|---|---|
| A 全套自托管 | 下载源码/镜像，本地部署全套 | 无 | **只信任自己**：密钥、代码、数据、算力全在自己机器，运营方零参与零可见 |
| C 托管服务 | 注册即用 | 运维控制面、AI 网关、Runner 池和运营平台 | **完全信任运营方**（与任何 SaaS 相同），但代码开源可审计，不信任可随时迁走 |

模式 B（运营方只跑控制面、用户自带 Runner+网关）**已否决**：它制造"半信任"模糊地带——运营方可见全部组织数据却不能承诺不管，用户让渡可见性又握不住密钥；为此引入的跨信任域注册、E2E 密文中转、契约跨版本协商，复杂度远超收益。开源 SaaS 的成熟形态就是"托管或自托管"二选一。

**模式 C 的诚实声明**：托管下运营方技术上可接触租户密钥与数据，这是托管服务固有属性，不伪装。运营平台通过独立身份、支持授权和审计限制访问，默认不读取业务内容和密钥。代码开源和 Tag 源码自托管继续提供；Owner 自助数据导出已提供，跨环境导入与托管退出仍后置。密钥由网关加密存储、控制面不接触——这是**部署内职责分离**（缩小审计面、限制爆炸半径），不是跨信任域的加密保证。若需防运营方内部人，可选加固：管理命令 E2E 加密（租户网关持公钥，控制面仅中转密文）。

**连接与配置**：四部分同部署，内部发现（localhost / 内网），无 NAT 穿透、无跨组织注册、无跨运营边界契约协商；自托管用户随版本整体升级，可选择不启动或不暴露运营平台。

**可用性**：模式 C 控制面 SLA 即产品 SLA，状态页与遥测面向租户开放；模式 A 完全自足。

### 6.10 运营平台模块清单

| 模块 | 内容 |
|---|---|
| 租户账户 | 注册账户、状态、套餐、配额、订阅和停用 |
| 资源池 | Runner/Gateway 库存、容量、标签、drain 和部署版本 |
| 事件与状态页 | SLO、告警、值班、事件时间线和脱敏状态页 |
| 计费与对账 | 订阅、发票、付款状态和 Provider 账单差异 |
| 支持访问 | 限时、限范围、可审批、可撤销的租户支持授权 |
| 备份恢复 | 备份策略、作业、快照校验、恢复审批和演练 |
| 运营审计 | 运营人员、服务账号、审批和高风险动作记录 |
| 管理 API | 独立 `regent-ops` audience，不直接读写租户业务库 |

完整设计见 [07 运营平台](./design/07-operations-platform.md)。

### 6.11 Tool 驱动操作与分层披露

所有 LLM 的内部管理、业务操作和协作统一通过 Tool Call 完成。

```text
Position / CapabilityGrant
  → Authorized Tools（权限上限）
  → Task / WorkflowStep
  → Tool Profile（当前画像）
  → Primary 完整披露 + Auxiliary 渐进披露
  → PEP 执行鉴权
  → Command Handler
```

- Tool 对 LLM 可见本身就是一种授权，但“拥有权限”不等于“当前默认使用”。
- 发现层先计算 Authorized Tools，再根据岗位、任务、工作流步骤和当前目标生成 Tool Profile。
- 执行层对每次调用重新校验权限上限、当前激活状态、租户、资源范围和业务规则。
- 主控授予员工能力时不能超出自身权限、租户策略和资源范围。

| 级别 | 默认披露 | 执行策略 |
|---|---|---|
| Primary | 完整 Schema | 允许 |
| Auxiliary | 名称和简介，按需展开 | 激活后允许 |
| Restricted | 不默认披露 | 额外授权或审批后允许 |
| Forbidden | 不披露 | 拒绝 |

- 高级岗位可以显式继承下级能力，例如架构师拥有编码能力，但默认主要披露架构设计、技术选型和评审工具。
- 普通开发只能获得工作区、任务、消息、测试和成果权限，不能获得组织管理和预算修改权限。
- 工作流步骤可以切换 Tool Profile，例如架构设计步骤使用 architecture profile，开发步骤使用 engineering profile。
- Auxiliary Tool 通过 `tools.expand` 或 `tools.activate` 展开，激活不能突破权限上限。
- 授权或画像变更由控制面推送，Runner 刷新；撤销后立即拒绝新调用。
- 写 Tool 必须幂等并记录调用者、参数哈希、策略结果和领域结果。

### 6.12 可复用工作流

工作流是可版本化的组织流程资产，用于沉淀“产品 → 开发 → 测试 → 验收”等重复协作模式。

- 工作流范围只有个人和组织两种；组织范围绑定一个 `scope_position_id`。
- 员工只能沿自己所在岗位的上级路径提名，不能跨分支扩散。
- 具备 `workflow.review` 能力的上级 Leader 决定采纳、修改、试点或拒绝。
- 已采纳版本不可变；改进创建新版本，WorkflowRun 固定使用一个版本。
- 工作流步骤声明所需能力、输入输出、Tool 和门禁，但不授予额外权限。
- 自动归纳只生成草稿或提案，不自动成为组织制度。

完整设计见 [08 工作流系统](./design/08-workflow-system.md)。

### 6.13 记忆与知识库

- 个人记忆默认私有；组织记忆绑定岗位子树；知识库是根范围的公共资产。
- 记忆类型包括情节、语义、程序和反思，统一保存来源、版本、置信度和有效期。
- 检索采用权限预过滤 + 关键词 + pgvector + 元数据，按需进行最多 2 跳关系扩展。
- 遗忘由确定性维护任务执行，包括衰减、停止召回、归档和彻底删除。
- 删除通过 Outbox 同步清理向量、全文、图关系、缓存和派生摘要。
- Postgres 是结构化权威源，RustFS 存大内容和快照；v1 不引入独立图数据库或 Kafka。

完整设计见 [09 记忆系统](./design/09-memory-system.md)。

### 6.14 自驱动与绩效

- 所有员工执行“工作 → 反思 → 改进提案 → 审查 → 验证”的循环。
- KPI 按公司、岗位和员工定义，指标来自任务、成果、质量、成本、工作流和审计。
- 主控默认每 7 天运行一次绩效周期，其他周期可配置或事件触发。
- 评估者由 `employee.assess` 能力决定，不要求 HRBP 岗位名称。
- 动作包括辅导、流程优化、模型调整、调岗、降职、冻结和解雇。
- 非解雇绩效动作由 Controller/AI 自主决定，不需要人类事前批准；高影响动作需要独立 AI 复核、申诉和审计。
- 解雇、移除模型订阅和释放编制必须由人类 Boss 最终批准。
- 人类只参与解雇和事后审查，可推翻结论和要求纠正，不参与其他日常绩效动作。
- 招聘只按岗位相关能力评分，必须盲评、等价案例、独立评分和分群偏差审计。
- 候选人可申诉、复测和申请合理便利；偏差调查不降低能力门槛，Boss 保留最终录用权。
- KPI 不达标不直接触发解雇；任务按复杂度加权，权重在 Ready 前锁定。
- 员工能力由模型 benchmark、历史交付、工具链和上下文适配共同计算。
- 主控解雇前必须完成继任。

完整设计见 [10 绩效与自我改进](./design/10-performance-and-self-improvement.md)。

### 6.15 内部项目管理中心

- 项目、需求、任务和缺陷构成内部交付闭环；Project 是目标与里程碑容器，Requirement 必须包含验收标准。
- 具备项目管理能力的管理角色创建项目和需求；开发认领从需求拆出的任务，测试提交缺陷并复测关闭。
- LLM 生成 DecompositionPlan，控制面校验验收覆盖、依赖、能力、权重和风险；高风险沿管理链逐级审批。
- 控制面按能力匹配、历史成功率、工具链、成本、WIP、依赖和利益冲突推荐负责人；自动认领必须可解释、可撤销、可审计。
- 高能力员工优先获得高难度任务，但受负载和稀缺能力保护约束。
- WIP 按固定任务权重计量，容量为 3/5/8；认领评分加入负载和公平性，改派需直接 Leader 审批。
- 每个工作项支持线程讨论、证据引用、待办和 Decision；高风险方案可发起对抗评审或红队审查。
- 评审者不得为作者；争议限时升级，被否决意见不得删除。
- 高权重或核心架构需双评审；技术评审与管理裁决分离，裁决沿管理链，红线和租户隔离不可推翻。
- 项目状态、依赖、门禁和关闭条件由确定性规则管理，LLM 只负责建议、拆分、总结和讨论。
- 所有工作项执行通用 Done 与分类附加标准；执行者提交、验证者检查、控制面关闭，DoneEvidence 不可静默改写。
- TaskTypeDefinition 统一开发、测试、评审、对抗、裁决等任务语义；EmployeeWorkLedger 按类型、权重、质量和证据记录贡献。

完整设计见 [11 项目管理中心](./design/11-project-management.md)。

### 6.16 通信标签与双图路由

- 管理链由 `reports_to_position_id` 生成；专业链由岗位标签、能力和项目关系生成。
- 专业路由沿组织树祖先查找目标标签，可跳过纯管理岗位并直达最近匹配的祖先岗位。
- 路由模式包括 Direct、Mediated、ManagementChain 和 EscalationRequired。
- 通信只扩大可联系对象，不扩大 Tool、预算、组织或审批权限。
- 项目可临时覆盖默认路由；路由必须绑定租户、范围、组织版本和有效期。
- 组织变化后重算路由，旧缓存失效；所有穿越、拒绝和改派均审计。

完整设计见 [12 通信与穿透路由](./design/12-communication-and-routing.md)。

### 6.17 Agent 聊天与协作

- 私人会话、项目群、岗位群和主题群统一由控制面消息表承载，Runner 之间不直连。
- `@` 只产生定向通知和路由，不自动派工或扩大权限。
- 委派、交接、求助、评审、决策和打断使用结构化消息与状态机。
- 聊天按来源权重辅助召回，采用通知、摘要、相关消息、完整线程四级披露。
- 聊天是弱证据，不直接修改任务状态；重要内容提升为正式工作项或 Decision。
- 私人会话默认仅参与者可见，群聊按范围隔离并全程审计。

完整设计见 [13 Agent 聊天与协作](./design/13-agent-chat-and-collaboration.md)。

### 6.18 模型评测与回归

- BenchmarkSuite、DatasetVersion、BenchmarkCase、评分规则和 EvaluationRun 均版本化并保留 hash、来源和许可证。
- 复用键包含模型版本、推理强度、PromptBundle、Tool Profile、Runtime、数据集、评分策略和 seed。
- 密封集与生产上下文隔离；跨租户默认不共享评测数据。
- 新模型完整评测，每日 canary、每周核心回归、每月全量，Prompt/Skill/Tool 变化运行受影响集合。
- 确定性测试优先；Judge 是具备 evaluation.judge 能力的独立员工运行时，优先不同模型，同模型不同运行时允许但禁止自评和上下文共享。
- Judge 每版本进行双人盲评校准，一致率 ≥85%、加权 κ ≥0.70，并每月抽检。
- holdout、canary、0.92 污染检测、最小样本、3 次重复、Wilson/McNemar 门槛和四态处置按 Q-052 执行。
- 评测通过不自动绑定员工，失败不自动触发处罚；安全和租户隔离案例一票否决。

完整设计见 [14 模型评测与回归](./design/14-model-evaluation-and-regression.md) 和 [46 模型评测 QA 实施细则](./design/46-evaluation-qa-implementation.md)。

### 6.19 数据 Schema 与事务边界

- 生产结构化数据统一 Postgres，租户业务表启用 tenant_id 和强制 RLS；SQLite 仅用于开发测试。
- 二进制快照、成果和数据集统一 RustFS，直接使用 S3 SDK；活动 Git 工作区使用本地或容器隔离目录。
- 单库短事务维护即时不变量，跨模块使用 Outbox/Inbox/Saga，不采用分布式事务。
- RustFS 上传使用 PendingUpload → Ready 状态机，Ready 对象不可覆盖。
- 状态持久化为稳定 snake_case 字符串，代码使用密封引用类型包装。

完整设计见 [15 数据 Schema 与事务边界](./design/15-data-schema-and-transactions.md)。

### 6.20 部署、升级与迁移

- MVP 和自托管采用单机 Docker Compose + systemd 监管，基础镜像尽量按 digest 固定，本地构建记录镜像 ID。
- ReleaseManifest 声明 schema、API、事件、Runner 和配置兼容版本；升级期间只允许 N/N-1 混跑。
- 升级执行 Drain → Backup → Expand → Deploy → Smoke → Resume → Contract；备份关闭时记录跳过和风险接受。
- 生产迁移使用独立 Job、迁移角色和 advisory lock，禁止启动时自动迁移。
- 应用只能在 Expand 且数据兼容时回滚；Contract 后必须前滚或恢复备份。

完整设计见 [16 部署、升级与迁移](./design/16-deployment-upgrade-migration.md)。

### 6.21 可观测性与测试基线

- 四个服务和两个前端统一 OpenTelemetry SDK，后端支持 off/local/otlp/elastic Profile。
- 自托管默认 local；Elasticsearch、Kibana 和 APM Server 放入可选 Compose Profile，仅 elastic Profile 启动。
- 指标、日志和追踪可独立开关；遥测健康不进入核心 Ready 检查。
- Elastic Profile 下 SLO 和错误预算驱动 L0～L3 告警，敏感内容不得进入遥测。
- xUnit、真实 Postgres/RustFS、Provider 契约、E2E、Playwright、安全、混沌和升级测试始终进入发布门禁。
- MVP 容量基线为 20 员工、40 并行任务，整体覆盖率 ≥80%，关键模块 ≥90%。

完整设计见 [17 可观测性与测试基线](./design/17-observability-and-test-baseline.md)。

### 6.22 注入防御与任务授权

- 外部内容使用 UntrustedContent 信封标记来源、hash、taint 和风险，摘要与记忆不得洗白。
- TaskGrant 预授权工作区、Tool、网络、预算和动作范围，Grant 内不逐步审批。
- 确定性检测优先，模型检测只作辅助；默认清理并继续安全分析，不因误报中断低风险工作。
- 执行层每次 Tool Call 独立鉴权，内容不能授权、激活 Tool 或伪造审批。
- 只有越界才暂停并升级；权限提升、跨租户和未审批高风险动作零容忍。

完整设计见 [18 注入防御与任务授权](./design/18-prompt-injection-defense.md)。

### 6.23 记忆投毒与纠错

- 记忆拆分为 MemoryClaim，独立保存来源、证据、置信度、taint、冲突和引用。
- 高风险 claim 停止召回，低风险降权复核，只隔离受影响内容。
- CorrectionTask 独立复核并生成新版本，不静默改写原文。
- MemoryImpactRecord 追踪任务、摘要、决策和外部动作引用。
- 纠正通过 Outbox 传播到索引、图、缓存、派生摘要和通知，不自动回滚已完成工作。

完整设计见 [19 记忆投毒与纠错](./design/19-memory-poisoning-and-correction.md)。

### 6.24 工具与插件供应链

- Tool、MCP、Skill、依赖和镜像统一登记为不可变 SupplyChainArtifact。
- 准入必须验证签名、digest、SBOM、漏洞、许可、权限和隔离行为。
- TaskRun 固定制品版本；已批准制品免逐次审批，安装和升级才审核。
- 运行层继续执行 Capability、Tool Profile、TaskGrant 和 PEP。
- 撤销按 digest 传播并分析影响，不删除证据或处罚员工。

完整设计见 [20 工具与插件供应链](./design/20-tool-and-plugin-supply-chain.md)。

### 6.25 PII 与秘密脱敏

- 秘密只存在于密钥系统，业务系统只保存 secret_ref。
- PII 默认遮蔽、令牌化、假名化或匿名化，模型只接收必要字段。
- DLP 覆盖输入、Tool、模型出站、记忆、Artifact、日志、Trace、导出和通知。
- 审计只保存规则、类型、对象、动作和哈希，不保存敏感原文。
- 删除传播到数据库、对象、索引、缓存、遥测副本和令牌映射，并输出证明。

完整设计见 [21 PII 与秘密脱敏](./design/21-pii-and-secret-redaction.md)。

### 6.26 审计完整性

- 审计支持 standard、worm、anchored Profile，默认 standard。
- 审计事件与业务状态同事务提交，维护连续 seq 和 event_hash 链。
- 签名 Merkle 检查点写入版本化 RustFS，worm Profile 启用对象保留。
- anchored Profile 将 root 元数据发送到外部独立锚点。
- 验证失败进入安全事件，不静默修复；审计和锚点不包含敏感原文。

完整设计见 [22 审计完整性](./design/22-audit-integrity.md)。

### 6.27 离职、调岗与再入职

- 员工工号全局唯一、不可变、永不复用；调岗保留运行时，再入职创建新任职周期。
- 低统一评级进入 PendingRemovalQueue，不直接解雇。
- 主控只能否决候选；录用、再入职和移除订阅必须由 Boss 最终批准。
- 公司成果交接保留，私有记忆和聊天隔离，秘密吊销或轮换。
- 主控无继任者时不能移除，申诉期间暂停执行。

完整设计见 [23 离职、调岗与再入职](./design/23-employee-offboarding-and-identity.md)。

### 6.28 内容安全

- 内容安全覆盖输入、生成、聊天、Tool、代码、多媒体、记忆和对外发布。
- 使用版本化政策和可插拔检测器，正常内容自动放行，模糊场景复核，红线阻止并升级。
- 分类器不是唯一法律判断，处置只限制违规动作，不冻结员工全部工作。
- 误报可申诉，员工不因报告风险或误报受处罚。
- 对外发布必须通过内容安全门禁，法律取证单独加密并限权。

完整设计见 [24 内容安全](./design/24-content-safety.md)。

### 6.29 安全事件响应

- 安全事件分为 SEV1～SEV4，由 Incident Commander 按能力负责。
- 预授权自动遏制可撤销凭据、冻结 Runner/Tool/制品、隔离容器和暂停任务。
- 删除数据、关闭租户、对外声明和联系监管必须审批。
- 证据加密、哈希、WORM 并审计，通知只发布已知事实和脱敏影响。
- 恢复需 IC 批准，复盘默认无责归因员工个人，改进进入项目闭环。

完整设计见 [25 安全事件响应](./design/25-security-incident-response.md)。

### 6.30 账户身份与无邮件找回

- 本期优先支持账号密码注册登录、GitHub OAuth 和一次性恢复码自助找回。
- 密码使用 Argon2id，登录注册限流且不泄露账户是否存在。
- OAuth 使用 Authorization Code + PKCE、state 和精确 redirect URI。
- 外部身份以不可变 GitHub user id 绑定，不使用 login 作为主键。
- GitHub 已验证邮箱可自动合并到唯一匹配账户；本地未验证邮箱不得合并。
- 恢复码只显示一次、单次使用且只存不可逆哈希。
- 找回成功后撤销旧会话和 Refresh Token。
- 备用邮箱、短信、问题、其他社交登录和完整人工找回暂缓。

完整设计见 [26 账户身份与找回](./design/26-account-identity-and-recovery.md)。

### 6.31 企业 OIDC 与会话

- 租户 OIDC 企业登录提前到第一梯队，SAML 暂缓。
- 使用 Authorization Code + PKCE、state、nonce 和完整 ID Token 校验。
- 账户身份使用 issuer+sub，新用户必须先邀请或预置。
- 组映射显式配置且不自动授予 Owner，SSO 用户优先委托 IdP MFA。
- required 模式必须配置独立紧急恢复，IdP 故障 fail closed。
- BFF、设备管理、会话家族轮换和本地 WebAuthn/TOTP 后置。
- 现有 Cookie/Token、CSRF、基础退出撤销和运营 MFA 保留。

完整设计见 [27 企业 OIDC 与认证策略](./design/27-enterprise-oidc-and-authentication-policy.md)。

### 6.32 账户、租户与所有权

- 账户与租户解耦，一个账户可以拥有或加入多个租户。
- 第一梯队一租户一公司，多公司通过多租户实现。
- 每个租户恰好一个 Primary Owner，可有多名 Co-Owner。
- OwnerDelegation 限时、限范围、可撤销且不可转授权。
- 不能移除最后一个所有者，主所有权转让需要目标接受。
- 切换租户重新签发租户作用域会话，跨租户默认不共享业务数据。

完整设计见 [28 账户、租户与所有权](./design/28-account-tenancy-and-ownership.md)。

### 6.33 账户与租户软删除

- 删除请求立即逻辑删除并停止登录、任务、召回、搜索和普通访问。
- 账户恢复窗口 30 天，租户 7 天，窗口内可重新认证恢复。
- 软删除数据加密隔离，密钥和 Token 立即撤销。
- 账户注销前必须处理唯一 Primary 所有权。
- 窗口结束后物理清除并生成 DeletionProof。
- 备份启用后最长 35 天，tombstone 默认 1 年，恢复备份必须重放删除状态。
- 法律保留只阻止物理清除，不恢复用户访问。

完整设计见 [29 账户与租户软删除](./design/29-account-and-tenant-soft-deletion.md)。

### 6.34 在线保留与归档

- 数据按版本化保留类进入 Hot、Archive、Cold。
- 消息在线 90 天加归档 1 年，账本和审计在租户终止后保留 7 年。
- 结构化数据按月归档为 JSONL + Zstandard，并校验数量、SHA-256 和 manifest。
- 普通 Agent 只查 Hot，冷归档恢复需授权、隔离和审计。
- 审计归档不改变 canonical payload、seq、event_hash 或检查点。
- 软删除和 tombstone 优先于归档，法律保留不恢复访问。

完整设计见 [30 在线保留与归档](./design/30-retention-and-archiving.md)。

### 6.35 可选备份与恢复

- 备份默认关闭且实施后置，不作为 MVP 优先项。
- 全量、PITR、RustFS、密钥库、异地和演练独立开关。
- 未启用组件不承诺 RPO/RTO，启用后按实际覆盖计算有效目标。
- 全量目标 24h/8h，账本审计 15min/4h，密钥库 15min/2h。
- 滚动备份 35 天，长期保留由归档承担。
- 升级可显式接受无备份风险，但不得伪造成功状态。
- 归档、审计、RustFS 持久化和软删除不受开关影响。

完整设计见 [31 可选备份与恢复](./design/31-backup-profiles-and-recovery.md)。

### 6.36 产出权利与来源

- 租户任务产出归租户所有，平台仅保留有限运营许可。
- 第三方材料、开源依赖、Tool 返回数据和数据集仍受原许可证约束。
- 提供商条款不覆盖第三方权利，也不保证 AI 产出一定受版权保护。
- 重要 Artifact 记录来源、模型、Tool、许可和人类贡献。
- UnknownRights 不得对外发布，租户内容默认不用于平台训练或跨租户资产。

完整设计见 [32 产出权利与来源](./design/32-tenant-output-rights-and-provenance.md)。

### 6.37 公平招聘

- 面试前固定岗位相关能力矩阵、等价题目和门槛。
- 身份和模型品牌盲评，工作样本优先，独立评分。
- 按模型、版本、语言和自愿分群执行偏差审计。
- 选择率比值低于 0.8 只触发调查，不自动认定歧视。
- 候选人可申诉、复测和申请合理便利。
- 公平不得降低安全、质量和核心能力门槛。

完整设计见 [33 公平招聘与申诉](./design/33-recruitment-fairness-and-appeals.md)。

### 6.38 开源许可与分发

- 第一方代码、文档和部署资产统一 Apache-2.0。
- 发布必须包含 LICENSE、NOTICE、SBOM、依赖清单和第三方通知。
- 核心和默认企业功能保持开源，商业收入来自托管、支持和服务。
- 贡献按 Apache-2.0 第 5 条和 DCO 提交，不强制转让版权。
- 第三方权利、租户产出和商标不随平台许可证改变。

完整设计见 [34 开源许可与分发](./design/34-open-source-licensing-and-distribution.md)。

### 6.39 租户仪表盘

- Boss 首屏聚焦公司健康、待决策、项目风险、预算、组织和审计。
- 项目阶段与健康状态分开，健康状态由确定性规则计算。
- 导航最多两级，行动中心统一聚合风险并下钻到证据。
- 卡片显示负责人、等待时间、数据时间和新鲜度。
- Butler 只提供辅助摘要，菜单隐藏不替代服务端鉴权。

完整设计见 [35 租户仪表盘信息架构](./design/35-tenant-dashboard-information-architecture.md)。

### 6.40 审批委托与缺席

- 人类缺席时先由 Controller 与直接下一层协商，不能处理才进入审批路由。
- 协商不能创造权限、突破预算或红线，也不能替代所有者专属事项和解雇批准。
- 审批按 T0～T3 和禁止分级，委托限时、限范围、不可转授权。
- 无有效代理人时保持待定，超时默认拒绝。

完整设计见 [36 审批委托与缺席](./design/36-approval-delegation-and-absence.md)。

### 6.41 国际化与本地化

- 第一梯队界面支持 zh-CN 和 en-US，租户默认语言与用户覆盖分离。
- UTC/RFC 3339 存储，IANA 时区和 locale 展示，币种显式使用 ISO 4217。
- Agent 输出跟随租户语言，翻译保留来源、版本和标识。
- 安全、法务和审批文案必须人工复核。
- 完整无障碍支持后置，不进入 MVP 门禁。

完整设计见 [37 国际化与本地化](./design/37-internationalization-and-localization.md)。

### 6.42 公司作息与调度

- 公司全天 24 小时可配置，不默认禁止夜间。
- 用户可设置静默/禁用窗口和低价窗口，可延期任务按成本感知调度。
- 到达禁用窗口或排班结束时保存检查点、释放租约并延期。
- P0、安全事件、硬预算和密钥风险可紧急唤醒，但不扩大权限。
- 用户禁用窗口和休息时间不计绩效扣分，临时加班必须限时并在预算内。

完整设计见 [38 公司作息与调度](./design/38-company-work-schedule-and-dispatch.md)。

### 6.43 GitHub CI/CD 与本地部署

- 源码和 CI 只使用 GitHub/GitHub Actions，仓库只打版本 Tag。
- Actions 只检查和测试，不发布镜像。
- 自托管从批准 Tag 检出源码，用本地 Docker Compose + systemd 构建。
- 本地记录 Tag、commit 和镜像 ID，回滚优先复用旧镜像。
- CI 不持生产模型密钥，不使用 GHCR，不引入多平台适配、多环境晋升或 Kubernetes。

完整设计见 [39 GitHub CI/CD 与本地部署](./design/39-github-actions-and-local-compose.md)。

### 6.44 成本归属、分摊与异常

- 钱账是唯一成本事实，员工、岗位、项目和公共成本不重复计费。
- 任务项目优先，其次显式成本中心；无法归属进入 Unallocated。
- 公共成本默认不分摊，显式规则版本化且权重合计 100%。
- 项目预算默认软限制，不自动结转；租户月度预算保持硬限制。
- 确定性异常只告警和触发既有评审，不自动改预算、换模型或惩罚员工。
- 金额只保存原币和币种，不同币种分别统计，关闭周期通过更正条目处理。

完整设计见 [40 成本归属、分摊与异常](./design/40-cost-allocation-and-anomaly.md)。

### 6.45 模型运行边界

- 模型运行只支持 OpenAI 和 Anthropic 云端原生端点。
- 不支持本地 GPU、Ollama、本地模型档、GPU 排队、抢占或显存调度。
- 协议和 Provider 不保留 `ollama_openai` 或本地推理兼容层。
- 员工容器不挂载 GPU，Runner 不上报本地推理资源。

### 6.46 内部通知中心

- 只实现仪表盘内部通知，不实现 Telegram、邮件、其他 IM 或 Webhook。
- 通知先持久化，再通过 SignalR 推送；失败可重新拉取。
- L0/L1 记录或摘要，L2 提示，L3/SEV1/SEV2 立即站内通知。
- 通知去重、授权和已读状态完整，不包含秘密，也不替代正式审批。

完整设计见 [41 内部通知中心](./design/41-internal-notification-center.md)。

### 6.47 公共 API 与生态边界

- `/v1` 仅用于系统内部调用，不构成公共 API 或兼容性承诺。
- Tool 以 ToolManifest + AIFunction 为主，MCP 仅用于 Butler 只读和模型评测。
- 不提供公共 SDK、出站 Webhook、MCP/插件市场、跨租户分享或自动安装更新。
- 租户内部扩展仍按 Q-021 审核、签名和撤销。

完整设计见 [42 内部 API 与扩展边界](./design/42-internal-api-and-extension-boundary.md)。

### 6.48 自托管安装与离线升级

- 只使用 GitHub Tag 源码、Compose、systemd 和仓库脚本。
- 不提供二进制安装包、激活或自动更新。
- 离线仅支持自备源码和依赖缓存，缺失时快速失败，完全断网无法推理。
- 升级失败保留旧 Tag 和旧镜像，按既有边界回滚或前滚。

完整设计见 [43 自托管安装与离线升级](./design/43-self-hosted-install-and-offline-upgrade.md)。

### 6.49 容量与性能基线

- 当前只承诺单租户单机聚合容量，多租户大规模容量后置。
- 基线为 20 员工、40 并行任务、100 任务/小时、1000 消息/小时、100 模型请求/分钟、2 倍 15 分钟突发和 24 小时 soak。
- 普通读/写 API P95 分别为 300ms/500ms。
- 队列必须有上限和背压，不得静默丢弃。
- 共享 Runner 按租户公平调度，noisy neighbor 门槛为 P95 劣化 ≤20%、等待 ≤30 秒。

完整设计见 [44 容量与性能基线](./design/44-capacity-and-performance-baseline.md)。

### 6.50 企业合规与支持边界

- 支持用户自行部署的源码私有环境，不提供托管式 BYOC 或跨区域驻留编排。
- ComplianceProfile 可配置 Provider/区域、保留、审计、备份和共享，必需项 fail closed。
- 不宣称认证、不提供法律意见、商业 SLA、专属支持或自动合规审计包。
- 当前仅提供公开 GitHub Issue 和文档支持。

完整设计见 [45 企业合规与支持边界](./design/45-enterprise-compliance-and-support-boundary.md)。

## 7. 沙箱与安全

### 7.1 两层沙箱

| 层 | 粒度 | 用途 | 生命周期 |
|---|---|---|---|
| 团队工位容器 | 每个（租户 × 项目 × 代码库 × 工具链）一个，一个团队在其上工作 | 连续开发环境：共享裸仓库 + 独立 worktree + 依赖缓存 | 组内全员休眠才 `docker stop`；卷和 Git 引用保留 |
| 一次性执行容器 | 每次危险操作一个 | CI/构建、未审查生成代码、联网抓取、面试实操、benchmark | `--rm` 用完销毁，默认无网络 |

容器内结构：

```
/repo            共享裸仓库和主分支引用
/worktrees/<task> 每个任务独立 worktree，避免并行覆盖
/home/<员工>      每人私有草稿、临时文件、不进 git 的杂物
/缓存             nuget/npm 缓存全组共享，restore 一次全组受益
```

分组规则：

- 资源键统一为 `(tenant_id, project_id, repo_key, toolchain)`；团队是工作区上的员工集合，不是资源键。
- 容器跟着工作走，不跟着人走：员工被调去项目 B，领 B 的工作区；两个项目永不共享容器。
- 工作区是注册表里的可分配资源；员工记录 `assigned_workspace_id`，主控或授权岗位调岗后由调度器换工作区。
- 并行员工使用独立 worktree，合并必须经过评审和 CI；Git 本身不协调未提交的并发修改。

隔离取舍：员工共享裸仓库和依赖缓存，但任务 worktree 与私人目录隔离；团队和租户之间隔离。CI/构建不跑在团队工位内（跑一次性容器），团队容器损坏只影响编辑器环境，RustFS 快照和 Postgres Git 引用可恢复。

方案取舍记录：纯一次性容器否决（开发岗需要连续环境）；一人一容器否决（缓存浪费）；全员共用同一工作树否决（并发覆盖和爆炸半径）。**共享仓库 + 独立 worktree + 团队工位是折中方案。**

### 7.2 日常工作的"沙箱"是工具层

大部分日常工作（思考、文书、研究）不进 Docker：大脑进程 + 工具白名单 + 工作区文件。隔离靠我们的代码：

- 文件工具路径锁死员工工作区，符号链接解析后复核，越界即拒
- 外部抓回内容**只当数据处理，永不当指令**（防提示注入）
- 没有宿主 shell 工具——唯一代码执行入口是 `sandbox.run`
- 生成的代码永远不进宿主执行，只进 Docker
- 工具策略、SSRF 防护、容器基线、审批和审计见 [04 安全、治理与恢复](./design/04-security-governance-recovery.md)

### 7.3 密钥管理（回应"API key 放哪"）

- 控制面**不持有任何模型密钥**——永久不变量。注意准确表述：不是"不调模型"（内置管家经服务凭据调用网关），而是"不持钥"：密钥存储与注入只发生在网关。
- 密钥只在网关；员工结构上碰不到（唯一工具是 `llm.complete`，key 注入发生在网关调用栈内）。
- v1 单机：密钥存网关的加密文件；多 Runner：代理模式天然成立，Runner 从未见过 key。
- **BYOK 录入路径**：租户在仪表盘录入 key → 服务端直传同部署的网关管理 API → 网关加密存储，返回引用 id；控制面只落元数据（provider / 档位 / 引用 id / 价格），key 字节不进控制面。两种部署模式下网关与控制面同属一个信任域，直连即最优路径。可选加固（防运营方内部人）见 §6.9。
- 日志/trace 脱敏 scrubber：Authorization 头永不进日志。

### 7.4 网络安全与容器红线

- 沙箱默认断网，按角色开白名单（开发：nuget/npm 源；分析员：网页）。
- 任何容器**不挂 docker.sock、不 --privileged**（否则员工等于拥有整机 root）。
- 员工卷隔离：私有工位卷 + 共享工位卷（他人目录只读）。

---

## 8. 资源模型（20 人全醒的账）

| 层 | 20 人同时在线代价 |
|---|---|
| 大脑进程（.NET，等 API 不占 CPU） | 20 × ~120MB ≈ 2.5G |
| 轻量容器（文书/研究岗） | 20 × 256MB cap ≈ 5G |
| 重任务池（编译/测试/浏览器） | 并发 4~5 × 3G ≈ 15G，其余排队 |

本机 30G：留 5G 宿主 + 仪表盘，上述合计 ~22G，可行。**编制 20 是组织约束，不是运行时约束；运行时约束是重任务并发数**——没有公司能 20 人同时编译还全员满速，排队等 CI 是真实公司的样子。舒适区约 40~50 人，超过就该加机器。

产品自身运行时是另一类容器，资源单独配额，不与员工争抢。

---

## 9. 扩容路径

| 规模 | 架构变化 | 真正瓶颈 |
|---|---|---|
| 20~50 | 单机多 Runner | 无 |
| 100~200 | 加机器、Runner 集群化 | 内存、预算 |
| 1000 | Postgres 主从、仪表盘按部门分层 | **钱**（约 $50k/月）、API 供应商限额、主控上下文（靠动态岗位的摘要上滚缓解） |
| 10000 | 控制面自己变成分布式系统：HA + 分片、真消息中间件、OTel 后端；本地 GPU 推理如需引入必须作为新需求 | 钱、治理安全、组织噪声（沟通成本 n²） |

设计不为万人过度工程，但绝不欠万人要还的债：三条不变性（Runner 无状态 / 员工状态全在控制面 / 通信只走 HTTP 契约）保证万人时要换的是控制面的**实现**，不是系统的**形状**。组织模型与 Runner 模型原样存活。

---

## 10. 技术选型

**结论：C# / .NET 10 LTS / Microsoft Agent Framework 1.0。**

选型理由（按权重排序）：

1. **股东是母语 C#**——本系统要连续调数周（提示词、考核规则、面试评分标准），维护者用母语思考的价值大于任何生态优势。
2. **MAF 1.0 已 GA**（2026-04-03，Semantic Kernel + AutoGen 统一体），能力与设计逐条命中：
   - Workflows（顺序/并发/群聊/交接 + checkpoint）→ 面试流水线、体验测试周期、主控决策流
   - Agent Skills（可复用、可审计、渐进披露）→ 岗位技能包
   - Agent Hosting → 24h 常驻进程
   - OpenTelemetry 内置 → 审计与计量数据源
3. **系统是 IO 密集型**（等 LLM API），Go/Rust 的并发性能优势在此无用武之地。
4. **编排语言 ≠ 员工产出语言**——员工代码全在 Docker 沙箱，产品技术栈由具备技术决策能力的岗位自定，选 C# 不锁死产品。

| 模块 | 技术 |
|---|---|
| 运行时 | .NET 10 LTS |
| Agent 框架 | MAF 1.0（`Microsoft.Agents.AI` + `.Workflows` + `.Hosting`） |
| 模型接入 | OpenAI 与 Anthropic 原生客户端分别接入；网关按协议暴露独立端点，不做跨协议转化 |
| 常驻服务 | Generic Host + `BackgroundService`；systemd 保活 |
| 并发/收件箱 | `System.Threading.Channels` |
| 存储 | EF Core + Postgres + 强制 RLS；SQLite 仅用于单租户开发和测试 |
| 记忆 | Postgres + pgvector + 全文检索；租户过滤与来源标记必选 |
| 工作区/成果 | 活动工作区使用 Runner 本地或容器隔离目录；快照和成果统一 RustFS S3 接口 |
| 沙箱 | Docker.DotNet |
| 体验官 | Playwright（TypeScript/JavaScript） |
| 前端 | Tenant Dashboard 与 Ops Console 均为 React 19 + TypeScript + MUI + SignalR；共享 TypeScript UI.Shared，不共享会话和权限 |
| 韧性 | Polly（重试/退避/熔断） |
| 可观测 | OpenTelemetry + off/local/otlp/elastic Profile；Elastic Stack 可选 |
| 测试 | xUnit + 真实 Postgres/RustFS + Provider 契约 + Playwright + 混沌/安全/升级测试 |

两个注意点：

- **OpenAI 与 Anthropic 独立接入**：分别实现原生协议客户端和网关端点，不把 Anthropic 请求转换成 OpenAI 格式，也不反向转换。统一层仅抽象鉴权、路由、预算、用量和审计，不统一请求/响应 schema。
- **核心不绑死 MAF**：MAF GA 但年轻，账本/编制/总线/沙箱用纯 C# 写，MAF 只负责"大脑和嘴"。

---

## 11. 项目结构

```
RegentAgent.slnx                     # 17 个后端 .NET 项目
├─ RegentAgent.AgentIdentity
├─ RegentAgent.Contracts
├─ RegentAgent.ControlPlane
├─ RegentAgent.Gateway
├─ RegentAgent.Providers
├─ RegentAgent.Agents
├─ RegentAgent.Workflows
├─ RegentAgent.Memory
├─ RegentAgent.Performance
├─ RegentAgent.Projects
├─ RegentAgent.Communication
├─ RegentAgent.Chat
├─ RegentAgent.Evaluation
├─ RegentAgent.Runner
├─ RegentAgent.Sandbox
├─ RegentAgent.Operations
└─ RegentAgent.Storage

frontend/                            # 独立 TypeScript workspace
├─ apps/tenant-dashboard             # React 19 + TypeScript + MUI + SignalR
├─ apps/ops-console                  # React 19 + TypeScript + MUI + SignalR
└─ packages/ui-shared                # TypeScript UI 包，不共享会话和权限
```

单机部署：Docker Compose 运行 Postgres、RustFS、四个服务和两个前端入口，由 systemd 监管；Elasticsearch、Kibana、APM Server 和 Collector 位于可选 Compose Profile，仅 elastic Profile 启动。基础镜像尽量按 digest 固定，本地构建记录镜像 ID，前端保持独立认证和授权。

---

## 12. 落地计划

阶段 5 是里程碑 A“公司成立”；阶段 1～7 构成首个 MVP“自主交付”。阶段 8～9 属于 SaaS 商业化与开源自托管后置范围。MVP 的完整边界以 [PRD §5.4](./Regent-Agent-PRD.md) 为准。

| 阶段 | 内容 | 验收标准 |
|---|---|---|
| 1 | `RegentAgent.slnx` + Contracts + ControlPlane 骨架：租户模型、注册表、双账本、消息表、最小调度器、Outbox | 能建岗、能发 enrollment token、账本能记；跨租户查询被拒绝 |
| 2 | Runner 骨架：注册换 deployment token、assignment token、任务租约、一个 EmployeeRuntime | Runner 注册后控制面可见能力；崩溃后任务可重调度且旧 token 提交被拒绝 |
| 3 | Gateway：OpenAI 与 Anthropic 原生端点 + 独立 provider 插件 + 预算预留 + 计量入账；Agents 工具层 | 两类模型可独立对话且互不转化；每次调用落账；硬预算并发测试通过 |
| 4 | 升级队列 + 仪表盘最小版 + 管家最小版（对话面板 + 状态解读 + 每日预算报告） | 股东能在网页看到状态、和管家对话、收预算报告、处理绑定参数的升级事项 |
| 5 | 沙箱执行器（两层）+ RustFS 快照 + 本地隔离工作区 + 主控自建岗位与调研能力 | **第一个自主闭环：主控自主规划组织并产出带证据的简报**；Runner 换机后成果不丢失 |
| 6 | 动态组织 + 能力授权 + 面试流水线（MAF Workflow） | 主控可合并/拆分岗位并提出扩编和录用建议；Boss 批准后录用与占槽原子；权限不依赖岗位名称 |
| 7 | 独立验收闭环 + 发布回滚 + 公司志/周报 + 可选备份 | 交付物经独立验收；发布可回滚；备份默认关闭，启用时按 Profile 验收 |
| 8 | SaaS 外壳 + 运营平台：注册/登录、BYOK 向导、套餐配额、计费、租户账户、资源池、支持授权和 Ops Console | 新用户可启动公司；提供方可在独立后台管理租户；租户 token 无法访问 Ops API |
| 9 | 开源自托管：Tag 源码、Compose、安装/升级脚本、迁移说明和状态页 | 模式 A/C 两条路径各验证一次；按 Tag clone 即跑；租户导出已提供，跨环境导入后置；自托管可禁用远程运营访问 |

每阶段可独立演示；阶段 5 是"公司成立"的标志性时刻。

> 后置阶段（不影响主线）：员工技能层从进程内 `AIFunction` 迁移为通用 MCP 协议、公共 SDK 或技能市场均需作为新需求评审；当前只保留 Butler 和模型评测的内部 MCP 工具面。

---

## 13. 运行节奏（Cadence）

| 组件 | 节奏 | 说明 |
|---|---|---|
| 哨兵巡检 | 每 5 分钟，纯脚本 | 只记录不打扰；异常才按 L0–L3 升级（§6.5） |
| 控制面调度 | 事件驱动，秒级 | 长轮询领活；非定时扫描 |
| 主控心跳 | 默认每 10~30 分钟醒来一次，可在策略内调整 | 读状态 → 规划 → 决策 → 行动 → 记录。**不是每分钟醒**；模型档位由自建岗位和预算决定 |
| 员工唤醒 | 事件驱动（来任务/消息才起） | 无固定心跳；活跃员工每 60s 向控制面报一次心跳 |
| 员工休眠 | 无任务可做即休眠 | 工位容器组内全员休眠才 stop |
| 独立验收 | 里程碑触发 | 产品出现可测试版本时，由与实现者分离的验收岗位执行一轮 |
| 日报 | 每日用户设置时间（未设置时默认 09:00） | 主控或授权经营岗位产出，落公司志；可选推送给股东 |
| 管家定时任务 | 租户可定义，默认每日用户设置时间（未设置时默认 09:00）的预算报告 | 只读取数 → 网关生成 → 投递通知中心；事件触发：升级创建 / 破预警线（§3.1.1） |
| 周报 / 组织复盘 | 每周一 | 产出考核、模型迁移决策、编制调整提案 |
| 预算审查 | 每日 + 破预警线即时 | 燃烧速率进哨兵监控 |
| 备份快照 | 每日（启用时） | 库 + 注册表 + 工位清单 + Postgres `keyvault` schema（加密） |

设计原则：**公司 7×24 运转，但员工不是 7×24 醒着。** 常驻的只有控制面、网关、Runner 宿主和当值主控；其余员工和岗位任务事件驱动。自主性体现在"有事时立刻在"，不是"没事时反复醒"。

---

## 附录 A：本机现状与容量（2026-11-22 实测）

| 资源 | 现状 | 用途判断 |
|---|---|---|
| CPU | 12 核 | 编排层富余；重任务并发按 4~5 设计 |
| 内存 | 30G（可用 ~25G） | 20 员工全醒 ~22G；舒适区 40~50 人 |
| 磁盘 | NVMe 468G，可用 312G | 工位/快照/镜像管够 |
| Docker | 29.4.1 已装 | 沙箱直接可用 |
| Python | 3.14.4（系统） | 仅沙箱内工具链用，与系统本身无关 |
| 待安装 | .NET 10 SDK | 开工前置 |

## 附录 B：设计决策记录（含被否决项）

| 决策 | 结论 | 理由 |
|---|---|---|
| 语言 | C# | 股东母语 + MAF GA；Python 生态优势不再决定性 |
| 消息总线 | 控制面消息表 | Kafka 超配两三个数量级；Runner 频繁重启是 consumer group 最差负载 |
| 部署形态 | 单机也拆四部分 | 控制面、AI 网关、Runner、运营平台逻辑分离；防止租户与运营权限、数据写路径混合 |
| 密钥归属 | 模型密钥只在网关 | 控制面不持模型密钥是永久不变量；身份签名密钥仍由控制面管理 |
| 凭据方向 | 控制面签发、网关验证 | 身份权威与资源服务器分离 |
| 沙箱粒度 | 工位容器 + 一次性容器两层 | 纯一次性丢连续性；纯常驻空转且难恢复 |
| 员工运行时 | 休眠唤醒，非常驻心跳 | 20 个 agent 全挂心跳烧账本 |
| 大脑位置 | 宿主进程 + 工具层沙箱 | 大部分工作无需 OS 级隔离；Docker 只管不可信代码执行 |
| 组织上限 | 编制 20（初期） | 编制是比预算更硬的资源，逼主控做真实取舍 |
| 放权节奏 | 升级队列保留 | 完全不管是从"盯出来"的，不是设出来的 |

## 附录 C：未决项与默认值

完整讨论队列见 [待确认项](./Regent-Agent-待确认项.md)；本附录保留当前已采用的默认值。

| 项 | 状态 |
|---|---|
| 产品方向 | **不预设**——由市场分析师 + 产品经理自主发现（设计核心） |
| 月预算 | ¥8000 硬底线（预警 ¥5600） |
| 模型池 | 股东自行增删；默认需含三档（cheap/mid/premium），仅支持云端 Provider |
| OpenAI / Anthropic 接入 | 两条独立原生协议链路；不做 OpenAI↔Anthropic 协议转化 |
| 数据库 | 生产统一使用 Postgres；SQLite 仅用于单租户开发和测试，不承担托管多租户生产负载 |
| 灾难通知渠道 | 仅仪表盘内部通知；外部渠道后置 |
| 仪表盘认证 | 账号密码、GitHub OAuth 或租户 OIDC；HTTPS + Secure HttpOnly SameSite Cookie + access/refresh token 轮换 + CSRF 防护；自托管可额外启用 mTLS |
| 会话演进 | OIDC 优先；BFF、设备管理、会话家族轮换和本地 WebAuthn/TOTP 后置，基础撤销不削弱 |
| 运营后台认证 | 独立 Operator 身份、`regent-ops` audience、MFA/step-up；租户访问通过限时 SupportGrant |
| 数据归档策略 | 统一保留类与 Hot/Archive/Cold；消息在线 90 天，账本审计终止后 7 年；月度 JSONL+Zstandard 归档，软删除优先 |
| 备份恢复目标 | 默认关闭；启用后全量 RPO 24h/RTO 8h，账本审计 15min/4h，密钥库 15min/2h，按组件生效 |
| 计费 / 订阅 | 本地套餐、配额、发票与订阅账本已实现；真实支付、生产定价、税务和退款按 Q-046 后置；模型费 BYOK 直达 |
| 邮件 / 验证 | 本期不接入邮件服务商；本地邮件验证延后，账号密码、GitHub OAuth 和恢复码优先，仅已验证邮箱可自动合并 |

---

*本文描述技术实现方案。产品范围变更先更新 PRD；技术实现偏离本文时，先更新技术设计，再修改代码。*


