# 16 部署、升级与迁移

- 版本：v0.1（待评审）
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

- 生产禁止应用启动时自动迁移。
- 独立 Migration Job 使用迁移角色。
- Postgres advisory lock 保证单实例执行。
- EF Core migration history 记录版本和 checksum。
- 迁移前备份，失败立即停止发布。
- 设置锁超时和语句超时。
- 大表回填分批、限速、可恢复。
- 回填记录进度、处理行数和校验和。
- 数据迁移先 dry-run，再正式执行。
- 迁移角色不用于日常运行。

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
