# 15 数据 Schema 与事务边界

- 版本：v0.8（schema 9 里程碑租户隔离基线；审批决定身份、Outbox 有界重试、checksum 恢复与运行角色边界持续校验）
- 范围：Postgres Schema、RLS、RustFS、本地工作区、事务、Outbox/Saga、分区和状态类型
- 对应需求：FR-057、BR-050、BR-051、BR-052、BR-166
- 对应缺口：G-05、G-06、G-07、G-09、G-16

## 1. 存储职责

| 存储 | 职责 | 不是 |
|---|---|---|
| Postgres | 租户、组织、任务、消息、预算、账本、审计和对象元数据 | 大文件存储 |
| RustFS | Git 快照、工作区包、报告、发布包、数据集和备份对象 | 业务状态权威源 |
| Runner 本地/容器目录 | 当前 TaskRun 的活动 Git 工作区和临时缓存 | 持久化真相 |
| SQLite | 单租户开发和单元测试 | 生产或多租户集成测试 |

不保留 WorkspaceStore、ArtifactStore 多实现抽象，也不保留本地文件系统作为持久化对象存储。

## 2. Schema 组织

```text
identity      租户、员工、岗位、任职、能力
runtime      Runner、租约、调度、工具调用
projects     项目、需求、任务、缺陷、评审、决策
communication 会话、消息、委派、交接
performance  KPI、评审、绩效动作、人类事后审查、工作台账
dashboard    只读卡片投影、项目健康、行动中心
models       模型绑定、目录、预算、用量、账本
keyvault     Gateway 加密模型密钥、包装数据密钥、版本元数据
evaluation   评测集、密封数据、评测运行
platform     模型目录、运营配置
audit        审计、Outbox、Inbox、幂等记录
memory       MemoryEmbeddings pgvector 投影（当前实体位于 public schema）
```

Schema 是模块代码边界，不是租户隔离边界。运营数据使用独立权限和访问路径。

`keyvault` schema 使用 Gateway 独立数据库角色；ControlPlane、Runner、Operations 和前端角色无访问权限。该 schema 保存信封加密后的 Provider 密钥、包装数据密钥和版本元数据，主密钥不进入 Postgres。`keyvault` 表同样启用 `tenant_id` 和 RLS。

## 3. 租户隔离与 RLS

租户业务表采用共享表 + `tenant_id` + 强制 Postgres RLS。

- 所有租户业务表包含 `tenant_id UUID NOT NULL`。
- Account 和 Tenant 根记录包含 `deleted_at`、`deletion_request_id` 和 `purged_at`。
- 普通查询和 RLS 同时过滤根记录 `deleted_at IS NULL`。
- 主键、唯一约束、外键和索引包含 `tenant_id`。
- 跨表外键使用复合 `(tenant_id, referenced_id)`。
- 应用角色不是表 Owner，且没有 `BYPASSRLS`。
- 每个事务执行 `SET LOCAL app.tenant_id = '<uuid>'`。
- 仓储层在执行 SQL 前强制注入租户过滤。
- 连接池不得复用前一租户的事务上下文。
- 迁移角色与运行角色分离，运行角色无 DDL 权限。
- 系统级表使用独立 Schema、权限和显式 system scope。
- TenantMembership、TenantOwnership、OwnerInvitation 和 OwnerDelegation 属于控制面身份范围，但查询必须同时校验 tenant_id 和 account_id。
- Primary Owner 使用数据库部分唯一索引保证每租户最多一个 active primary。
- 成员和所有权更新使用短事务，并通过 Outbox 撤销旧会话上下文。
- 软删除先更新根记录和 tombstone，再通过 Outbox 撤销会话、索引和对象访问。
- 活动数据索引使用 `WHERE deleted_at IS NULL` 的部分索引。
- 物理清除分批执行，完成后在短事务内写入 purged_at 和 DeletionProof。

RLS 与复合约束必须由 Postgres 集成测试验证，SQLite 测试不能替代。

### 3.1 当前迁移基线

- `deploy/sql/controlplane-003.sql` 从 EF Core Npgsql 模型生成完整业务表，并对 22 张含 `TenantId` 的表启用且强制 RLS。
- `deploy/sql/controlplane-004.sql` 增加按 `app.user_id` 读取成员关系的策略，将 SchemaVersion 提升为 4，支持登录和租户列表。
- `deploy/sql/controlplane-005.sql` 将 SchemaVersion 提升为 5：创建 `MemoryEmbeddings`、pgvector HNSW 余弦索引和强制 RLS；把 `Positions.Path` 映射为 ltree，按租户递归回填并创建 GiST 索引。
- `deploy/sql/controlplane-006.sql` 将 SchemaVersion 提升为 6，只替换 PostgreSQL 目录注释并写入版本记录；不修改业务行、列类型、默认值、约束、索引、RLS、授权或扩展版本。
- `deploy/sql/controlplane-007.sql` 将 SchemaVersion 提升为 7，为 `Approvals` 增加七个字段：`RequesterUserId`、`TargetEmployeeId`、`TargetCandidateId`、`TargetPositionId`、`ActionHash`、`ConsumedAt`、`ConsumedBy`；旧记录默认失败关闭，不得跨目标或重复执行。
- `deploy/sql/controlplane-008.sql` 将 SchemaVersion 提升为 8，为 `Approvals` 增加 `DecidedBy`/`DecidedAt`，为 `Outbox` 增加 `NextAttemptAt`/`LastError`，并增加可空 `SchemaVersions.Checksum` 迁移校验和列；支持有界退避、处理租约、隔离状态和迁移历史完整性校验。
- `deploy/sql/controlplane-009.sql` 将 SchemaVersion 提升为 9：回填 `Milestones.TenantId` 并设为非空，为 `Projects(TenantId, Id)` 建立唯一索引，将里程碑项目外键改为 `(TenantId, ProjectId)` 复合外键，并启用强制 RLS 与 canonical 租户策略；`BEFORE INSERT OR UPDATE` 触发器仅在 `TenantId` 省略或随 `ProjectId` 更新而未显式改租户时从 `Projects` 派生，保证 N-1 旧插入形状兼容而不削弱 `NOT NULL`、复合外键或 RLS。
- 所有租户表策略同时约束 `USING` 与 `WITH CHECK`，只允许 `current_setting('app.tenant_id')` 匹配当前租户；启动校验只对引号外 token 忽略大小写、空白、冗余 `text` 转换和括号，并单独精确比较字符串字面量与带引号标识符，兼容 PostgreSQL 17/18 将 `NULLIF` 反解析为大写，同时拒绝设置名大小写/空格变化、错误引号列、`OR true`、缺失 `WITH CHECK` 和弱化谓词。
- 当前物理基线中 `Milestones` 已由 009 纳入 `TenantId`、强制 RLS 和复合外键；`Tasks` 的项目外键仍是 003 的单列外键，应用必须继续经 `Projects` 校验租户，不能把单列外键当作跨租户防护。
- 006 的注释不虚构数据库约束：`Approvals` 没有 `Version` 列；`Memberships_user_read` 仅约束 SELECT 且与租户策略在 SELECT 上按 permissive OR 组合，应用仍须按当前租户过滤；`Artifacts.ProjectId` 没有外键或项目删除级联；`AuditRecords` 链首 `PreviousHash` 是空字符串而非 NULL。相应语义由应用状态条件、显式鉴权、清理流程和哈希链校验维护，006 不新增约束。
- 其他物理注释边界：`Notifications.RecipientId` 当前是账户 `UserId` 而非 EmployeeId；`Projects.SpentMinor` 是用量投影累加的项目快照；`Positions.CapabilitiesJson` 不替代能力策略或执行层授权。
- `TenantRlsConnectionInterceptor` 在已认证请求打开 Postgres 连接时设置租户上下文，连接关闭前重置；`TenantRlsScope` 为身份、后台逐租户任务等显式流程在事务内设置租户和用户上下文；无身份连接不设置租户，RLS 默认拒绝。
- `scripts/migrate-postgres.py` 校验迁移版本、SHA-256 和事务，统计 RLS 声明，并把 Npgsql 关键字连接串显式转换为 libpq 形式；Expand 执行前先读取 checked-in manifest，并要求文件/版本/SHA-256 集合精确匹配，篡改 SQL 在任何 DDL 前失败。空数据库通过 `to_regclass` 检测缺失的 `SchemaVersions` 并从 003 开始。真实执行在同一个 psql 会话中获取基础锁及旧版逐迁移锁 `6317000 + target`，重新读取并验证历史、应用全部待执行迁移、仅回填 NULL checksum、断言历史版本集合与最新版本完全匹配后按逆序释放 lock；失败时 `ON_ERROR_STOP` 结束会话并释放锁。`--dry-run`/`--check` 不连接数据库，精确 RLS/策略由 PostgreSQL 集成测试和启动只读校验执行。CI 先执行真实 psql 冒烟，再调用同一 runner。
- Schema 8 引入 checksum 历史：runner 只回填 NULL 值，绝不覆盖非 NULL 校验和；发现非 NULL 漂移立即失败。008 已提交但回填前中断的数据库可在下一次运行自动恢复，历史版本缺失或最新迁移未写入版本行时失败关闭。`deploy/sql/controlplane-checksums.json` 是 checked-in 且嵌入 ControlPlane 程序集的权威 manifest，记录 003～009 每个文件的精确 SHA-256；CI 通过 `scripts/check-migration-checksums.py` 重新生成并校验，runner 与启动校验共享同一 manifest 解析规则并消费同一集合。
- 所有环境下 PostgreSQL 启动均严格只读校验：SchemaVersion 必须为 9，并验证 vector/ltree 扩展、`MemoryEmbeddings.Vector vector(256) NOT NULL`、HNSW `vector_cosine_ops`、`Positions.Path ltree NOT NULL`、GiST 索引、九个审批权威字段与两个 Outbox 重试字段的精确类型/可空性/默认值、`SchemaVersions.Checksum text NULL` 且无默认值，以及 003～009 每个版本与嵌入 manifest 完全相同的 SHA-256；格式合法但值被篡改同样失败。不执行 `EnsureCreated`、本地增量升级、版本引导或演示种子。只有 SQLite/InMemory 的 Development 路径保留本地初始化。
- `PostgresRlsValidator` 以 EF 含 `TenantId` 的实体表加 `MemoryEmbeddings` 为显式 allowlist，同时扫描当前 schema 的所有非扩展物理表，拒绝未映射的 `TenantId` 表。它要求启用且强制 RLS、精确 canonical permissive ALL 策略，并要求 `Memberships` 恰好存在一个精确的 `Memberships_user_read` permissive SELECT/PUBLIC 策略；缺失、复制、弱化、命令或角色错误均失败。运行角色不得是或继承 superuser/`BYPASSRLS`/`CREATEROLE`/`CREATEDB`，不得拥有或可继承任一应用表/schema Owner 角色，不得有 schema CREATE、审计表写权或 SchemaVersions 写权。
- `deploy/sql/runtime-grants.sql` 先撤销运行角色和 PUBLIC 在当前 schema 的 CREATE、表/序列及列级权限，再授予业务表 CRUD；`AuditRecords`/`AuditCheckpoints` 仅 SELECT/INSERT，`SchemaVersions` 仅 SELECT。租户清除不再删除审计检查点，以保持审计表 append-only。
- 本地未配置 PostgreSQL DSN 时，真实数据库集成测试按环境门控跳过，不把跳过表述为通过。CI 使用按 digest 固定的 pgvector PostgreSQL 17/18 矩阵，通过同一 runner 应用 003–009，再执行最小权限授权并运行迁移、RLS、权限和审批并发集成测试。
- `MemoryEmbeddings` 的 `(TenantId, ClaimId)` 复合主键允许不同租户使用相同 claim ID；应用层每次 ADO.NET 操作都开启事务并 `SET LOCAL app.tenant_id`，RLS 同时执行 `USING`/`WITH CHECK`。租户 GUID 文本按值规范化，大小写、花括号和连字符差异不产生新租户。该表是向量投影，不表示 claim 内容已迁入 PostgreSQL。
- `Positions.Path` 使用 `root.<position-guid>[.<child-guid>]` 规范格式，最多四级岗位（ltree `nlevel` 最多为 5）。回填会拒绝环、缺失或跨租户父节点以及超四级数据；公司启动、创建、合并和拆分均维护路径。
- claim、Finding、图谱、纠错、影响和上下文绑定仍由 JSON 本地模块保存；PostgreSQL 全文检索和完整生产记忆权限传播尚未实现。

### 3.2 Schema 6～9 目录注释与验证

- 006 是按 `003 → 004 → 005 → 006` 顺序执行的可重复事务迁移，使用调用方 `search_path` 解析带引号对象；覆盖 005 的英文注释并补充业务含义、状态机、租户/RLS、审计哈希、幂等、租约/fencing、预算币种、生命周期、保留、JSON 投影、外键、pgvector 与 ltree 不变量。
- 注释覆盖当前 `public` 物理 Schema 的 27 张业务表、56 个索引（含 27 个主键索引）、29 个基线 PostgreSQL `p`/`f`/`u`/`c` 目录约束（27 PK + 2 FK，基线 UNIQUE/CHECK 为 0；合成 UNIQUE/CHECK 测试把分母提高到 31；唯一索引只计入 `indexes`），以及 `vector`、`ltree` 两个扩展。业务用户列在 006 为 231，007 增加七个审批字段后为 238，008 增加两个审批决定字段和两个 Outbox 重试字段后为 242，009 增加 `Milestones.TenantId` 后为 243；`SchemaVersions.Checksum` 属于迁移元数据，不计入业务用户列分母，但脚本单独要求其中文注释非空。
- `deploy/sql/verify-schema-comments.sql` 基于 `current_schema()` 和 PostgreSQL 目录统一统计表、列、约束、索引和扩展注释；脚本显式列出 `tables`/`columns`/`constraints`/`indexes`/`extensions` 五类并左连接聚合，缺失分类以 `total=0` 触发零分母异常。约束分母只包含 `contype IN ('p','f','u','c')`。PostgreSQL 18 将表 `NOT NULL` 属性额外暴露为 `contype='n'` 目录行，验证器明确排除，不进入约束分母。注释去空白后必须包含基本汉字（U+4E00–U+9FFF），NULL、空白、纯英文或纯符号均计为缺失。脚本拒绝零对象分母和任何缺失，成功时输出实际 `commented/total` 与 `100%` 覆盖率。
- `PostgresMigrationTests` 覆盖 003–009：006 验证结构/RLS/授权目录与业务行数不变；007/008/009 在应用前种子化代表性数据，重复执行后断言行数、字段值和目录结构不变。009 验证里程碑租户回填、复合外键、强制 RLS、N-1 无 `TenantId` 插入经运行角色派生租户、跨租户读写隔离与两租户行为 canary。表、列、索引、约束和扩展注释分别被置为缺失、空白和纯英文时验证必须失败；再次应用 006 后全部恢复且版本 6 仍唯一。PostgreSQL 17/18 回归还验证 003–009 后基线约束分母为 29/29（27 PK + 2 FK，基线 UNIQUE/CHECK 为 0）、PG18 的 `n` 行存在但不影响分母、合成 UNIQUE/CHECK 把分母提高到 31（唯一索引仍只计入 `indexes`），以及真实约束注释缺失会失败。
- `PostgresMigrationTests` 拥有 deparse 与 RLS/权限回归：PostgreSQL 17/18 验证 deparse 后 canonical 策略与 `Memberships_user_read` 仍精确匹配；RLS 负例覆盖成员策略缺失、`USING(true)`、复制、命令/角色错误以及运行角色直接或继承 `CREATEROLE`/`CREATEDB`。
- `PostgresMigrationRunnerTests` 只拥有 runner/checksum/恢复/tamper/lock 行为：调用真实 `scripts/migrate-postgres.py` 创建空临时数据库并验证版本 003–009 的 checksum 全部匹配；重复运行验证 SKIP，篡改版本 7 checksum 后验证失败关闭，并覆盖 schema 3～7（含无 Checksum 列的 schema 7）升级到 9、008 后回填中断恢复、并发 runner、旧版逐迁移锁互斥、Tampered SQL 在任何建表前失败和最新迁移未写版本行的失败场景。该测试需要 `REGENT_TEST_POSTGRES_MIGRATION_DSN` 和 psql，未配置 DSN 时按环境门控跳过。
- 关系权威源与投影必须区分：已迁移表由 PostgreSQL 负责持久化和 RLS；`MemoryEmbeddings` 只保存 claim 的向量投影，claim、Finding、图谱、纠错、影响和上下文绑定仍由 JSON 本地模块负责。账户会话、邀请和 OIDC 绑定等尚未关系化的模块也不得由逻辑 ERD 推断为已落地表。

## 4. 核心表族

| 领域 | 核心表 |
|---|---|
| 身份组织 | `tenants`、`employees`、`positions`、`position_assignments`、`capability_grants` |
| 运行调度 | `runners`、`task_leases`、`tool_invocations`、`idempotency_records` |
| 项目交付 | `projects`、`requirements`、`tasks`、`defects`、`reviews`、`decisions` |
| 协作容量 | `wip_slots`、`work_ledger`、`conversations`、`messages` |
| 模型财务 | `model_bindings`、`budget_reservations`、`llm_usage`、`ledger_entries` |
| Gateway 密钥库 | `model_keys`、`key_versions`、`wrapped_data_keys` |
| 成本治理 | `budget_envelopes`、`budget_carryovers`、`cost_allocation_rules`、`cost_allocation_results`、`cost_anomalies`、`reconciliation_cases` |
| 通知 | `notifications`、`notification_deliveries`、`notification_preferences`、`notification_suppressions` |
| 容量治理 | `capacity_policies`、`queue_limits`、`capacity_reports` |
| 合规治理 | `compliance_profiles`、`compliance_controls`、`compliance_evaluations`、`compliance_violations`、`compliance_evidence_exports` |
| 对象元数据 | `artifacts`、`workspace_snapshots`、`object_uploads` |
| 记忆向量投影 | `MemoryEmbeddings`（pgvector，claim 正文仍在 JSON） |
| 评测 | `benchmark_suites`、`dataset_versions`、`evaluation_runs`、`case_results`、`judge_assignments`、`judge_calibrations`、`contamination_findings`、`statistical_gates`、`evaluation_retests` |
| 可靠性 | `outbox`、`inbox`、`audit_events` |

模块拥有自己的表，其他模块不能直接写入。跨模块协作通过应用命令和事件完成。

## 5. 字段与命名

- 主键使用 UUID。
- 时间使用 `timestamptz`，业务时间统一 UTC。
- 金额使用 `numeric(18,8)`，Token 使用 `bigint`。
- 可变聚合使用 `row_version` 乐观并发。
- JSON/JSONB 投影只存扩展元数据或本地模块快照，不承载核心业务约束；关系表仍是已迁移模块的权威源，MemoryEmbeddings 仅是向量投影。
- 审计、账本、DoneEvidence 和 Ready 对象不物理删除。
- 表名使用 snake_case 复数，列名使用 snake_case。

## 6. 状态引用类型

数据库状态使用稳定的小写 `snake_case` 字符串，代码使用密封引用类型包装。

```csharp
public sealed record TaskStatus
{
    public string Value { get; }

    private TaskStatus(string value) => Value = value;

    public static readonly TaskStatus Ready = new("ready");
    public static readonly TaskStatus InProgress = new("in_progress");
    public static readonly TaskStatus Done = new("done");

    public static TaskStatus From(string value) => value switch
    {
        "ready" => Ready,
        "in_progress" => InProgress,
        "done" => Done,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public override string ToString() => Value;
}
```

- 禁止业务代码传播裸状态字符串。
- EF Core 使用 ValueConverter 双向映射。
- JSON 对外序列化为字符串，不输出 `{ "value": ... }`。
- 数据库 CHECK 约束验证允许值。
- 展示文案不得作为持久化值。
- 新增状态必须同步包装类型、状态机、CHECK 迁移、API 文档和测试。

## 7. 索引

- 租户索引第一列始终为 `tenant_id`。
- 队列使用部分索引。
- 不为低选择性字段盲目建索引。
- 大 JSONB 只对实际查询字段建索引。

```text
messages(tenant_id, seq) UNIQUE
MemoryEmbeddings(Vector) USING HNSW vector_cosine_ops
Positions(Path) USING GiST
outbox(tenant_id, published_at) WHERE published_at IS NULL
inbox(consumer_id, event_id) UNIQUE
tasks(tenant_id, status, created_at)
task_leases(tenant_id, expires_at) WHERE released_at IS NULL
llm_usage(tenant_id, created_at)
ledger_entries(tenant_id, account_type, created_at)
audit_events(tenant_id, created_at)
```

## 8. 分区

MVP 不预先分区核心业务表。

满足以下条件时启用按月分区：

- 单表超过约 1000 万行/月。
- 单表超过约 500GB。
- 备份、清理或查询延迟已经影响 SLO。

优先分区：

- `llm_usage`
- `ledger_entries`
- `audit_events`
- `messages`

不分区组织、岗位、任务、缺陷和预算预留等强一致小表。分区表主键和唯一约束必须包含分区键。

## 9. 事务边界

### 9.1 单事务强一致

以下操作在同一 Postgres 事务中完成：

- 录用员工 + 占用编制 + 创建任职。
- 认领任务 + 创建 WIP Slot + 更新任务状态。
- 预算预留 + 并发扣减。
- usage 结算 + 写账本 + 写 Outbox。
- 发送消息 + 写 Outbox。
- 评审结论 + 任务状态变更 + 写 Outbox。

事务只修改一个业务聚合，或具有明确不变量的少量聚合。

### 9.2 跨模块流程

```text
Command → 本地事务 → Outbox → Saga/Process Manager
        → 下游幂等 Command → 补偿或人工升级
```

不采用 XA、2PC、跨服务长事务或跨模块直接写表。

### 9.3 并发控制

- 默认 `Read Committed`。
- 普通聚合使用 `row_version`。
- 队列使用 `FOR UPDATE SKIP LOCKED`。
- 预算和编制使用行锁或 `Serializable`。
- `40001/40P01` 冲突有界重试。
- 模型调用、工具调用和 RustFS 上传不包在数据库事务中。

## 10. Outbox 与 Inbox

- Outbox 与业务状态同事务提交。
- Dispatcher 使用 `SKIP LOCKED` 批量领取。
- 至少一次投递，Inbox 按消费者和事件 ID 去重。
- 毒消息进入死信并告警。
- 事件包含 `tenant_id`、`schema_version`、`event_id` 和 `occurred_at`。
- 消费者必须兼容旧事件版本。
- 投影可从权威事件重建，投影滞后不影响业务事务。

## 11. 本地 Git 工作区

- 每个 TaskRun 使用独立目录：`/workspaces/{tenant_id}/{employee_id}/{task_id}/`。
- 容器只挂载本任务目录，不共享其他租户、员工或任务目录。
- 禁止挂载宿主根目录、Docker Socket 和任意宿主路径。
- 路径由服务端生成，校验规范化路径、符号链接和越界访问。
- 容器以非 Root 用户运行，只允许工作目录和临时目录写入。
- CPU、内存、PID、磁盘和网络受限。
- Git 凭据短期注入，不写入仓库历史。
- 工作目录允许丢失，已确认状态和检查点不依赖本地盘。

## 12. RustFS 对象存储

- 直接使用 S3 SDK 和 RustFS endpoint，不保留多后端抽象。
- Bucket 划分为 `regent-workspaces`、`regent-artifacts`、`regent-evaluation`、`regent-backups`。
- 对象键包含租户和资源范围，例如 `tenants/{tenant_id}/tasks/{task_id}/...`。
- 前缀不是安全边界，必须结合 IAM、短期凭据或控制面签名 URL。
- 不开放公共 Bucket，Runner 只能访问授权前缀。
- 下载前由控制面校验权限，再签发短期 URL。
- 启用静态加密、传输加密、版本控制、访问日志和 checksum。

## 13. 上传状态机

```text
PendingUpload
  → 上传 RustFS
  → 校验 key、大小和 checksum
  → Ready
  → 同事务写 Outbox
```

- 上传失败重试或删除孤儿对象。
- 数据库提交失败保留 PendingUpload，由清理任务处理。
- Ready 对象不可覆盖，新内容生成新版本。
- DoneEvidence、审计附件和发布包启用版本保护。
- 归档对象使用 JSONL + Zstandard 或原始二进制 digest，并写入 ArchiveManifest。
- Ready 归档对象不可覆盖，修正生成新版本。
- 对象删除遵循保留策略并记录审计。
- Postgres 按月分区，只有归档清单提交并验证后才 Detach 热分区。

## 14. 检查点与恢复

```text
任务执行 → 安全点快照 → 上传 RustFS
        → Postgres 记录版本和 checksum
        → Runner 可随时重建工作目录
```

Runner 崩溃后：

1. 旧租约失效并 fencing 旧提交。
2. 新 Runner 获取最近 RustFS 快照。
3. 恢复本地工作目录。
4. 从 TaskCheckpoint 续跑。
5. 对未确认上传和孤儿对象进行对账。

## 15. 补偿

| 流程 | 补偿 |
|---|---|
| BYOK 创建 | 状态机 + 删除未完成密钥引用 |
| 任务认领 | 同事务回滚；租约超时释放 |
| 模型调用 | 先预留、后结算；失败释放或对账 |
| 外部工具副作用 | 幂等键 + 执行记录 + 人工对账 |
| 跨模块事件 | Outbox 重试 + Inbox 去重 + Saga 补偿 |
| RustFS 上传 | PendingUpload 重试、孤儿清理和引用核对 |
| 本地工作区丢失 | 从 RustFS 快照恢复 |

外部副作用不能依靠数据库回滚撤销。

## 16. 备份与恢复

- 备份默认关闭；启用后 Postgres 可使用 PITR 和一致性备份。
- RustFS 可启用版本控制、复制或独立备份。
- 恢复顺序为 Postgres → RustFS → 引用核对 → 抽样下载。
- 对账 Artifact、WorkspaceSnapshot、PendingUpload 和孤儿对象。
- 未配置备份时禁止执行恢复；启用组件的生产恢复能力必须在上线前验证。

## 17. 验收场景

1. 两个租户并发操作不互相可见。
2. 应用角色无法绕过 RLS。
3. 连接池切换租户后不泄漏上下文。
4. 跨租户外键和复合唯一约束无法插入。
5. 预算并发预留不超支。
6. 任务认领不重复。
7. 录用不超编制。
8. Outbox 崩溃后不丢事件。
9. Inbox 重复投递不重复执行。
10. SQLite 不被用于生产集成验证。
11. 活动工作区丢失后可从 RustFS 快照恢复。
12. 容器无法访问其他任务或租户目录。
13. RustFS 上传中断后不会产生 Ready 假对象。
14. Ready 对象不能被覆盖。
15. 状态在数据库和 API 中都是稳定字符串。
16. 代码不能使用任意裸字符串创建状态。
17. 新增状态需要同步 CHECK 迁移和测试。
18. 分区迁移不影响在线任务。
