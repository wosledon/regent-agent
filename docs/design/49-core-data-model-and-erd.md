# 49 核心 ERD 与数据字典

- 版本：v0.6（schema 9 里程碑租户隔离基线）
- 范围：租户、身份、组织、项目、消息、模型、账本、评测、治理和运营核心实体关系
- 关联设计：[15 数据 Schema 与事务边界](./15-data-schema-and-transactions.md)、[01 基础契约与状态模型](./01-contracts-and-state.md)、[后端技术文档](../Regent-Agent-后端技术文档.md)

## 1. 范围

- 本文是核心逻辑模型，不替代逐表迁移脚本和字段级 Schema；上方 Mermaid 保持核心逻辑实体视图，`MemoryEmbeddings` 与 `Positions.Path` 由 005 引入，schema 6 仅补充中文目录注释。逻辑实体不得据此推断为当前已落地表。
- 逻辑模型要求所有租户业务表包含 `tenant_id` 并启用强制 RLS；当前物理基线中 `Milestones` 由 009 增加 `TenantId`、强制 RLS 和复合项目外键，`MemoryEmbeddings` 直接使用 `TenantId` 和强制 RLS。
- 目标租户约束要求主键、唯一约束、外键和索引按租户隔离；009 已将 `Milestones` 项目外键改为 `(TenantId, ProjectId)`，但 `Tasks` 仍是 003 的单列项目外键，应用必须经项目校验租户。
- 跨表复合外键 `(tenant_id, referenced_id)` 是目标不变量；当前只有里程碑项目关系具备该约束，不能据逻辑 ERD 声称所有物理外键均已按租户复合。
- 006 注释遵循当前物理例外：`Approvals` 无 `Version`；`Memberships_user_read` 是启动强制要求的唯一 SELECT-only/PUBLIC permissive 策略，与租户策略在 SELECT 上按 permissive OR 组合，缺失、复制、弱化、命令或角色错误均失败；`Artifacts.ProjectId` 无外键或级联删除；`AuditRecords` 链首 `PreviousHash=''` 而非 NULL。不得为迁就注释新增约束。
- 状态使用稳定 `snake_case` 字符串，代码使用密封引用类型包装。
- 大文件和二进制内容存 RustFS，Postgres 只存元数据和对象引用。

## 2. 核心 ERD

```mermaid
erDiagram
    TENANT ||--|| COMPANY : owns
    TENANT ||--o{ MEMBERSHIP : contains
    ACCOUNT ||--o{ MEMBERSHIP : joins
    TENANT ||--o{ POSITION : defines
    POSITION ||--o{ POSITION_ASSIGNMENT : staffed_by
    EMPLOYEE ||--o{ POSITION_ASSIGNMENT : assigned
    TENANT ||--o{ PROJECT : owns
    PROJECT ||--o{ REQUIREMENT : contains
    REQUIREMENT ||--o{ TASK : decomposes
    TASK ||--o{ TASK_RUN : attempts
    EMPLOYEE ||--o{ TASK_RUN : executes
    TENANT ||--o{ CONVERSATION : owns
    CONVERSATION ||--o{ MESSAGE : contains
    TASK_RUN ||--o{ TOOL_INVOCATION : performs
    TENANT ||--o{ MODEL : configures
    MODEL ||--o{ MODEL_KEY_RECORD : has_versions
    TASK_RUN ||--o{ USAGE_EVENT : consumes
    USAGE_EVENT ||--o{ LEDGER_ENTRY : posts
    TENANT ||--o{ BUDGET_RESERVATION : reserves
    TASK_RUN ||--o{ BUDGET_RESERVATION : uses
    TENANT ||--o{ ARTIFACT : stores
    TASK_RUN ||--o{ ARTIFACT : produces
    TENANT ||--o{ EVALUATION_RUN : evaluates
    EMPLOYEE ||--o{ EVALUATION_RUN : candidate
    EVALUATION_RUN ||--o{ JUDGE_ASSIGNMENT : judged_by
    EMPLOYEE ||--o{ JUDGE_ASSIGNMENT : judges
    TENANT ||--o{ COMPLIANCE_PROFILE : configures
    TENANT ||--o{ NOTIFICATION : receives
    TENANT ||--o{ AUDIT_EVENT : records
    TENANT ||--o{ SUPPORT_GRANT : authorizes

    TENANT {
        UUID id PK
        string status
        timestamp deleted_at
        timestamp purged_at
    }
    ACCOUNT {
        UUID id PK
        string primary_email
        string status
        timestamp deleted_at
    }
    MEMBERSHIP {
        UUID id PK
        UUID tenant_id FK
        UUID account_id FK
        string role
        string status
    }
    COMPANY {
        UUID id PK
        UUID tenant_id FK
        string name
        string status
    }
    POSITION {
        UUID id PK
        UUID tenant_id FK
        UUID parent_position_id FK
        string name
        string status
    }
    EMPLOYEE {
        UUID id PK
        UUID tenant_id FK
        string employee_no
        string operational_state
        UUID active_binding_id FK
    }
    POSITION_ASSIGNMENT {
        UUID id PK
        UUID tenant_id FK
        UUID position_id FK
        UUID employee_id FK
        string kind
        timestamp started_at
        timestamp ended_at
    }
    PROJECT {
        UUID id PK
        UUID tenant_id FK
        string name
        string workflow_state
        UUID budget_envelope_id FK
    }
    REQUIREMENT {
        UUID id PK
        UUID tenant_id FK
        UUID project_id FK
        string title
        string status
        int version
    }
    TASK {
        UUID id PK
        UUID tenant_id FK
        UUID requirement_id FK
        string task_type
        string status
        int weight
        UUID assignee_id FK
    }
    TASK_RUN {
        UUID id PK
        UUID tenant_id FK
        UUID task_id FK
        UUID employee_id FK
        int attempt
        string status
        UUID lease_token
    }
    CONVERSATION {
        UUID id PK
        UUID tenant_id FK
        string kind
        string status
    }
    MESSAGE {
        UUID id PK
        UUID tenant_id FK
        UUID conversation_id FK
        bigint seq
        string message_type
        string priority
    }
    TOOL_INVOCATION {
        UUID id PK
        UUID tenant_id FK
        UUID task_run_id FK
        string tool_name
        string policy_decision
        string result_status
    }
    MODEL {
        UUID id PK
        UUID tenant_id FK
        string provider
        string provider_model_id
        string protocol
        string key_ref
        string state
    }
    MODEL_KEY_RECORD {
        UUID id PK
        UUID tenant_id FK
        UUID model_id FK
        string key_ref
        int key_version
        string encrypted_key
        string wrapped_data_key
        string state
    }
    USAGE_EVENT {
        UUID id PK
        UUID tenant_id FK
        UUID task_run_id FK
        string source
        string source_event_id
        bigint amount
        string currency
    }
    LEDGER_ENTRY {
        UUID id PK
        UUID tenant_id FK
        UUID usage_event_id FK
        UUID project_id FK
        string account_type
        bigint amount
        string currency
        string entry_type
    }
    BUDGET_RESERVATION {
        UUID id PK
        UUID tenant_id FK
        UUID task_run_id FK
        bigint amount
        string currency
        string state
        timestamp expires_at
    }
    ARTIFACT {
        UUID id PK
        UUID tenant_id FK
        UUID task_run_id FK
        string object_key
        string hash
        string upload_state
    }
    EVALUATION_RUN {
        UUID id PK
        UUID tenant_id FK
        UUID candidate_employee_id FK
        UUID dataset_version_id FK
        string model_version
        int seed
        string status
    }
    JUDGE_ASSIGNMENT {
        UUID id PK
        UUID tenant_id FK
        UUID evaluation_run_id FK
        UUID judge_employee_id FK
        string independence
        string status
    }
    COMPLIANCE_PROFILE {
        UUID id PK
        UUID tenant_id FK
        string preset
        int version
        string status
    }
    NOTIFICATION {
        UUID id PK
        UUID tenant_id FK
        UUID recipient_id FK
        string level
        string status
        bigint after_id
    }
    AUDIT_EVENT {
        UUID id PK
        UUID tenant_id FK
        bigint seq
        string prev_hash
        string event_hash
        string event_type
    }
    SUPPORT_GRANT {
        UUID id PK
        UUID tenant_id FK
        UUID operator_id FK
        string scope
        timestamp expires_at
        string status
    }
```

## 3. Schema 所有权

| Schema | 主要实体 | 权威模块 |
|---|---|---|
| `identity` | Account、Tenant、Membership、Employee、Position、Assignment | ControlPlane Identity/Organization |
| `runtime` | Runner、TaskRun、Lease、ToolInvocation | ControlPlane Runtime |
| `projects` | Project、Requirement、Task、Defect、Review、Decision | Projects |
| `communication` | Conversation、Message、Route、Delegation、Handoff | Communication/Chat |
| `performance` | KPI、PerformanceReview、PerformanceAction | Performance |
| `models` | Model、Binding、Usage、Budget、Ledger | ControlPlane/Gateway |
| `keyvault` | ModelKeyRecord、KeyVersion、WrappedDataKey | Gateway |
| `evaluation` | Benchmark、EvaluationRun、Judge、Calibration | Evaluation |
| `governance` | Escalation、Compliance、Notification、Audit | ControlPlane Governance |
| `platform` | Operator、SupportGrant、ResourcePool、Backup | Operations |
| `dashboard` | 只读卡片、ProjectHealth、ActionCenter 投影 | ControlPlane Dashboard |
| `public`（当前） | MemoryEmbeddings pgvector 投影 | Memory |

上表是目标逻辑所有权，不表示所有实体已经关系化。Schema 9 的当前物理权威源是 `deploy/sql/controlplane-003.sql`～`009.sql` 建立的 27 张 `public` 表；账户、成员、组织、项目、任务、审计、Outbox/Inbox 等已迁移模块以 PostgreSQL 为事实源。`MemoryEmbeddings` 只是派生向量投影；claim、Finding、图谱、纠错、影响和上下文绑定仍由 JSON 本地模块保存。账户会话、邀请、OIDC 绑定、聊天和绩效等尚未迁移模块不得写成已落地表。

Schema 6 通过 006 为这 27 张表、231 个业务用户列、55 个索引（含主键索引）、29 个基线 PostgreSQL `p`/`f`/`u`/`c` 目录约束（27 PK + 2 FK，基线 UNIQUE/CHECK 为 0；合成 UNIQUE/CHECK 测试把分母提高到 31；唯一索引只计入 `indexes`）及 vector/ltree 扩展提供中文业务注释；007 追加 `Approvals.RequesterUserId`、`TargetEmployeeId`、`TargetCandidateId`、`TargetPositionId`、`ActionHash`、`ConsumedAt`、`ConsumedBy` 七个字段，业务用户列达到 238；008 再追加 `Approvals.DecidedBy`、`DecidedAt` 与 `Outbox.NextAttemptAt`、`LastError` 四个业务字段，达到 242；009 增加 `Milestones.TenantId`，业务用户列达到 243，并将里程碑索引替换为租户复合索引、为项目增加租户唯一索引后，索引总数达到 56；同时安装仅在新行省略 `TenantId` 时从项目派生的兼容触发器，保留非空、复合外键、强制 RLS 和 canonical 策略。`SchemaVersions.Checksum` 是额外迁移元数据列，不进入业务用户列分母，但单独校验其中文注释。`verify-schema-comments.sql` 排除系统列、dropped 列、临时表和扩展拥有的系统对象，约束分母只包含 `contype IN ('p','f','u','c')`；PostgreSQL 18 的 `contype='n'` 表 `NOT NULL` 目录行不进入该分母。脚本要求每类总数大于零，并将 NULL、空白或没有基本汉字的注释计为缺失。迁移测试对表、列、索引、约束和扩展分别覆盖缺失、空白、纯英文三种失败路径，再应用 006 验证恢复；PostgreSQL 17/18 回归还验证 003–009 后基线约束注释为 29/29（27 PK + 2 FK，基线 UNIQUE/CHECK 为 0）、PG18 `n` 行排除、合成 UNIQUE/CHECK 把分母提高到 31（唯一索引仍只计入 `indexes`）和真实约束注释缺失失败。注释迁移不改变关系结构、RLS、授权或业务数据。007/008/009 迁移测试种子化业务行并验证幂等重放保留行数和值。

## 4. 核心数据字典

| 实体 | 关键字段 | 说明 | 权威源 |
|---|---|---|---|
| Tenant | id、status、deleted_at、purged_at | 安全、预算和数据边界 | ControlPlane |
| Account | id、email、status、deleted_at | 全局登录身份 | ControlPlane |
| Membership | tenant_id、account_id、role、status | 账户与租户关系 | ControlPlane |
| Company | tenant_id、name、status | 一租户一公司 | ControlPlane |
| Position | parent_position_id、path、name、capabilities | 动态岗位；Postgres `path` 为 ltree，其他开发提供程序为 text | ControlPlane |
| MemoryEmbedding | tenant_id、claim_id、content_hash、model_version、vector、indexed_at | 005 引入的 pgvector 投影，006 补充中文注释；`(tenant_id, claim_id)` 主键、`vector(256)`、HNSW `vector_cosine_ops`；claim 正文仍在 JSON | Memory |
| Employee | employee_no、operational_state、binding | 独立员工运行时 | ControlPlane |
| PositionAssignment | employee_id、position_id、period | 任职历史 | ControlPlane |
| Project | workflow_state、budget_envelope_id | 项目生命周期 | Projects |
| Requirement | project_id、version、acceptance_criteria | 需求和验收锚 | Projects |
| Task | requirement_id、task_type、weight、status | 可认领工作项 | Projects |
| TaskRun | task_id、attempt、lease_token、status | 一次执行尝试 | Runtime |
| Conversation | kind、status、scope | 私人/群组/岗位会话 | Chat |
| Message | conversation_id、seq、priority | 租户内有序消息 | Communication |
| ToolInvocation | task_run_id、tool_name、policy_decision | Tool 执行和审计 | Runtime/PEP |
| Model | provider、model_id、protocol、key_ref | 模型元数据 | ControlPlane |
| ModelKeyRecord | encrypted_key、wrapped_data_key、version | Postgres 密钥库密文 | Gateway |
| UsageEvent | source_event_id、amount、currency | 权威用量 | Gateway/Runner |
| LedgerEntry | usage_event_id、project_id、entry_type | 只增不改钱账 | ControlPlane |
| BudgetReservation | amount、state、expires_at | 调用前预算预留 | ControlPlane |
| Artifact | object_key、hash、upload_state | RustFS 对象元数据 | ControlPlane |
| EvaluationRun | dataset_version、model_version、seed | 可复现评测 | Evaluation |
| JudgeAssignment | judge_employee_id、independence | Judge 员工和模型独立性 | Evaluation |
| ComplianceProfile | preset、version、status | 可配置合规策略 | Governance |
| Notification | recipient_id、level、status | 站内通知 | Governance |
| AuditEvent | seq、prev_hash、event_hash | 不可变审计链 | Governance |
| SupportGrant | operator_id、scope、expires_at | 限时运营支持 | Operations |

## 5. 关键关系与不变量

- 一个账户可以有多个 Membership，一个租户可以有多个成员。
- 第一梯队一个租户对应一个 Company。
- 一个员工同一时间只有一个 primary PositionAssignment。
- 一个岗位同一时间只有一个主任职者。
- `Positions.Path` 是 `root.<position-guid>[.<child-guid>]` 规范 ltree，最多四级岗位；创建、合并、拆分和回填必须保持父路径、租户、无环和深度约束。
- `MemoryEmbeddings` 只保存租户内 256 维有限向量投影，按显式 claim 候选集合执行余弦 top-k；应用层权限预过滤与强制 RLS 同时生效。
- Task 必须属于 Requirement，Requirement 必须属于 Project。
- TaskRun 通过租约和 fencing 防止旧 Worker 覆盖新结果。
- Message 的 `(tenant_id, seq)` 唯一。
- UsageEvent 的 `(source, source_event_id)` 唯一。
- LedgerEntry 只增不改，更正引用原条目。
- ModelKeyRecord 只存密文和包装数据密钥，主密钥不进入数据库。
- Artifact 只有 `Ready` 后才能被任务完成引用。
- JudgeAssignment 必须指向独立员工运行时，不能自评。
- ComplianceProfile 必需控制失败时相关动作 fail closed。
- AuditEvent 连续 `seq`、`prev_hash` 和 `event_hash` 不可静默改写。

## 6. RLS 与删除

- 租户业务表启用 `FORCE ROW LEVEL SECURITY`。
- 应用角色不得直接或继承 superuser/`BYPASSRLS`/`CREATEROLE`/`CREATEDB` 或 Owner 角色，不是任一应用表/schema Owner，且无 schema CREATE、审计表写权或 SchemaVersions 写权。
- 每个事务使用 `SET LOCAL app.tenant_id`。
- Account 根记录和 Tenant 根记录支持软删除。
- 软删除立即撤销会话、Token 和对象访问。
- 窗口结束后分批物理清除并生成 DeletionProof。
- tombstone 防止已删除身份复活。
- 法律保留只限制物理清除，不恢复普通访问。
- `keyvault` schema 使用 Gateway 独立角色，其他服务角色无访问权限。
- 005 引入的 `MemoryEmbeddings` 同样启用且强制 RLS，并条件性授予 `regent_controlplane` CRUD 权限；`Positions.Path` 使用 GiST ltree 索引，向量列使用 HNSW 余弦索引。真实运行角色测试覆盖完整候选过滤余弦查询、ltree 子树/深度和跨租户读写隔离；精确 ACL 向量查询的本地自然计划可为复合主键或顺序扫描，HNSW 是否被选择取决于数据分布和统计。
- PostgreSQL 启动在所有环境都只读校验 schema 9 的扩展、列类型/非空、HNSW/GiST、RLS 精确策略、`SchemaVersions.Checksum` 列契约，以及 003～009 与嵌入 checksum manifest 完全相同的 SHA-256；schema 0/4/5/6/7/8 或不完整 schema 9 直接失败且不写版本、不种子。SQLite/InMemory 仅在 Development 执行本地初始化。
- SQLite/InMemory 的向量仍写入 `memory-vectors.json`，组织路径使用 text 和既有循环回退；它们不是 PostgreSQL RLS 验证证据。

## 7. 保留与归档

| 数据 | 默认保留 |
|---|---|
| 消息 | 在线 90 天 + 归档 1 年 |
| 任务和项目过程数据 | 在线 180 天 + 归档 3 年 |
| 账本和审计 | 租户终止后 7 年 |
| 密钥密文 | 模型存活期间，删除后按删除策略清除 |
| 评测原始材料 | 按数据集版本和合规策略保留 |
| 日志/指标/Trace | 14 天 / 30 天 / 7 天 |
| 备份 | 滚动最长 35 天，启用时生效 |

删除优先于归档；归档不是备份。

## 8. ERD 验收

1. 每个核心实体都有明确权威模块。
2. 所有租户实体包含 tenant_id 和 RLS 规则。
3. 密钥密文与主密钥分离。
4. 任务、用量、账本和审计链路可追踪。
5. Judge 员工与候选员工关系明确。
6. 软删除、物理清除和 tombstone 可表达。
7. 保留策略与归档边界一致。
8. Schema 9 的表、243 个业务用户列、29 个基线 `p`/`f`/`u`/`c` 约束（27 PK + 2 FK，基线 UNIQUE/CHECK 为 0；合成 UNIQUE/CHECK 测试分母为 31；唯一索引只计入 `indexes`；PG18 `contype='n'` 排除）、56 个索引和 vector/ltree 扩展注释覆盖验证为 100%，迁移元数据 checksum 注释单独校验，且重复执行 007/008/009 不改变结构、业务行数或代表性值。
