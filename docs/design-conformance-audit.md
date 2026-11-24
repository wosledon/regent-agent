# 设计一致性审计

- 审计日期：2026-11-23
- 范围：`docs/design/01`～`50` 与已接受 ADR
- 基线提交：`1dda65f`
- 口径：`符合（本地）` 表示当前本地模式有实现与自动化证据；`符合（适配）` 表示远程端口、协议和失败语义已实现但未连接真实组件；`部分（生产）` 表示本地闭环成立但生产设计仍有明确缺口。本文不把本地实现表述为生产就绪。

## 1. 本轮修复的设计偏差

| 偏差 | 修复 | 证据 |
|---|---|---|
| models.dev 刷新被手工价格反向覆盖，失败时不标记陈旧 | 目录字段优先、手工值仅补缺；失败保留最近快照并标记 `Stale` | `RuntimePolicyServiceTests` |
| 外部身份允许匿名 JIT 入租户，未持久化 subject 绑定 | 增加 state/PKCE/nonce 事务、code 交换、邀请门禁和 provider+issuer+subject 绑定 | `IdentityServiceTests`、`ExternalIdentityProviderTests` |
| 本地密码使用 PBKDF2 | 新凭据使用 Argon2id，旧凭据成功登录后升级 | `IdentityServiceTests` |
| Provider 只支持非流式响应 | 增加原生 SSE 透传、有界读取和流式用量提取，不跨协议转换 | `HttpProviderClientTests` |
| 生产启动自动建表/种子，缺少 RLS 启动边界 | 生产只校验 SchemaVersion 与强制 RLS，不执行自动迁移或种子 | `SchemaVersionServiceTests`、`PostgresRlsValidator` |
| remote Gateway 与生产 Runner 内部端点可匿名调用 | 缺少服务/管理员/Runner 凭据时启动失败，远程请求鉴权 | 组合根校验与中间件 |
| 审计锚定硬编码本地文件 | `IAuditAnchorStore` 支持本地和对象存储，按租户/序号验证 | `AuditAnchorStoreTests` |
| KeyVault 只有本地文件 | 增加 Postgres `keyvault` 信封加密适配、RLS SQL 和 TLS 校验 | `PostgresKeyVaultOptionsTests` |
| 可观测性只有 Exporter 开关 | 增加严格脱敏处理器、ASP.NET Core/HTTP 仪表和导出超时 | `ObservabilityRedactionTests` |
| Compose 硬编码 Postgres，备份只能本地 | 默认 SQLite/本地，组件 Profile 显式启用；备份支持可选 S3 上传/恢复 | YAML 解析、本地备份恢复 round trip |

## 2. 逐模块矩阵

| 设计 | 主要实现证据 | 结论 | 剩余边界 |
|---|---|---|---|
| 01 基础契约与状态 | Cookie 控制面 + Agent Assignment Token、租户上下文、Outbox、幂等服务 | 部分（生产） | 生产 JWKS 轮换与完整 Token 吊销传播尚未实现 |
| 02 运行时可靠性 | `TaskExecutionService`、租约/fencing、Outbox/Inbox、本地沙箱 | 符合（本地） | 真实多 Worker 故障演练未执行 |
| 03 模型、计量与预算 | Provider 路由、原生 HTTP/SSE、Agent JWT、KeyVault、租户预算与用量归因 | 部分（生产） | 预算仍为本地令牌计数，未完成金额预留/结算与 Provider 账单对账 |
| 04 安全、治理与恢复 | Tool PEP、审批、审计、DLP、事件响应、备份脚本 | 部分（生产） | 容器隔离、真实 WORM 与异地恢复按约束未执行 |
| 05 产品交付闭环 | `DeliveryPolicy`、`DeliveryService`、独立验收、发布/回滚 | 符合（本地） | 容器发布未执行 |
| 06 主控与动态组织 | `OrganizationService`、能力授权、组织调整、继任 | 符合（本地） | 无 |
| 07 运营平台 | Operations、租户启停、SupportGrant、状态页、运营审计 | 符合（本地） | 托管计费与外部 SLA 不在范围 |
| 08 工作流系统 | `WorkflowService`、版本、向上提名、独立审查、执行门禁 | 符合（本地） | 无 |
| 09 记忆系统 | 混合检索、图谱、ACL、衰减、归档、删除传播 | 符合（本地） | 生产 pgvector 未连接 |
| 10 绩效与自我改进 | 改进审查、KPI、绩效动作、申诉反转 | 符合（本地） | 无 |
| 11 项目管理 | 需求/任务/缺陷状态机、自动认领、评审、裁决 | 符合（本地） | 无 |
| 12 通信与路由 | `CommunicationRouter`、管理链/专业链、项目覆盖 | 符合（本地） | 无 |
| 13 Agent 聊天协作 | Agent Token、群成员、回复/@、证据、只读可视化追溯 | 符合（本地） | 通用 Tool 调用与完整委派/交接 UI 仍未实现 |
| 14 模型评测回归 | 版本化评测、密封/污染检测、回归计划 | 符合（本地） | 真实模型回归未执行 |
| 15 数据 Schema 与事务 | SQLite/JSON/对象存储、Outbox、启动 SchemaVersion | 部分（生产） | 尚缺完整 Postgres 基线迁移和全部租户表 RLS SQL |
| 16 部署升级迁移 | Compose Profile、生产启动校验、版本脚本 | 部分（生产） | Expand/Contract 与混版本回滚未在容器环境演练 |
| 17 可观测性与测试 | 四服务 OTel、严格脱敏、仪表、测试基线 | 部分（生产） | 两个前端遥测、运行时 SLO/错误预算未实现 |
| 18 注入防御 | taint、TaskGrant、注入扫描、执行层鉴权 | 符合（本地） | 无 |
| 19 记忆投毒纠错 | Finding、隔离、申诉、独立纠错、影响传播 | 符合（本地） | 无 |
| 20 工具与插件供应链 | 签名、SBOM、准入、撤销、影响记录 | 部分（生产） | 自动漏洞扫描与完整 LicenseInventory 未接入 |
| 21 PII 与秘密脱敏 | DLP、令牌化、秘密隔离、删除传播 | 符合（本地） | 无 |
| 22 审计完整性 | 哈希链、Merkle、可插拔锚定、验证 API | 符合（适配） | 真实外部 WORM 未连接 |
| 23 离职调岗再入职 | 永久工号、任职周期、Boss 审批、私有记忆隔离 | 符合（本地） | 无 |
| 24 内容安全 | 版本化策略、分级处置、复核、申诉 | 符合（本地） | 无 |
| 25 安全事件响应 | SEV 分级、遏制、证据、通知、恢复、复盘 | 符合（本地） | 无 |
| 26 账户身份与找回 | Argon2id、恢复码、会话撤销、GitHub 已验证邮箱 | 符合（本地） | 邮件/短信与人工找回明确后置 |
| 27 企业 OIDC | discovery/JWKS、code+PKCE、nonce、邀请、subject 绑定 | 部分（生产） | required SSO、组策略、IdP MFA 与紧急恢复未实现 |
| 28 账户租户所有权 | 多租户成员、Co-Owner、Primary Owner、限时委托 | 符合（本地） | 无 |
| 29 软删除 | 软删除/恢复、 tombstone、物理清除证明 | 符合（本地） | 无 |
| 30 保留归档 | 保留策略、法律保留、归档校验、授权恢复 | 符合（本地） | 真实冷存储未连接 |
| 31 备份恢复 | 默认关闭、manifest/checksum、本地/S3 目标 | 部分（生产） | 尚未与 BackupProfile 作业、有效 RPO/RTO 和恢复审批编排集成 |
| 32 产出权利来源 | 权利声明、来源、第三方通知、发布门禁 | 符合（本地） | 无 |
| 33 公平招聘 | JD、盲评、公平审计、候选人申诉 | 符合（本地） | 无 |
| 34 开源许可分发 | Apache-2.0、NOTICE、DCO、SBOM 文件 | 符合（本地） | 自动许可门禁未接入 CI |
| 35 仪表盘 IA | Boss 首屏、确定性健康、行动中心、组织协作四视图与追溯 | 符合（本地） | 无 |
| 36 审批委托缺席 | 分级、协商、委托、SLA、超时拒绝 | 符合（本地） | 无 |
| 37 国际化本地化 | zh-CN/en-US、IANA 时区、ISO 币种、翻译回退 | 部分（生产） | WCAG 2.2 AA 按 G-52 明确后置 |
| 38 作息调度 | 工作窗口、低价窗口、假期、紧急唤醒 | 符合（本地） | 无 |
| 39 CI/CD 与 Compose | GitHub Actions、Tag、Compose Profile、校验脚本 | 部分（生产） | 按用户约束未创建或测试容器 |
| 40 成本归属分摊 | 原币账本、幂等、分摊、异常、项目预算 | 符合（本地） | 不做汇率换算 |
| 41 内部通知 | 持久通知、分级、去重、SignalR 增强 | 符合（本地） | 仅站内 |
| 42 内部 API 扩展 | Tool/MCP 边界、供应链准入、权限过滤 | 符合（本地） | 公共 API/SDK/Webhook 不在范围 |
| 43 自托管离线升级 | Tag 校验、离线缓存、安装/升级/回滚脚本 | 部分（生产） | 未执行容器安装与离线升级演练 |
| 44 容量性能 | 队列上限、背压、租户公平、容量脚本 | 符合（本地） | 生产 soak/混沌未执行 |
| 45 企业合规 | ComplianceProfile、fail closed、证据、支持边界 | 符合（本地） | 不宣称认证或商业 SLA |
| 46 评测 QA | Judge 校准、污染检测、统计门槛、复测 | 符合（本地） | 无 |
| 47 OpenAPI | OpenAPI 3.1、错误模型、TS 生成 | 部分（生产） | `Idempotency-Key`、`If-Match`、游标分页尚未全端点强制 |
| 48 前端路由 | Tenant/Ops 独立应用、路由、权限、共享组件、协作工作台 | 符合（本地） | 无 |
| 49 核心 ERD | 实体、租户字段、Schema 所有权、保留规则 | 部分（生产） | 生产迁移与 RLS 集成测试未执行 |
| 50 可插拔适配 | Provider、目录、S3、KeyVault、身份、OTel、锚定、备份、Compose | 符合（适配） | 真实组件连通性按约束未调试 |

## 3. 验证结果

- `dotnet build RegentAgent.slnx --no-restore`：通过，0 警告、0 错误。
- `DOTNET_ROLL_FORWARD=Major dotnet test RegentAgent.slnx --no-build --no-restore`：91 项通过（Contracts 1、Gateway 12、ControlPlane 78）。
- `corepack pnpm typecheck`、`corepack pnpm lint`、`corepack pnpm test`、`corepack pnpm build`：全部通过；Vitest 共 7 项通过。
- `corepack pnpm test:e2e`：10 项 Playwright 全部通过，覆盖协作四视图、群聊创建、成员追溯和移动端。
- OpenAPI 与 Tenant Dashboard TypeScript schema 已从当前 ControlPlane 重新生成。
- 文档链接/围栏/表格/编号、Compose 与 Collector YAML、离线缓存前置检查：全部通过。
- 容量基线：50 请求，P50 2.4ms、P95 11.8ms。
- 本地备份创建与恢复 round trip：通过，76 个文件。
- ControlPlane、Gateway、Operations 健康检查与 OpenAI/Anthropic 本地 SSE 冒烟：通过。
- 未创建或启动容器，未连接真实 Provider、IdP、Postgres、RustFS、OTLP/Elastic。

## 4. 审计结论

- 当前本地业务闭环没有发现新的未实现模块；本轮修复了身份、密码、目录、流式、生产启动和远程凭据等关键偏差。
- 剩余 `部分（生产）` 项均需真实组件、完整生产迁移或明确后置能力，不能通过本地替身证明。
- 后续生产化必须按 01、03、15、16、17、27、31、47、49 的缺口逐项闭环，且不得把本文的本地结论改写成生产就绪。
