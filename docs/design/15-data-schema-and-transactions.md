# 15 数据 Schema 与事务边界

- 版本：v0.1（待评审）
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
| 评测 | `benchmark_suites`、`dataset_versions`、`evaluation_runs`、`case_results`、`judge_assignments`、`judge_calibrations`、`contamination_findings`、`statistical_gates`、`evaluation_retests` |
| 可靠性 | `outbox`、`inbox`、`audit_events` |

模块拥有自己的表，其他模块不能直接写入。跨模块协作通过应用命令和事件完成。

## 5. 字段与命名

- 主键使用 UUID。
- 时间使用 `timestamptz`，业务时间统一 UTC。
- 金额使用 `numeric(18,8)`，Token 使用 `bigint`。
- 可变聚合使用 `row_version` 乐观并发。
- JSONB 只存扩展元数据，不承载核心业务约束。
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
