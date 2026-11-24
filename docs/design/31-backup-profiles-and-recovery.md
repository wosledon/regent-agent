# 31 可选备份与恢复

- 版本：v0.2（本地组件作业、有效 RPO/RTO 与双人恢复演练已验收）
- 范围：BackupProfile、组件开关、有效 RPO/RTO、异地副本、恢复审批和可选演练
- 对应需求：FR-073、BR-100、BR-101、BR-102、BR-166
- 对应缺口：G-44

## 1. 优先级与原则

- 备份默认关闭，实施后置，不作为 MVP 优先项。
- ComplianceProfile 可将指定备份组件设为发布或运行必需控制；未启用时相关动作 fail closed，不得伪造备份成功。
- 每个组件独立开关，不搞全有或全无。
- 未启用组件不承诺 RPO/RTO，不计入备份成功指标。
- 启用后按实际备份覆盖和最近成功证据计算有效 RPO/RTO。
- 归档、审计、RustFS 持久化和软删除不是备份，不受开关影响。

## 2. BackupProfile

```text
id
deployment_id
enabled
postgres_full
postgres_pitr
rustfs
keyvault
offsite
restore_drills
schedule
retention_days
target_ref?
version
```

- `enabled=false` 时所有备份作业和演练停止。
- 默认 `retention_days=35`。
- 配置版本化，变更写入审计。
- 普通租户不能修改部署级备份配置。
- 自托管由部署管理员控制，托管由授权 SRE 控制。

## 3. 开关依赖

| 开关 | 依赖 | 说明 |
|---|---|---|
| postgres_full | enabled | 每日一致性快照 |
| postgres_pitr | enabled + postgres_full | WAL/PITR |
| rustfs | enabled | 对象版本、清单和快照 |
| keyvault | enabled | Postgres `keyvault` schema 的密文和包装数据密钥备份 |
| offsite | 至少一个本地备份开关 | 异地复制 |
| restore_drills | 至少一个可恢复组件 | 恢复演练 |

- 开启子开关时自动要求 master enabled。
- 关闭 master 时保留配置但停止执行。
- 依赖不满足时拒绝激活并返回缺失项。
- 不自动开启用户未选择的组件。

## 4. 便捷 Profile

开关是权威配置，预设只用于批量填充：

| 预设 | 内容 |
|---|---|
| off | 全部关闭，默认 |
| local | Postgres 全量 + RustFS + 密钥库 |
| standard | local + PITR + 月度演练 |
| offsite | standard + 异地 + 季度全量演练 |

- 预设不锁定配置，用户仍可逐项调整。
- 部署报告显示实际开关，不只显示预设名称。

## 5. 目标与有效值

| 组件 | 配置目标 RPO | 配置目标 RTO |
|---|---:|---:|
| Postgres 全量 | 24 小时 | 8 小时 |
| 账本与审计 PITR | 15 分钟 | 4 小时 |
| RustFS | 24 小时 | 8 小时 |
| 密钥库 | 15 分钟 | 2 小时 |
| 配置与 ReleaseManifest | 0 | 1 小时 |

- 配置目标不等于已实现能力。
- 有效 RPO 取最近成功恢复点与当前时间的差。
- 有效 RTO 来自实际恢复演练。
- 无成功备份或演练时显示 `unverified`。
- master 关闭时显示 `not_configured`，不显示 0 或虚假达标。

## 6. 备份内容

启用后按开关包含：

- Postgres 业务库和运营库。
- WAL/PITR 日志。
- RustFS 对象清单、版本和快照。
- Postgres `keyvault` schema 的加密密钥库。
- ReleaseManifest、基础镜像 digest、本地镜像 ID、Schema 和配置版本。
- 审计检查点、签名和锚定状态。

Runner 本地工作区不备份，从 RustFS 快照恢复。

## 7. 频率与保留

- Postgres 全量默认每日一次。
- WAL 连续归档。
- RustFS 每日清单和版本快照。
- `keyvault` schema 每次变更后备份，每日再快照；主密钥单独保管。
- 配置和 ReleaseManifest 每次发布生成。
- 滚动备份最长 35 天。
- 长期保留由 Q-031 归档承担。
- WAL 保留覆盖 35 天恢复窗口。

## 8. 异地副本

- 托管可启用异地加密副本。
- 自托管必须显式选择外置磁盘、NAS 或对象存储。
- 同盘副本不能标记为 offsite。
- 异地状态单独监控，不冒充本地备份成功。
- 关闭 offsite 不影响本地备份。

## 9. 一致性与加密

- 使用备份 ID 关联 Postgres、RustFS 和密钥库。
- 记录快照时间、LSN、对象清单和密钥版本。
- 备份使用独立备份密钥加密。
- 主密钥与备份密文分离保管。
- 托管主密钥恢复需要双人审批。
- 校验 SHA-256、记录数和 manifest。
- 密钥库备份不包含明文主密钥。

## 10. 恢复

```text
Check configured coverage
  → Approve
  → Isolate environment
  → Restore enabled components
  → Replay tombstone
  → Verify references and hashes
  → Rebuild indexes
  → Smoke test
  → Resume
  → Report effective RPO/RTO
```

- 未配置备份时返回 `not_configured`。
- 组件缺失时拒绝标记为完整恢复。
- 不在生产库上直接覆盖。
- 支持单租户和全量恢复。
- 恢复后重放 Outbox/Inbox 和 tombstone。
- 校验账本、审计链、对象 digest 和密钥版本。
- RTO 按验证通过并恢复服务计算。

## 11. 升级集成

- 升级 Backup 步骤读取 BackupProfile。
- master 关闭时可跳过，但必须记录 `backup_step=skipped`。
- 跳过需要明确风险接受人、理由和时间。
- 不得伪造备份成功或沿用旧作业状态。
- Contract 前报告当前备份和恢复状态。
- 备份启用时，Contract 前必须验证最近备份可读。

## 12. 恢复演练

- 默认关闭。
- 可配置每周抽样、每月单租户、每季度全量。
- 密钥恢复演练需双人参与。
- 演练在隔离环境执行。
- 验证数据完整性，不只检查进程启动。
- 输出实际 RPO/RTO 和失败步骤。

## 13. 告警与指标

仅对启用组件告警：

- 超过 26 小时无成功全量备份。
- WAL 滞后超过 10 分钟预警，15 分钟告警。
- 异地副本超过一个周期。
- 哈希或清单验证失败。
- 恢复演练失败。

关闭组件不产生“备份失败”告警，也不计入成功率分母。

## 14. BackupJob 与 RestoreJob

```text
BackupJob
  id、profile_version、component、status、started_at、completed_at、
  snapshot_ref、manifest_ref、hash、retention_until

RestoreJob
  id、backup_ids、scope、approval_ref、status、restore_point、
  verified_at、rpo_actual、rto_actual、report_ref
```

- 作业幂等，可重试。
- 失败状态保留错误和部分结果。
- 恢复报告绑定审批、备份代次和验证证据。

## 15. API

- `GET /ops/backup-profile`。
- `PUT /ops/backup-profile`。
- `POST /ops/backup-profile/validate`。
- `POST /ops/backups/run`。
- `GET /ops/backups`。
- `POST /ops/restores`。
- `GET /ops/restores/{id}`。
- `POST /ops/restore-drills/run`。

## 16. 测试场景

1. 默认 Profile 全部关闭。
2. master 关闭时不产生备份作业。
3. 子开关自动要求 master enabled。
4. PITR 需要 Postgres 全量。
5. offsite 需要至少一个本地备份。
6. 关闭组件不计入备份成功率。
7. 未配置时恢复返回 not_configured。
8. 组件缺失时不能标记完整恢复。
9. 有效 RPO 来自最近成功恢复点。
10. 无演练时 RTO 显示 unverified。
11. 备份恢复后 tombstone 生效。
12. 同盘副本不能标记 offsite。
13. 升级跳过备份具有风险接受记录。
14. 备份密文不包含明文主密钥。
15. 35 天后滚动备份清除。
16. 归档和软删除不受 master 开关影响。

## 17. 验收场景

1. 所有备份能力可独立开关。
2. 默认关闭且不承诺 RPO/RTO。
3. 启用组件具有可验证目标。
4. 有效 RPO/RTO 来自实际证据。
5. 异地和演练可单独启用。
6. 升级可显式跳过并记录风险。
7. 恢复前检查配置覆盖。
8. 恢复后验证一致性和 tombstone。
9. 关闭组件不产生虚假指标。
10. 备份实施不阻塞核心 MVP。

### 17.1 当前本地实现

- BackupProfile 支持 Postgres 全量/PITR、RustFS、KeyVault、Offsite 和演练开关，并校验 master、PITR、Offsite 与演练依赖。
- 每个启用组件生成独立 BackupJob、manifest、SHA-256、快照引用和保留期限；本地无对应组件时返回 Blocked，不伪造成功。
- 有效 RPO 只在所有启用组件都有成功作业时计算；有效 RTO 只来自已验证恢复演练，否则显示 `unverified`。
- 恢复请求必须带审批引用，批准人不得与请求人相同；批准时重新读取 manifest 并校验哈希，输出隔离本地验证报告。
- Ops Console 展示组件作业、有效 RPO/RTO、恢复审批和双人演练；未连接真实 Postgres/RustFS/KeyVault，也不执行生产覆盖恢复。
