# 设计一致性审计

- 审计日期：2026-11-25
- 范围：`docs/design/01`～`50` 与已接受 ADR
- 基线提交：`7754274`（本轮变更为当前工作树）
- 口径：`符合（本地）` 表示当前本地模式有实现与自动化证据；`符合（适配）` 表示远程端口、协议和失败语义已实现但未连接真实组件；`部分（生产）` 表示本地闭环成立但生产设计仍有明确缺口。本文不把本地实现表述为生产就绪。

## 1. 本轮修复的设计偏差

| 偏差 | 修复 | 证据 |
|---|---|---|
| models.dev 刷新被手工价格反向覆盖，失败时不标记陈旧 | 目录字段优先、手工值仅补缺；失败保留最近快照并标记 `Stale` | `RuntimePolicyServiceTests` |
| 外部身份允许匿名 JIT 入租户，未持久化 subject 绑定 | 增加 state/PKCE/nonce 事务、code 交换、邀请门禁和 provider+issuer+subject 绑定 | `IdentityServiceTests`、`ExternalIdentityProviderTests` |
| 本地密码使用 PBKDF2 | 新凭据使用 Argon2id，旧凭据成功登录后升级 | `IdentityServiceTests` |
| Provider 只支持非流式响应 | 增加原生 SSE 透传、有界读取和流式用量提取，不跨协议转换 | `HttpProviderClientTests` |
| 生产启动自动建表/种子，缺少 RLS 启动边界 | 所有环境 Postgres 只读校验 SchemaVersion 9、精确列契约、全部租户表 RLS/精确策略和运行角色边界；本地 bootstrap 仅限 SQLite/InMemory Development | `SchemaVersionServiceTests`、`PostgresSchemaValidatorTests`、`PostgresMigrationTests` |
| remote Gateway 与生产 Runner 内部端点可匿名调用 | 缺少服务/管理员/Runner 凭据时启动失败，远程请求鉴权 | 组合根校验与中间件 |
| 审计锚定硬编码本地文件 | `IAuditAnchorStore` 支持本地和对象存储，按租户/序号验证 | `AuditAnchorStoreTests` |
| KeyVault 只有本地文件 | 增加 Postgres `keyvault` 信封加密适配、RLS SQL 和 TLS 校验 | `PostgresKeyVaultOptionsTests` |
| 可观测性只有 Exporter 开关 | 增加严格脱敏处理器、ASP.NET Core/HTTP 仪表和导出超时 | `ObservabilityRedactionTests` |
| Compose 硬编码 Postgres，备份只能本地 | 默认 SQLite/本地，组件 Profile 显式启用；备份支持可选 S3 上传/恢复 | YAML 解析、本地备份恢复 round trip |
| 委派/交接/打断只有聊天消息，没有权威状态机和所有权门禁 | 增加 TaskCoordination 状态机、幂等 API/Tool、WIP/能力校验、检查点、租约 fencing 和原子转移 | `CollaborationCoordinationServiceTests`、协作 Playwright |
| required SSO、组能力、IdP MFA 策略和紧急恢复未形成运行时闭环 | 增加策略/Provider API、acr/amr/auth_time/组校验、能力 claims、空闲超时、单次轮换紧急凭据和 15 分钟受限会话 | `IdentityServiceTests`、本地 HTTP 紧急会话冒烟 |
| 批准交付计划没有创建运行时任务，完成状态没有回写交付页 | 增加 `DeliveryRuntimeProjectionService`，计划批准幂等创建 `TaskRecord`，认领/完成回写 `Claimed`/`ReadyForReview` | S-01、`CrossFeatureProjectionTests` |
| 套餐配额只在 HTTP 层检查，招聘和 Runner 自动建员工可绕过 | 组织、项目和任务执行应用服务统一调用 `ITenantQuotaGuard`；terminated 员工不占配额 | S-02、S-07、`BillingServiceTests`、`TaskExecutionServiceTests` |
| 员工离职仍保留在途任务租约，terminated 员工可再认领 | 中断在途任务、清租约、认领排除 terminated 员工，Agent 中间件拒绝旧 Token | S-03、`OrganizationServiceTests`、`TaskExecutionServiceTests` |
| 个人记忆纠错把复核者变成作者，导致原作者失去召回权限 | 个人 claim 保留原作者 ACL，复核者只记录在 CorrectionTask | S-04、`MemorySafetyServiceTests` |
| Gateway 用量只存在本地账本，没有进入控制面成本和项目支出 | 增加持久投影队列、异步重试、事件 ID 幂等、租户/员工/任务归属校验和项目支出更新 | S-05、`UsageProjectionTests`、`RuntimePolicyServiceTests` |
| 租户软删除把请求 GUID 文本与 SQLite 大写 GUID 文本直接比较，返回 404 | 改为 GUID 值比较；现有 Cookie 与 Agent Token 均 fail closed，仅保留恢复/会话管理 | S-06、`DataLifecycleServiceTests` |
| 紧急访问响应没有显式 `sessionKind`，调用方需额外查询会话 | `EmergencyAccessResult` 增加 `sessionKind=Emergency` | S-08、`IdentityServiceTests` |
| 记忆向量硬编码 JSON，租户清除无法删除 Postgres 投影 | 增加 `IMemoryVectorStore`、有效 Provider 选择、Guid 值语义、完整权限候选预过滤和统一租户清除；Postgres 失败不回退 | `MemoryVectorStoreTests`、`MemoryIndexServiceTests`、`PostgresRuntimeSmokeTests` |
| 组织岗位没有可查询路径，Postgres 缺少 ltree 索引 | 维护规范 `Positions.Path`，Postgres 使用 ltree/GiST 与 `nlevel`，其他提供程序保留 text/循环回退 | `PositionPathTests`、`OrganizationServiceTests`、`PostgresLTreeTests` |

## 2. 逐模块矩阵

| 设计 | 主要实现证据 | 结论 | 剩余边界 |
|---|---|---|---|
| 01 基础契约与状态 | Cookie 控制面 + Agent Assignment Token、租户上下文、Outbox、幂等服务 | 部分（生产） | 生产 JWKS 轮换与完整 Token 吊销传播尚未实现 |
| 02 运行时可靠性 | `TaskExecutionService`、租约/fencing、Outbox/Inbox、本地沙箱、交付投影、离职中断和自动建员工配额 | 符合（本地） | 真实多 Worker 故障演练未执行 |
| 03 模型、计量与预算 | Provider 路由、原生 HTTP/SSE、Agent JWT、KeyVault、价格快照、金额预留/结算/释放/过期、独立尝试用量、持久投影队列和账单更正 | 符合（本地） | 生产多 Worker 原子预留与真实 Provider 账单未连接 |
| 04 安全、治理与恢复 | Tool PEP、审批、审计、DLP、事件响应、备份脚本 | 部分（生产） | 容器隔离、真实 WORM 与异地恢复按约束未执行 |
| 05 产品交付闭环 | `DeliveryPolicy`、`DeliveryService`、运行时任务投影、独立验收、发布/回滚 | 符合（本地） | 容器发布未执行 |
| 06 主控与动态组织 | `OrganizationService`、能力授权、组织调整、继任、规范路径与 ltree 子树查询 | 符合（本地真实 PG） | 生产并发与大规模迁移未验证 |
| 07 运营平台 | Operations、租户启停、SupportGrant、状态页、运营审计 | 符合（本地） | 托管计费与外部 SLA 不在范围 |
| 08 工作流系统 | `WorkflowService`、版本、向上提名、独立审查、执行门禁 | 符合（本地） | 无 |
| 09 记忆系统 | 混合检索、图谱、ACL、衰减、归档、删除传播、pgvector 完整候选过滤与统一租户清除 | 符合（本地真实 PG） | claim、Finding、图谱和纠错仍为 JSON；精确 ACL 查询自然计划随统计选择 PK/Seq Scan，生产全文检索与规模压测未验证 |
| 10 绩效与自我改进 | 改进审查、KPI、绩效动作、申诉反转 | 符合（本地） | 无 |
| 11 项目管理 | 需求/任务/缺陷状态机、计划到运行时投影、认领/完成回写、评审、裁决 | 符合（本地） | 无 |
| 12 通信与路由 | `CommunicationRouter`、管理链/专业链、项目覆盖 | 符合（本地） | 无 |
| 13 Agent 聊天协作 | Agent Token、群成员、回复/@、证据、委派/交接/求助/打断状态机、安全点租约、只读过程面板 | 符合（本地） | 无 |
| 14 模型评测回归 | 版本化评测、密封/污染检测、回归计划 | 符合（本地） | 真实模型回归未执行 |
| 15 数据 Schema 与事务 | SQLite/JSON/对象存储、Outbox、SchemaVersion 9、Npgsql 基线、27 表/243 个业务用户列/29 个基线 `p`/`f`/`u`/`c` 约束（27 PK + 2 FK，基线 UNIQUE/CHECK 为 0；合成 UNIQUE/CHECK 测试分母为 31；唯一索引只计入 `indexes`；PG18 `contype='n'` 排除）/56 索引中文注释、24 表强制 RLS、MemoryEmbeddings HNSW、Positions.Path ltree/GiST、精确列与策略启动校验、迁移 checksum 中断恢复和最小权限运行角色 | 符合（本地；真实 PG 由 CI 门控） | CI 一次性 PostgreSQL 17/18 服务容器集成测试通过；生产/live 迁移及生产规模并发/混沌未验证 |
| 16 部署升级迁移 | Compose pgvector Profile、全环境 Postgres 启动校验、空数据库 003–009 bootstrap、单会话 advisory-lock runner、checksum 漂移失败关闭与中断恢复、006 `p`/`f`/`u`/`c` 注释覆盖/幂等/结构不变、007/008/009 行业务行保留、005 无效层级回滚与最小权限授权、PostgreSQL 17/18 一次性服务容器 CI 矩阵 | 部分（生产） | Contract、不可变 Tag 与混版本回滚未在容器环境演练 |
| 17 可观测性与测试 | 四服务 OTel、严格脱敏、两个前端浏览器遥测、SLO/错误预算、L1～L3 告警确认/抑制/恢复、行动中心投影和 ES 状态展示 | 符合（本地真实 ES） | 生产 TLS/ILM、告警渠道与 24 小时 soak 未执行 |
| 18 注入防御 | taint、TaskGrant、注入扫描、执行层鉴权 | 符合（本地） | 无 |
| 19 记忆投毒纠错 | Finding、隔离、申诉、独立纠错、个人作者 ACL 保留、影响传播 | 符合（本地） | 无 |
| 20 工具与插件供应链 | 签名、SBOM、准入、撤销、影响记录、272 依赖 LicenseInventory 和 NuGet/pnpm 漏洞门禁 | 符合（本地） | 真实镜像与容器漏洞扫描未执行 |
| 21 PII 与秘密脱敏 | DLP、令牌化、秘密隔离、删除传播 | 符合（本地） | 无 |
| 22 审计完整性 | 哈希链、Merkle、可插拔锚定、验证 API | 符合（适配） | 真实外部 WORM 未连接 |
| 23 离职调岗再入职 | 永久工号、任职周期、Boss 审批、在途任务中断、租约清除、terminated 认领排除、私有记忆隔离 | 符合（本地） | 无 |
| 24 内容安全 | 版本化策略、分级处置、复核、申诉 | 符合（本地） | 无 |
| 25 安全事件响应 | SEV 分级、遏制、证据、通知、恢复、复盘 | 符合（本地） | 无 |
| 26 账户身份与找回 | Argon2id、恢复码、会话撤销、GitHub 已验证邮箱 | 符合（本地） | 邮件/短信与人工找回明确后置 |
| 27 企业 OIDC | discovery/JWKS、code+PKCE、nonce、邀请、subject 绑定、required SSO、组能力、acr/amr/auth_time、空闲超时和紧急恢复 | 符合（适配） | 真实 IdP/JWKS 轮换与容灾未连接验证；SAML/BFF/设备管理后置 |
| 28 账户租户所有权 | 多租户成员、Co-Owner、Primary Owner、限时委托、应用层套餐配额、订阅账本和 Owner 数据导出 | 符合（本地） | 真实支付、税务、跨环境导入和托管退出后置 |
| 29 软删除 | GUID 值比较、Cookie/Agent fail closed、软删除/恢复、tombstone、物理清除证明 | 符合（本地） | 无 |
| 30 保留归档 | 保留策略、法律保留、归档校验、授权恢复 | 符合（本地） | 真实冷存储未连接 |
| 31 备份恢复 | 默认关闭、组件开关、BackupJob/manifest/checksum、有效 RPO/RTO、双人恢复审批与演练报告 | 符合（本地） | 真实组件备份、异地副本和生产恢复未执行 |
| 32 产出权利来源 | 权利声明、来源、第三方通知、发布门禁 | 符合（本地） | 无 |
| 33 公平招聘 | JD、盲评、公平审计、候选人申诉 | 符合（本地） | 无 |
| 34 开源许可分发 | Apache-2.0、NOTICE、DCO、272 组件 LicenseInventory、CycloneDX SBOM、许可/漏洞 CI 门禁 | 符合（本地） | 真实镜像与容器漏洞扫描未执行 |
| 35 仪表盘 IA | Boss 首屏、确定性健康、行动中心、组织协作四视图与追溯 | 符合（本地） | 无 |
| 36 审批委托缺席 | 分级、协商、委托、SLA、超时拒绝 | 符合（本地） | 无 |
| 37 国际化本地化 | zh-CN/en-US、IANA 时区、ISO 币种、翻译回退 | 部分（生产） | WCAG 2.2 AA 按 G-52 明确后置 |
| 38 作息调度 | 工作窗口、低价窗口、假期、紧急唤醒 | 符合（本地） | 无 |
| 39 CI/CD 与 Compose | GitHub Actions、不可变 Action SHA、supply-chain job、Tag、Compose Profile、校验脚本 | 符合（本地） | CI 使用一次性 PostgreSQL 17/18 服务容器并执行后端镜像 smoke build；生产/live 迁移与生产规模并发/混沌未验证 |
| 40 成本归属分摊 | Gateway 用量持久投影、原币账本、幂等、分摊、异常、项目预算 | 符合（本地） | 不做汇率换算 |
| 41 内部通知 | 持久通知、分级、去重、SignalR 增强 | 符合（本地） | 仅站内 |
| 42 内部 API 扩展 | Tool/MCP 边界、供应链准入、权限过滤 | 符合（本地） | 公共 API/SDK/Webhook 不在范围 |
| 43 自托管离线升级 | ReleaseManifest、Tag 校验、离线缓存、Expand/Contract、N/N-1 计划与回滚边界演练 | 符合（本地） | 未执行真实容器安装、离线升级和生产回滚 |
| 44 容量性能 | 队列上限、背压、租户公平、容量脚本 | 符合（本地） | 生产 soak/混沌未执行 |
| 45 企业合规 | ComplianceProfile、fail closed、证据、支持边界 | 符合（本地） | 不宣称认证或商业 SLA |
| 46 评测 QA | Judge 校准、污染检测、统计门槛、复测 | 符合（本地） | 无 |
| 47 OpenAPI | OpenAPI 3.1、全局幂等/版本中间件、request_id、稳定错误码、签名游标、用量投影端点和 TS 生成 | 符合（本地） | 生产多实例幂等仍需 Postgres 唯一约束与事务保证 |
| 48 前端路由 | Tenant/Ops 独立应用、路由、权限、共享组件、协作工作台 | 符合（本地） | 无 |
| 49 核心 ERD | 实体、租户字段、Schema 所有权、MemoryEmbeddings 与 Positions.Path 物理投影 | 符合（本地真实 PG） | CI 已运行一次性容器迁移/并发集成测试；生产/live 迁移及生产规模并发/混沌未验证 |
| 50 可插拔适配 | Provider、目录、S3、KeyVault、身份、OTel、锚定、备份、Compose、JSON/Postgres 记忆向量端口 | 符合（适配） | 其余真实组件连通性按约束未调试 |

## 3. 验证结果

- 使用精确 .NET 10 SDK 10.0.404 执行 `dotnet build RegentAgent.slnx --no-restore --configuration Release`：通过，0 警告、0 错误。
- 在不配置 `REGENT_TEST_POSTGRES_DSN`/`REGENT_TEST_POSTGRES_MIGRATION_DSN` 的情况下执行完整 Release 测试：366 项总数、345 项通过、21 项跳过、0 失败。跳过项全部为 PostgreSQL 运行角色、临时 schema 迁移、RLS/权限和 runner 集成测试，不计为通过；CI 另在一次性 PostgreSQL 17/18 服务容器中执行这些真实集成测试。
- `DeliveryServiceTests.Self_review_and_unverified_key_claims_are_rejected`：通过；测试先种子化项目任务，再分别断言关键结论双源错误和自评审守卫错误。
- `python3 -m py_compile scripts/migrate-postgres.py`、Expand dry-run、Contract dry-run：通过；dry-run 列出 003–009 与 contract 文件、SHA-256、事务并统计 RLS 声明；精确 RLS/策略由 PostgreSQL 集成测试和启动只读校验执行。
- `python3 scripts/check-workflow-pins.py`：通过，第三方 Action 均为完整 commit SHA。
- Tenant Dashboard TypeScript schema 重新生成到临时文件并与 `apps/tenant-dashboard/src/lib/api-client/schema.d.ts` diff：通过。前端契约未改变，因此本轮未运行前端 typecheck/test/build。
- `python3 scripts/validate-docs.py`：通过；文档链接、围栏、表格和 FR/BR/G/Q 编号有效。`.github/workflows/ci.yml` 与 `compose.yaml` YAML 解析通过。
- `PostgresMigrationTests` 拥有 deparse、注释、PG18、UNIQUE/CHECK 和 RLS 回归：CI 的 PostgreSQL 17/18 一次性服务容器验证基线约束分母 29/29（27 PK + 2 FK，基线 UNIQUE/CHECK 为 0）、PG18 `contype='n'` 排除、合成 UNIQUE/CHECK 把分母提高到 31（唯一索引只计入 `indexes`）和真实约束注释缺失失败；`PostgresMigrationRunnerTests` 拥有 runner/checksum/恢复/tamper/lock 回归。CI 同时执行后端镜像 smoke build；未执行 live/生产迁移或生产规模并发/混沌演练。
- `git diff --check`：通过。本轮未执行 live 或生产 PostgreSQL 迁移、生产容器发布或任何 Git 状态变更。

## 4. 审计结论

- 当前用户约束下的 FR-001～FR-090 本地实现均已完成并通过自动化回归，没有剩余本地未实现模块。
- 剩余 `部分（生产）` 项均需真实远程组件、生产数据库集成、真实支付、跨环境导入或容器发布验证，不能通过本地替身证明。
- 后续生产化必须按 01、03、15、16、17、27、31、47、49 的缺口逐项闭环，且不得把本文的本地结论改写成生产就绪。
