# 29 账户与租户软删除

- 版本：v0.1（待评审）
- 范围：停用、账户注销、租户删除、软删除、恢复、物理清除、备份和法律保留
- 对应需求：FR-071、BR-094、BR-095、BR-096
- 对应缺口：G-42

## 1. 原则

- 删除请求立即逻辑生效，但数据在恢复窗口内加密保留。
- 软删除不是永久保存，窗口结束后必须物理清除。
- 停用、账户注销和租户删除是不同操作。
- 注销账户不得自动删除其他股东的公司。
- 删除、恢复和清除必须幂等、可审计且不可绕过。

## 2. 状态

```text
Account: Active ↔ Suspended
           └→ SoftDeleted → Purged

Tenant:  Active ↔ Suspended
           └→ SoftDeleted → Purged
```

- `Suspended` 保留数据且可恢复，不属于删除。
- `SoftDeleted` 对普通用户和 Agent 不可见；现有 Cookie 会话和 Assignment Token 均 fail closed，仅保留恢复和会话管理路径。
- 租户标识按 GUID 值比较，不依赖 SQLite/Postgres 格式化字符串的大小写。
- `Purged` 表示物理清除完成。
- 法律保留使用 `PurgedWithLegalHold` 或 `SoftDeletedWithLegalHold`。

## 3. 操作与权限

| 操作 | 发起人 | 恢复窗口 |
|---|---|---:|
| 账户停用 | 账户本人或授权管理员 | 无固定删除期限 |
| 账户注销 | 账户本人 | 30 天 |
| 租户删除 | Primary Owner | 7 天 |
| 租户暂停 | Primary Owner 或运营方 | 无固定删除期限 |

- 需要最近认证和显式确认。
- 运营人员不能代替 Primary Owner 删除租户。
- 法律或安全强制删除必须具有授权、范围、理由和审计。

## 4. 所有权阻塞

- 账户注销前必须处理全部租户成员关系。
- 账户是唯一 Primary Owner 时，必须先转让所有权或删除该租户。
- Co-Owner 注销只移除成员关系，公司继续存在。
- 租户删除只影响目标租户。
- 不能通过账户注销留下无主租户。
- 租户删除是最后所有者不变量的显式终止场景。

## 5. 软删除立即效果

请求受理后立即：

- 撤销账户或租户的全部登录会话和 Refresh Token。
- 禁止新登录、新任务、新模型调用和外部发布。
- 停止自动招聘、定时任务和新的写入。
- 从搜索、召回、聊天、通知和仪表盘隐藏。
- 撤销个人 API Token、外部集成 Token 和可用密钥。
- 将对象移入加密隔离区或标记不可读。
- 写入 deletion_request_id、deleted_at 和 purge_due_at。
- 通知其他所有者或管理员。

在途任务按安全检查点取消或结束，不产生新的外部副作用。

## 6. 数据隔离

- Account 和 Tenant 根记录是删除权威源。
- 普通查询必须校验根记录未删除。
- Postgres RLS 同时过滤 deleted_at 和 tenant_id。
- 普通应用角色不能读取隔离数据或修改 deleted_at。
- 向量、全文、图索引和缓存立即移除。
- 派生摘要、通知和导出文件立即失效。
- RustFS 对象进入隔离前缀并取消普通下载权限。
- 本地工作区和容器停止后清除或加密隔离。

软删除数据只能由删除、恢复、取证和合规角色访问。

## 7. 凭据处理

- 密码哈希和恢复码哈希保持禁用和隔离，恢复窗口内不参与登录。
- GitHub/OIDC Access Token 和 Refresh Token 立即撤销。
- 模型密钥、API Key、证书和第三方凭据立即撤销。
- 租户密钥不保留可用明文。
- 恢复后重新签发会话和必要凭据。
- 无法重新验证凭据时走 Q-027 找回流程。

## 8. 恢复

```text
SoftDeleted
  → 重新认证
  → 校验身份、所有权和恢复窗口
  → 检查 legal_hold/security_block
  → 恢复根记录
  → 重新计算成员和能力
  → 重建索引和缓存
  → 签发新会话
  → 写审计
```

- 账户注销窗口 30 天，租户删除窗口 7 天。
- 只有账户本人可以恢复账户。
- 只有 Primary Owner 可以恢复租户。
- purge_started_at 已设置或法律/安全阻止时拒绝恢复。
- 恢复不恢复已撤销的密钥和外部 Token。
- 恢复后建议轮换密码和恢复码。
- 重建索引失败时保持业务不可用并告警，不部分恢复。

## 9. 物理清除

恢复窗口结束后：

```text
PurgeScheduled
  → Purging
  → Verifying
  → Purged
```

清除范围：

- Postgres 业务数据和账户私有数据。
- RustFS 对象、快照和导出文件。
- 向量、全文、图索引和缓存。
- 遥测副本和 PII 令牌映射。
- 本地工作区和容器。
- 删除任务临时文件。

要求：

- 按依赖顺序分批执行。
- 每步幂等、可重试、可审计。
- 删除作业不能读取业务明文。
- 任何关键步骤失败都不能标记 Purged。
- 生成最终 DeletionProof。

## 10. 备份与 Tombstone

- 备份默认关闭；启用后滚动备份最长保留 35 天。
- 软删除立即写入持久化 tombstone，与备份开关无关。
- tombstone 默认保留 1 年，只包含不可逆标识、范围、时间和结果哈希。
- 备份到期后按计划物理清除。
- 恢复备份时必须先重放 tombstone 和软删除状态。
- 已 Purged 的数据不得重新进入普通查询。
- DeletionProof 列出尚未到期的备份及计划清除日期。
- WORM 或法律保留导致超期时记录原因、范围、审批和到期时间。

## 11. 法律保留

- 法律保留必须有范围、理由、授权人、开始时间和到期时间。
- 只保留必要数据，加密并严格限权。
- 非保留数据继续清除。
- 保留不恢复账户或租户访问。
- 保留项在 DeletionProof 中单独列出。
- 保留释放后自动进入物理清除队列。

## 12. 外部服务

- 删除 Regent 保存的 Prompt、响应、日志和缓存。
- 撤销 GitHub、OIDC 等已实现集成凭据。
- 不删除用户在外部平台的账号。
- 已发送给模型提供商的数据按外部政策处理。
- 产品必须说明不能保证撤回第三方已经保存的数据。
- 外部处理结果和限制写入 DeletionProof。

## 13. 审计与最小记录

业务数据清除后只保留：

- 随机 deletion_request_id。
- 账户或租户的不可逆标识。
- 请求、软删除、恢复、清除和备份清除时间。
- 删除范围、结果哈希和保留项。
- 不保留可还原业务内容、PII 原文或秘密。

审计事件仍按 Q-023 追加写，不随源数据删除。

## 14. DeletionRequest

```text
id
subject_type
subject_id
requested_by
reason_code
status
requested_at
deleted_at
restore_due_at
purge_started_at?
purged_at?
legal_hold?
tombstone_ref?
```

状态：

`Requested → SoftDeleted → PurgeScheduled → Purging → Purged`

可恢复分支：`SoftDeleted → Active`。

## 15. DeletionProof

```text
id
deletion_request_id
scope
logical_deleted_at
completed_steps
failed_steps
retained_items
backup_purge_dates
external_limitations
created_at
result_hash
```

- 软删除完成生成阶段性回执。
- 物理清除完成生成最终证明。
- 法律保留生成带保留项的证明。
- 证明本身不包含业务内容和秘密。

## 16. API 与事件

- `POST /account/deactivation`。
- `POST /account/deletion`。
- `POST /account/deletion/restore`。
- `POST /tenants/{id}/deletion`。
- `POST /tenants/{id}/deletion/restore`。
- `GET /deletions/{id}`：查看状态和证明。

事件：

- `DeletionRequested`
- `SubjectSoftDeleted`
- `DeletionRestored`
- `PurgeScheduled`
- `PurgeCompleted`
- `PurgeFailed`
- `LegalHoldApplied`
- `BackupPurgeScheduled`

## 17. 测试场景

1. 软删除后普通 API 和 Agent 不可见。
2. RLS 查询不能读取软删除数据。
3. 搜索和召回立即移除。
4. 密钥和 Token 立即撤销。
5. 账户 30 天内可以恢复。
6. 租户 7 天内可以恢复。
7. 窗口过期后拒绝恢复。
8. 唯一 Primary Owner 注销被阻塞。
9. Co-Owner 注销不删除公司。
10. 物理清除失败不标记完成。
11. 备份恢复后 tombstone 仍然生效。
12. 法律保留不恢复用户访问。
13. 第三方数据限制写入证明。
14. 相同邮箱在清除前不能自动恢复旧账户。
15. 删除事件不包含 PII 和秘密。
16. 清除任务重复执行不产生副作用。

## 18. 验收场景

1. 删除请求立即产生逻辑隔离。
2. 账户和租户具有独立恢复窗口。
3. 软删除数据加密且不可普通访问。
4. 恢复不恢复已撤销密钥。
5. 物理清除覆盖所有权威和派生存储。
6. 删除证明完整且可验证。
7. 备份和 tombstone 防止数据复活。
8. 法律保留范围明确。
9. 外部副本边界明确。
10. 账户注销不误删其他股东公司。
11. 失败清除可重试且不会误报成功。
12. 软删除不是永久保留，超期必须清除。
