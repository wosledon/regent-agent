# 16 部署、升级与迁移

- 版本：v0.6（schema 9 Expand 基线）
- 范围：Compose/systemd、版本集、升级顺序、数据库迁移、混版本、回滚与恢复演练
- 对应需求：FR-058、BR-053、BR-054、BR-055
- 对应缺口：G-05、G-16、G-20、G-21

## 1. 部署目标

- MVP 和自托管采用单机 Docker Compose + systemd 监管。
- 托管环境复用相同镜像和发布协议，后续可替换编排器。
- 四部分、两个前端、Postgres 和 RustFS 作为一个版本集发布。
- 升级不得依赖应用启动时自动迁移。
- 应用回滚和数据库回滚必须明确区分。

## 2. 部署拓扑

```text
systemd
  └─ Docker Compose
       ├─ Postgres
       ├─ RustFS
       ├─ company-controlplane
       ├─ company-gateway
       ├─ company-runner
       ├─ company-operations
       ├─ tenant-dashboard
       └─ ops-console

可选 observability-elastic Profile：
       ├─ elasticsearch
       ├─ kibana
       ├─ apm-server
       └─ otel-collector
```

- 默认 Compose 不启动可选遥测组件。
- GitHub 只打版本 Tag，GitHub Actions 只检查和测试，不发布镜像。
- 本地从批准 Tag 检出源码，用 Docker Compose 构建；基础镜像尽量按 digest 固定。
- 配置通过环境变量或 secret 文件注入。
- 密钥不写入镜像、Compose 明文或普通配置。
- 容器使用非 Root 用户、只读根文件系统和显式数据卷。
- 两个前端保持独立认证、授权和审计。

## 3. ReleaseManifest

```text
release_version
version_tag
source_commit
base_image_digests[]
database_schema_version
config_schema_version
observability_profile
audit_integrity_profile
api_versions[]
event_schema_versions[]
minimum_runner_version
compatible_previous_versions[]
backup_required
contract_migration_required
```

发布包必须包含：

- ReleaseManifest。
- Compose 文件。
- 环境变量模板。
- 迁移清单。
- 健康检查命令。
- 升级和回滚说明。
- 已知不兼容项。
- TestEvidence、安全/容量/混沌/恢复测试摘要。
- LICENSE、NOTICE、SBOM 和 ReleaseLicenseReport。
- 版本 Tag、源 commit 和本地构建记录，详见 [39 GitHub CI/CD 与本地部署](./39-github-actions-and-local-compose.md)。
- install、upgrade、validate、rollback 和 status 脚本，离线缓存与失败恢复见 [43 自托管安装与离线升级](./43-self-hosted-install-and-offline-upgrade.md)。

## 4. 版本兼容

- 四个服务作为同一版本集发布。
- 升级期间只允许 N 与 N-1 混跑。
- N-2 Runner 拒绝注册。
- API 使用 `/v1` 主版本路径。
- 新增字段向后兼容；删除、改语义和收窄类型属于破坏性变更。
- 事件消费者忽略未知字段，拒绝不支持的主版本。
- Runner 声明最低控制面版本，旧 Runner 只完成已认领任务。
- 配置具有 schema 版本，旧版本忽略未知键以避免阻塞回滚。

## 5. 升级前置检查

```text
ReleaseManifest 兼容矩阵
Postgres/RustFS 健康
磁盘和备份空间
当前 schema/config schema
observability profile 和 Exporter 配置
审计 Profile、最近检查点和验证状态
未完成迁移
在途任务和模型调用
密钥库和备份可用性
```

关键检查失败时不开始升级。检查结果写入发布审计。

## 6. 升级流程

### 6.1 Drain

- 停止接收新任务。
- 停止新的模型调用。
- 等待在途调用结束或到达 TaskCheckpoint。
- Runner 不再认领新工作。
- 保留旧服务健康直到备份完成。

### 6.2 Backup

- 读取 BackupProfile；默认关闭，不作为升级优先项。
- 启用时执行 Postgres 一致性备份、RustFS 版本检查、`keyvault` schema 加密备份和清单校验。
- 关闭时记录 `backup_step=skipped`、风险接受人、理由和时间。
- 不得伪造备份成功或沿用旧作业状态。
- 最近审计检查点、签名和锚定状态始终记录，不属于可选备份。
- 备份启用时在 Contract 前验证最近备份可读。

### 6.3 Expand Migration

只执行向后兼容变更：

- 新增表。
- 新增可空列或带默认值列。
- 新增索引。
- 历史数据满足约束后才新增 CHECK。

禁止同一发布执行：

- 删除列或表。
- 重命名列。
- 收窄类型。
- 删除旧状态值。
- 不可逆大回填。

### 6.4 Deploy

推荐顺序：

```text
ControlPlane
  → AI Gateway
  → Runner
  → TenantDashboard
  → Operations
```

- 新 Runner 注册并验证能力。
- 旧 Runner 完成 drain 后退出。
- 每步执行 Ready 检查和最小 smoke test。
- 任一步失败时停止后续部署。

### 6.5 Resume

- 恢复任务认领。
- 恢复模型调用。
- 观察错误率、消息、账本和 RustFS 上传。
- 发布状态写入审计。

### 6.6 Contract

- 所有实例升级到 N 后才允许 Contract。
- Contract 放在后续发布，不与 Expand 同版本。
- 删除旧结构前再次备份并确认 N-1 已退出。
- Contract 后旧应用不能回滚。

## 7. 数据库迁移

- 所有环境禁止 PostgreSQL 应用启动时自动迁移、建表、版本引导或演示种子；启动只读校验 schema 9 的扩展、精确列契约、checksum 列与历史、RLS 策略和运行角色边界。SQLite/InMemory 的 Development 初始化不受此限制。
- 独立 Migration Job 使用迁移角色；迁移角色必须拥有应用表，并显式获得目标 schema 的 `USAGE`/`CREATE`，其中 schema `CREATE` 是创建或替换函数等迁移对象的前置权限，不能由 schema Owner 身份隐式替代。运行角色不得拥有或继承任一应用表/schema Owner 角色，不得具有 `BYPASSRLS`、superuser、`CREATEROLE`、`CREATEDB`、schema CREATE、审计表写权或 SchemaVersions 写权；直接属性和角色继承均在启动时检查。
- `scripts/migrate-postgres.py` 是唯一 Expand 执行入口：执行前读取 checked-in checksum manifest，文件、版本和 SHA-256 集合必须精确匹配，篡改 SQL 在任何 DDL 前失败。空数据库通过 `to_regclass` 检测缺失的 `SchemaVersions` 并从 003 开始；在同一个 psql 会话中获取基础锁及旧版逐迁移锁 `6317000 + target`，重新读取并验证历史、应用全部待执行迁移、仅回填 NULL checksum、断言历史版本集合与最新版本完全匹配后按逆序释放 lock，同时设置 `lock_timeout` 和 `statement_timeout`。`ON_ERROR_STOP=1` 在失败时结束 psql，连接关闭释放 session lock。Npgsql 关键字连接串在调用 psql 前显式转换为 libpq 形式。
- Schema 8 的 `SchemaVersions.Checksum` 记录迁移文件 SHA-256。`deploy/sql/controlplane-checksums.json` 是 checked-in manifest 记录 003～009 的精确 SHA-256，并作为嵌入资源随 ControlPlane 发布；`scripts/check-migration-checksums.py` 在 CI 中重新生成并校验。runner 只回填 NULL 值，绝不覆盖非 NULL 校验和；发现非 NULL 漂移立即失败。008 已提交但回填前中断的数据库可在下一次运行自动恢复，历史版本缺失或最新迁移未写入版本行时失败关闭。所有已知版本在成功结束前都必须与 manifest 精确匹配。
- 迁移前备份，失败立即停止发布；大表回填分批、限速、可恢复，并记录进度、行数和校验和。数据迁移先 dry-run，再正式执行；迁移角色不用于日常运行。`--dry-run`/`--check` 只校验迁移文件与事务，Expand 阶段还校验 manifest、版本和 SHA-256，并统计 `FORCE ROW LEVEL SECURITY` 声明；它们不连接数据库，不能验证 RLS 是否生效或策略是否精确。精确 RLS/策略由 PostgreSQL 集成测试和所有环境的启动只读校验执行。
- 当前 Expand 基线为 `controlplane-003.sql`～`controlplane-009.sql`，SchemaVersion 为 9。007 增加 `RequesterUserId`、`TargetEmployeeId`、`TargetCandidateId`、`TargetPositionId`、`ActionHash`、`ConsumedAt`、`ConsumedBy` 七个审批字段；008 增加 `DecidedBy`、`DecidedAt`、`NextAttemptAt`、`LastError` 四个业务字段和 `SchemaVersions.Checksum` 元数据列；009 为 `Milestones` 增加 `TenantId`、强制 RLS、`(TenantId, ProjectId)` 复合外键和 N-1 兼容派生触发器，旧二进制省略 `TenantId` 时仍从项目派生，但非空、复合外键和 canonical 策略保持不变。`controlplane-004.sql` 的 `Memberships_user_read` 是唯一允许的额外 permissive SELECT 策略；启动校验要求其恰好一个、命令为 SELECT、角色为 PUBLIC，且 `UserId/app.user_id` 表达式精确匹配，缺失、复制、弱化、命令或角色错误均失败。
- `controlplane-005.sql` 安装/确认 vector 与 ltree 扩展，创建 `MemoryEmbeddings`、HNSW 余弦索引和强制 RLS，并把 `Positions.Path` 回填为规范 ltree 后设置 `NOT NULL`、创建 GiST 索引。回填在租户事务上下文内执行，遇到环、缺失/跨租户父节点或超过四级岗位时整份迁移失败。
- `deploy/sql/runtime-grants.sql` 先撤销运行角色和 PUBLIC 在当前 schema 的 CREATE、表/序列及列级权限，再授予业务表 CRUD；`AuditRecords`/`AuditCheckpoints` 仅 SELECT/INSERT，`SchemaVersions` 仅 SELECT。CI 在同一 runner 应用 003–009 后执行该脚本，不再使用宽泛的 `GRANT ... ON ALL TABLES`。
- `PostgresMigrationTests` 的临时 schema 迁移契约测试覆盖 003–009、表 27/27、业务用户列 231→238→242→243、基线 `p`/`f`/`u`/`c` 约束 29/29（27 PK + 2 FK，基线 UNIQUE/CHECK 为 0；合成 UNIQUE/CHECK 测试分母为 31；唯一索引只计入 `indexes`；PG18 `contype='n'` 不进入分母）、索引 55→56、扩展 2/2 的非空中文注释、006/007/008/009 重复执行与结构/业务行不变，以及 005 的环、缺失父节点、跨租户父节点和超深数据回滚。007/008/009 在应用前种子化代表性数据，并断言字段值、行数和租户隔离 canary；009 额外使用 `regent_controlplane` 运行角色执行不含 `TenantId` 的旧插入形状，验证触发器派生、强制 RLS 可见性、复合外键拒绝跨租户以及重复迁移后触发器仍唯一。009 还覆盖 `NOSUPERUSER`/`NOBYPASSRLS` 表 Owner 的多租户升级：测试保持 schema 由管理员所有，仅向表 Owner 授予 `USAGE`/`CREATE`，并验证该角色不直接或通过继承获得 superuser/`BYPASSRLS`；首次迁移前把 session-scoped `app.tenant_id` 设为最后一个租户，验证回填与复合外键校验不依赖该值，随后以另一个租户上下文重复应用 009 验证幂等。回填前临时移除 `Projects` 的 `FORCE ROW LEVEL SECURITY`，并使用一条不设置租户上下文的 set-based `UPDATE`，因此缺少 `NO FORCE` 会直接失败。事务提交前仍恢复 ENABLE/FORCE，并由同时检查 `Projects`/`Milestones` 的 guard 失败关闭，最终逐租户验证仅该租户里程碑可见、`TenantId` 回填、复合外键 `convalidated`、schema 非运行角色所有且 `Projects`/`Milestones` 强制 RLS。
- `PostgresMigrationTests` 拥有 deparse、注释、PG18、UNIQUE/CHECK 和 RLS/权限回归：PostgreSQL 17/18 验证 deparse 后 canonical 策略仍精确匹配，基线约束分母为 29/29（27 PK + 2 FK，基线 UNIQUE/CHECK 为 0），PG18 `contype='n'` 排除，合成 UNIQUE/CHECK 把分母提高到 31（唯一索引只计入 `indexes`），以及真实约束注释缺失失败；RLS 负例包含设置名大小写/空格变化和错误引号列。权限测试验证审计表 append-only、SchemaVersions 只读、schema 无 CREATE 和列级权限清理。
- `PostgresMigrationRunnerTests` 只拥有 runner/checksum/恢复/tamper/lock 行为：对空的可丢弃 PostgreSQL 数据库调用真实 runner，验证版本 003–009 和全部精确 checksum；重复运行验证 SKIP，篡改版本 7 checksum 后验证失败关闭，并覆盖 schema 3～7（含无 Checksum 列的 schema 7）升级到 9、008 后回填中断恢复、并发 runner、旧版逐迁移锁互斥和最新迁移未写版本行。Tampered SQL 测试验证 manifest 在任何建表前失败。
- CI 使用按 digest 固定的 pgvector PostgreSQL 17/18 一次性服务容器矩阵，以迁移角色调用同一 runner，再以运行角色执行迁移、RLS、权限和审批并发集成测试，并执行后端镜像 smoke build；前端 job 重新生成 Tenant Dashboard TypeScript schema 到临时文件并与跟踪文件 diff。
- 本地未配置 PostgreSQL DSN 时，真实数据库集成测试按环境门控跳过，不把跳过表述为通过；CI 的一次性 pgvector 服务容器执行真实迁移、RLS、权限和并发测试。未执行 live 或生产数据库迁移。

## 8. 应用回滚

允许应用回滚的条件：

- 数据库仍处于 Expand 阶段。
- 新版本没有写入旧版本无法读取的数据。
- API 和事件保持向后兼容。
- RustFS 没有删除旧版本需要的对象。
- 配置仍被旧版本接受。

回滚顺序：

```text
停止新任务
  → 回滚前端和 Operations
  → 回滚 Runner
  → 回滚 Gateway
  → 回滚 ControlPlane
  → smoke test
  → 恢复业务
```

禁止应用回滚的情况：

- 已执行 Contract Migration。
- 新数据无法被旧代码解析。
- 事件 schema 不兼容。
- 数据回填不可逆。

此时选择 Roll Forward，或按明确 RPO/RTO 停机恢复备份。数据库不自动回滚。

## 9. 配置兼容

- 配置具有 schema 版本。
- 新必填配置必须提供默认值或前置检查。
- 启动前执行 `regent-agent config validate`。
- 旧版本忽略未知配置键并记录警告。
- 连接串、key_ref 和 Bucket 名称保持稳定。
- 主密钥和运行秘密只从环境或 secret 文件读取；Provider API Key 密文从 Postgres `keyvault` schema 读取。
- 配置校验输出必须脱敏。

## 10. 健康检查

每个服务提供：

```text
/health/live
/health/ready
```

Ready 检查：

- Postgres 连接和 schema 兼容。
- RustFS 必需 Bucket 和权限。
- Postgres `keyvault` schema、Gateway 角色和主密钥注入可用。
- 必需配置有效。
- 当前版本支持运行。

Runner 额外提供：

```text
/drain
/status
```

## 11. 失败处理

| 失败点 | 处理 |
|---|---|
| 前置检查失败 | 不开始升级 |
| 备份失败 | 不开始升级 |
| Expand Migration 失败 | 停止发布，保留旧服务 |
| 服务健康检查失败 | 停止后续部署，应用回滚 |
| 新 Runner 注册失败 | 保留旧 Runner，不恢复新任务 |
| Smoke Test 失败 | 回滚或前滚，不恢复业务 |
| Contract 后发现不兼容 | 前滚或恢复备份 |
| RustFS 升级期间上传中断 | 保留 PendingUpload，恢复后重试 |

## 12. 升级演练

必须在类生产环境验证：

1. N-1 → N 正常升级。
2. Gateway 部署失败。
3. 迁移执行到一半失败。
4. 新旧 Runner 混跑。
5. 应用回滚但数据库保持 Expand。
6. Contract 后禁止应用回滚。
7. RustFS 上传在升级期间中断。
8. 从备份完整恢复。
9. 配置 schema 不兼容。
10. 事件消费者落后一个版本。
11. 旧 Runner 尝试注册到新控制面。
12. 迁移锁被重复执行。

## 13. 发布审计

记录：

- 操作者和审批。
- ReleaseManifest、基础镜像 digest 和本地镜像 ID。
- 前置检查结果。
- 备份引用和校验。
- 迁移版本、耗时和校验和。
- 每步部署状态。
- Smoke Test 结果。
- 回滚或前滚决定。
- 恢复后的 RPO/RTO。

## 14. 验收场景

1. 发布必须固定版本 Tag，并记录基础镜像 digest 和本地构建镜像 ID。
2. N-2 Runner 无法注册。
3. 升级前必须完成 Drain；备份启用时完成备份，关闭时记录风险接受。
4. Expand Migration 不删除旧结构。
5. 生产启动不会自动迁移。
6. 两个迁移 Job 不会并发执行。
7. 大表回填可以中断后恢复。
8. 服务部署失败会停止后续步骤。
9. Expand 阶段可以回滚应用。
10. Contract 后不能回滚旧应用。
11. 配置校验可以发现缺失和错误。
12. 旧版本可以忽略未知配置键。
13. API 和事件混版本可以正常工作。
14. RustFS PendingUpload 在升级后可以恢复。
15. 完整恢复演练产生 RPO/RTO 报告。
