# 30 在线保留与归档

- 版本：v0.1（待评审）
- 范围：保留类、Hot/Archive/Cold、月度归档、manifest、授权恢复、冷检索和清除
- 对应需求：FR-072、BR-097、BR-098、BR-099
- 对应缺口：G-43

## 1. 原则

- 热数据在线，温数据归档，冷数据按权限恢复。
- 归档不改变租户隔离、数据分类、审计完整性或删除优先级。
- Q-030 软删除和 tombstone 优先于归档。
- 法律保留只冻结指定范围，不恢复用户访问。
- 归档失败不丢热数据，验证失败不退出在线查询。

## 2. 优先级

```text
Legal Hold / 安全事件保全
  > 软删除与 tombstone
  > 保留策略
  > 普通归档与冷存储
  > 性能缓存清理
```

- 删除与保留冲突时记录范围、理由、审批和到期时间。
- 秘密和认证材料不进入归档。
- 第三方提供商数据不受本策略控制。

## 3. 默认保留表

租户可按法规或套餐延长；缩短需要合规审批。

| 数据 | 在线期 | 归档/保留 |
|---|---:|---|
| 消息、私人聊天、群聊 | 90 天 | 归档 1 年，之后清除 |
| 任务、评审、争议和决策记录 | 180 天 | 归档 3 年 |
| 账本、模型用量、订阅财务记录 | 1 年 | 租户终止后保留 7 年 |
| 审计事件和检查点 | 1 年 | 租户终止后保留 7 年；WORM 按 Profile |
| 项目成果、源码快照、发布包 | 项目存续期 | 终止后归档 1 年，再进入清除审核 |
| 知识库、已发布 Skill | 公司存续期 | 长期保留，版本可追溯 |
| 工作记忆 | 任务结束后 7 天 | 清除 |
| 个人情节记忆 | 180 天 | 归档 1 年 |
| 经验证的语义/程序记忆 | 持续有效 | 过期后归档，不自动消失 |
| 日志 | 14 天 | 清除；安全审计日志除外 |
| 指标 | 30 天 | 聚合后长期保留 |
| Trace | 7 天 | 清除 |
| 原始模型 Prompt/响应缓存 | 最多 30 天 | 默认不归档 |
| 招聘候选人资料 | 决策后 180 天 | 之后清除，法律保留除外 |
| 员工绩效记录 | 离职后 1 年 | 再归档 3 年 |
| 安全事件证据 | 事件关闭后 1 年 | 再保留 3 年，法律保留优先 |
| 备份 | 启用后 35 天 | 到期清除 |

- 重要聊天结论必须提升为 Decision、任务或知识库内容。
- 项目成果到期后进入清除审核，不静默删除公司 IP。
- 软删除窗口保持账户 30 天、租户 7 天，不因归档延长或缩短。

## 4. RetentionPolicy

```text
id
tenant_id?
data_type
retention_class
online_days
archive_days
total_retention
action_on_expiry
legal_hold_supported
version
effective_from
```

- 平台默认策略可被租户策略覆盖。
- 策略版本绑定归档任务和 manifest。
- 策略变更不追溯改写已生成 manifest。
- 新策略只影响后续计算，历史记录保留原策略版本。

## 5. 存储分层

| 层 | 存储 | 查询 |
|---|---|---|
| Hot | Postgres 热分区、RustFS 标准对象 | 普通 API、Agent、搜索 |
| Archive | RustFS 归档前缀，压缩加密 | 显式归档查询 |
| Cold | RustFS 冷存储层或独立冷前缀 | 异步恢复后查询 |

- 不引入新的存储后端抽象。
- RustFS 通过 S3 SDK 和生命周期能力实现。
- 冷存储不可用时退化为独立冷前缀。
- 所有对象继续带租户和分类元数据。

## 6. 归档格式

- 结构化数据：JSONL + Zstandard。
- 清单：`manifest.json`。
- 二进制对象：保留原始 digest，复制到 RustFS 归档区。
- Git 数据：Git Bundle。
- 审计：只压缩容器，不改变 canonical payload。

manifest 包含：

```text
archive_id
tenant_id
data_type
policy_version
schema_version
time_range
source_watermark
record_count
byte_count
files[]
sha256
encryption_ref
classification
created_at
```

## 7. 月度归档流程

```text
Select due data
  → Freeze watermark
  → Write archive objects
  → Verify counts and hashes
  → Commit ArchiveManifest
  → Remove from hot query
  → Detach hot partition
```

- 每月执行，可按数据类别分桶。
- 任务幂等，可断点续跑。
- 同一水位只产生一个有效 manifest。
- 部分上传保持 Pending，不暴露为可查询归档。
- 归档失败保留 Hot。
- 数量或 SHA-256 不一致时禁止切换。
- 消息 cursor 不得指向已退出在线查询的数据。
- 分区 Detach 前必须验证归档和引用完整性。

## 8. ArchiveManifest 与对象

```text
ArchiveManifest
  id、tenant_id、data_type、policy_version、time_range、watermark、
  record_count、status、verification_result、created_at

ArchiveObject
  id、archive_id、object_key、digest、size_bytes、compression、
  encryption_ref、classification、storage_tier
```

- manifest 提交后不可覆盖，修正生成新版本。
- Ready 归档对象不可覆盖。
- 对象键不包含 PII 或秘密。

## 9. 检索与恢复

- 普通 Agent、搜索和聊天只查 Hot。
- 归档查询需要 `archive.read` 能力和租户范围。
- 按租户、数据类型、时间范围和 manifest 检索。
- 恢复生成隔离临时副本，默认 7 天后清除。
- 恢复和下载全部审计。
- 冷归档异步恢复，不阻塞在线业务。
- 跨租户检索必须拒绝。
- 归档内容不自动进入员工记忆或模型上下文。

## 10. ArchiveRestoreJob

```text
id
archive_id
requested_by
purpose
status
restore_prefix
expires_at
created_at
```

状态：`Requested → Restoring → Ready → Expired → Purged`。

- 恢复对象使用独立加密密钥和临时权限。
- 到期自动撤销访问并清除。
- 失败可重试，不留下半可读结果。

## 11. 清除

- 归档到期后进入 PurgeEligible。
- 项目成果、知识库和长期审计必须经过策略审核。
- 物理清除覆盖 Archive 和 Cold 对象。
- 清除步骤幂等并生成 DeletionProof。
- 清除失败保持可见状态并重试。
- 归档清除不得删除最小审计和 tombstone。

## 12. 法律保留

- LegalHold 记录范围、理由、授权人、开始时间和到期时间。
- 只冻结匹配对象，不冻结整个租户。
- 保留数据加密、限权且不可普通访问。
- 保留释放后自动进入清除队列。
- WORM Profile 的 Object Lock 与 LegalHold 必须一致。
- 保留冲突进入安全或合规事件。

## 13. 审计归档

- 保留连续 seq、prev_hash、event_hash 和签名检查点。
- 容器压缩不改变 canonical payload。
- 归档后仍可重算哈希链和 Merkle root。
- 审计验证失败进入安全事件。
- 租户终止后审计保留 7 年，法律保留可延长。
- 不把业务正文加入归档清单或日志。

## 14. 消息与 cursor

- 消息在线 90 天，之后进入 Archive。
- 在线消费者只能读取 Hot cursor 范围。
- 已归档消息通过归档查询读取。
- 重要结论提升为正式 Decision 或知识库版本。
- 私人聊天归档仍保持原参与者权限。
- 员工离职或离开项目后不恢复归档访问权。

## 15. 记忆归档

- 工作记忆任务结束后 7 天清除。
- 个人情节记忆 180 天后归档 1 年。
- 语义和程序记忆按有效期与版本状态归档。
- 安全、制度和合规知识长期保留。
- 原始证据进入归档，摘要保留 derived_from。
- Archived 记忆不进入普通召回，只能授权追溯。

## 16. 遥测与模型缓存

- 日志 14 天、指标 30 天、Trace 7 天。
- 安全审计日志按审计保留类处理。
- 指标聚合数据可长期保留。
- 原始模型 Prompt/响应缓存最多 30 天且默认不归档。
- 需要长期保留的模型产出必须显式保存为 Artifact。
- 遥测删除请求必须传播到 local、otlp 和 elastic 后端。

## 17. API 与 Tool

- `GET /archive/manifests`：查询归档清单。
- `POST /archive/restore`：申请恢复。
- `GET /archive/restores/{id}`：查看恢复状态。
- `DELETE /archive/restores/{id}`：提前清除临时副本。
- `POST /admin/retention-policies`：配置策略。
- `POST /admin/legal-holds`：创建法律保留。
- `DELETE /admin/legal-holds/{id}`：释放保留。
- 普通员工不能调用归档管理 Tool。

## 18. 审计事件

- `ArchiveStarted`
- `ArchiveVerified`
- `ArchiveFailed`
- `HotPartitionDetached`
- `ArchiveRestoreRequested`
- `ArchiveRestoreCompleted`
- `ArchiveRestoreExpired`
- `ArchivePurged`
- `RetentionPolicyChanged`
- `LegalHoldApplied`
- `LegalHoldReleased`

## 19. 指标

| 指标 | 目标 |
|---|---|
| 归档延迟 | 持续下降 |
| 归档验证失败数 | 0 |
| 在线期超时未归档数 | 0 |
| 归档恢复成功率 | 持续提升 |
| 越权归档访问数 | 0 |
| 逾期未清除数 | 0 |
| 临时恢复副本超期数 | 0 |

## 20. 测试场景

1. 消息 90 天后退出 Hot。
2. 月边界和闰月归档正确。
3. 重复归档任务不产生重复 manifest。
4. 部分上传不暴露为 Ready。
5. 数量或哈希不一致时保留 Hot。
6. 审计归档保持链可验证。
7. 普通 Agent 不能查询 Cold。
8. 跨租户归档恢复被拒绝。
9. 临时恢复副本 7 天后清除。
10. 软删除数据不被后续归档复活。
11. 法律保留只冻结匹配范围。
12. 保留释放后进入清除队列。
13. 清除失败可重试且不误报完成。
14. 消息 cursor 不指向已归档数据。
15. 策略变更不改写历史 manifest。
16. 日志、指标和 Trace 按各自期限清除。

## 21. 验收场景

1. 每类数据具有版本化保留策略。
2. Hot、Archive、Cold 查询边界明确。
3. 月度归档具备水位、数量、哈希和清单验证。
4. 归档失败不丢热数据。
5. 审计归档保持完整可验证。
6. 冷归档恢复需授权、隔离和审计。
7. 临时副本默认 7 天清除。
8. 删除优先级高于归档。
9. 法律保留不恢复访问。
10. 在线、归档和冷存储容量可观测。
