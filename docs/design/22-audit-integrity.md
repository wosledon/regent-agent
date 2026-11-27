# 22 审计完整性

- 版本：v0.1（待评审）
- 范围：审计 Profile、哈希链、Merkle 检查点、签名、WORM、外部锚定和验证
- 对应需求：FR-064、BR-071、BR-072、BR-073
- 对应缺口：G-35

## 1. 目标与边界

- 审计不仅禁止修改，还要在数据库被篡改后发现。
- 默认 standard，不强制外部服务。
- 审计不保存秘密和完整 PII。
- 更正通过新事件完成，不修改历史。
- 验证失败进入安全事件，不静默修复数据库。
- 单机方案不能抵抗数据库、签名密钥和锚点同时被控制。

## 2. 完整性 Profile

| Profile | 能力 | 场景 |
|---|---|---|
| standard | 追加写、哈希链、每日签名 Merkle 检查点 | 默认自托管 |
| worm | standard + RustFS Object Lock/法定保留 | 合规要求 |
| anchored | worm + 外部独立 root 锚定 | 强审计、企业客户 |

Profile 版本化并记录在审计和 ReleaseManifest 中。

## 3. 审计事件

```text
id
tenant_id
stream_id
seq
event_type
actor_type
actor_id
action
target_type
target_id
occurred_at
correlation_id
policy_version
payload_hash
prev_hash
event_hash
schema_version
```

- 业务状态与审计事件通过同一事务 Outbox 提交。
- 审计角色只有 INSERT/SELECT。
- 更正事件引用原事件 ID，不覆盖原文。
- 删除源数据时追加删除事件，不删除审计事件。
- 事件正文只包含脱敏元数据。

## 4. 哈希链

```text
event_hash = SHA-256(canonical_payload + previous_event_hash)
```

- 每个租户、每条审计流独立链。
- seq 连续，缺口单独告警。
- 使用规范化序列化，保证跨环境 hash 一致。
- 按租户和时间分片，避免全局单链瓶颈。
- 链头、算法版本和密钥版本随检查点记录。

### 4.1 PostgreSQL 时间精度兼容

- 新审计事件先把 `CreatedAt` 舍入到 PostgreSQL `timestamptz` 的微秒精度，再计算哈希，确保写入后读回值可重算。
- 旧版本 PostgreSQL 事件使用 100 纳秒 tick 计算哈希，而数据库只保留微秒。升级不覆盖这些历史 `Hash`；验证器仅在 Npgsql 路径接受“存储微秒加上 PostgreSQL 舍入窗口内的候选 tick 可以重放原哈希”的旧记录，并在验证消息中报告兼容记录数。
- 严格验证可禁用该兼容窗口；任何无法由存储值或有限候选 tick 重放的哈希仍按篡改失败。该策略不需要回填或改写历史审计行。

## 5. AuditCheckpoint

```text
checkpoint_id
stream_id
from_seq
to_seq
merkle_root
event_count
created_at
signing_key_id
signature
profile
```

- 默认每日生成，高流量流可缩短间隔。
- 使用独立审计签名密钥。
- 密钥与运行身份分离，支持轮换和吊销。
- 检查点写入版本化 RustFS。
- worm Profile 下启用对象保留或法定保留。

## 6. Merkle 验证

验证器重算：

- 单条事件 hash。
- prev_hash 链。
- seq 连续性。
- Merkle root。
- 检查点签名。
- RustFS 对象版本和保留状态。

结果：

```text
Verified
GapDetected
HashMismatch
SignatureInvalid
CheckpointMissing
RetentionViolation
```

验证失败进入安全事件，不自动改写数据库或检查点。

## 7. 外部锚定

anchored Profile 将每日 root 发送到独立位置：

- 客户控制的 S3/RustFS。
- 独立时间戳服务。
- 透明日志或审计见证方。

只发布：

```text
root hash
seq 范围
时间
签名
```

不发布业务内容、Prompt、源码或 PII。

- 锚定失败进入告警和补发队列。
- 不阻塞普通业务。
- 合规 Profile 可阻止高影响动作或发布。
- 锚点确认记录与检查点关联。

## 8. WORM 与备份

- RustFS 对象保留和法定保留用于审计导出、检查点和备份 manifest。
- 数据库备份不能替代哈希链和独立检查点。
- 审计在租户终止后保留 7 年，法律保留可延长。
- 审计归档只压缩容器，不改变 canonical payload 和哈希链。
- 备份 manifest 和哈希纳入防篡改保护。
- 删除源数据不删除审计事件。
- 软删除和物理清除通过 tombstone 与 DeletionProof 关联审计。
- 法律保留优先于普通物理清理。
- 恢复后必须重新验证链和检查点。

## 9. 权限分离

- 应用角色：写业务事件，不直接改审计表。
- 审计写入角色：只能 INSERT。
- 验证角色：只读。
- 签名密钥管理员：独立授权。
- 运营支持：默认不可读取审计正文。
- 员工不能直接写审计或修改检查点。
- 高影响组织、预算和审批事件可要求额外签名。

## 10. 验证调度

- 写入时检查 seq 和 prev_hash。
- 每小时增量验证最近事件。
- 每日全量检查点验证。
- 每周独立验证抽样或全量。
- 数据库恢复、升级和密钥轮换后强制验证。
- 验证任务有租约、幂等和审计。

## 11. 威胁与证明边界

| 威胁 | standard | worm | anchored |
|---|---|---|---|
| 普通员工篡改 | 可阻止 | 可阻止 | 可阻止 |
| 数据库管理员改单条记录 | 检查点可发现 | 检查点可发现 | 独立锚定可发现 |
| 删除或重排事件 | seq/hash 可发现 | seq/hash 可发现 | seq/hash 可发现 |
| 删除本地检查点 | 备份可发现 | WORM 降低风险 | 外部 root 可发现 |
| DB + 签名密钥同时被控制 | 不能完全证明 | 不能完全证明 | 需外部锚定增强 |

权限分离、密钥分离和外部锚点共同扩大信任边界。

## 12. 指标

| 指标 | 目标 |
|---|---|
| 链验证成功率 | 100% |
| seq 缺口 | 0 |
| 检查点滞后 | 持续下降 |
| 外部锚定滞后 | 按 Profile 门槛 |
| 签名失败 | 0 |
| WORM 保留违规尝试 | 0 |
| 篡改事件 | 0 |
| 验证覆盖率 | 100% |

## 13. API 与 Tool

- `GET /audit/streams`：查看审计流。
- `GET /audit/events`：查询脱敏事件。
- `POST /audit/checkpoints`：手动创建检查点。
- `POST /audit/verify`：发起验证。
- `GET /audit/verifications/{id}`：查看报告。
- `POST /audit/anchors`：配置外部锚点。
- `GET /audit/anchors/{id}/status`：查看锚定状态。
- 普通员工不能修改审计、签名密钥或 Profile。

## 14. 测试场景

1. 修改历史事件触发 HashMismatch。
2. 删除中间事件触发 GapDetected。
3. 重排 seq 被拒绝或发现。
4. 重放旧事件不产生重复审计。
5. 分叉链被验证器发现。
6. 伪造检查点签名失败。
7. 签名密钥轮换后旧检查点仍可验证。
8. Object Lock 阻止删除和覆盖。
9. 外部锚定中断后自动补发。
10. 数据库恢复后链验证通过。
11. 审计角色 UPDATE/DELETE 被拒绝。
12. 审计事件不包含秘密或 PII。

## 15. 验收场景

1. 默认 Profile 不依赖外部服务。
2. 审计角色无法修改历史事件。
3. 每个流 seq 连续且 hash 可重算。
4. 检查点具有签名和 Merkle root。
5. worm Profile 下检查点不可提前删除。
6. anchored Profile 只发布 root 元数据。
7. 验证失败生成安全事件。
8. 数据库恢复后执行完整验证。
9. 审计和锚点不包含敏感原文。
10. 验证报告可用于独立审计。
