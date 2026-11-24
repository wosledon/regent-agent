# 28 账户、租户与所有权

- 版本：v0.1（待评审）
- 范围：账户多租户、Primary/Co-Owner、所有者邀请、限时代理和租户切换
- 对应需求：FR-070、BR-091、BR-092、BR-093
- 对应缺口：G-41

## 1. 目标与边界

- 账户与租户解耦，一个账户可以拥有或加入多个租户。
- 第一梯队保持一租户一公司，多家公司通过多个租户实现。
- Tenant 是安全、订阅、配额和计费边界。
- 不支持一个 Tenant 内多个 Company。
- 产品所有权不等于现实法律股权，不维护真实 cap table。

## 2. 领域模型

```text
UserAccount
  └── TenantMembership[]
          └── TenantOwnership?
                └── Tenant
                      └── Company

OwnerInvitation → TenantMembership + TenantOwnership
OwnerDelegation → 限时能力委托
LoginSession → ActiveTenantContext
```

- 账户身份、密码、GitHub/OIDC 和恢复码保持全局。
- 成员关系、所有权、能力和业务数据按租户隔离。
- 同一账户可以在不同租户拥有不同角色。

## 3. TenantMembership

```text
id
tenant_id
account_id
status
membership_version
created_at
updated_at
```

- `(tenant_id, account_id)` 唯一。
- 状态：`Invited / Active / Suspended / Removed`。
- 只有 Active 成员可以访问租户。
- 移除成员只影响该租户，不删除全局账户。
- 权限变化递增 membership_version。

## 4. 所有权

### 4.1 TenantOwnership

```text
membership_id
tenant_id
kind
granted_at
granted_by
revoked_at?
```

- `kind = primary | co_owner`。
- 每个租户恰好一个 primary。
- 可有多名 co_owner。
- Primary Owner 和 Co-Owner 都必须是 Active 成员。

### 4.2 权限

| 操作 | Primary Owner | Co-Owner | Owner Delegate |
|---|---|---|---|
| 公司治理、章程、预算 | 是 | 是 | 显式委托范围 |
| 管理 Co-Owner | 是 | 否 | 否 |
| 转让 Primary 所有权 | 是 | 否 | 否 |
| 管理订阅和计费 | 是 | 否 | 否 |
| 删除租户 | 是 | 否 | 否 |
| 紧急恢复 | 按策略 | 按策略 | 否 |

- 主所有者缺席时，普通公司决定可由 Co-Owner 处理。
- 主所有者专属操作保持待定，不自动同意。

## 5. OwnerInvitation

```text
id
tenant_id
inviter_id
target_account_id?
target_email?
token_hash
intended_kind
expires_at
status
accepted_at?
```

- 邀请 Token 高熵、单次使用、只存哈希。
- 默认 7 天过期，可提前撤销。
- 接受邀请必须处于已认证会话。
- 接受后创建 Active membership 和 ownership。
- 邀请不授予 Primary Owner。
- 邀请 Token 不进入日志、聊天或审计正文。
- required SSO 租户必须通过 OIDC 接受邀请。

## 6. 添加和移除所有者

### 添加 Co-Owner

1. Primary Owner 发起邀请。
2. 目标账户接受邀请。
3. 系统创建 Active membership 和 co_owner ownership。
4. 写入审计并通知双方。

### 移除 Co-Owner

- 只能由 Primary Owner 执行。
- 立即撤销目标账户在该租户的会话和委托。
- 保留历史审计和已完成工作。
- 不影响目标账户的其他租户成员关系。

### 最后所有者保护

- 不能移除、暂停或降级最后一个 Active 所有者。
- Primary Owner 离开前必须完成主所有权转让。
- 账户注销前必须处理唯一 Primary 所有权，不能留下无主租户。
- 账户和租户删除使用 Q-030 软删除，恢复窗口分别为 30 天和 7 天。
- 转让和接受必须在同一事务边界完成。

## 7. Primary 所有权转让

1. Primary Owner 选择目标 Active Co-Owner。
2. 目标明确接受。
3. 系统校验最后所有者不变量。
4. 原子交换 primary 和 co_owner。
5. 递增相关 membership_version。
6. 写入审计并通知双方。

- 目标不能是 suspended 或 removed。
- 不能批量自动转让。
- 转让不改变租户、公司、订阅或数据归属。

## 8. OwnerDelegation

```text
id
tenant_id
grantor_id
delegate_id
capabilities
reason
starts_at
expires_at
revoked_at?
```

- 只允许委托给 Active 成员。
- 不允许自己委托给自己。
- 默认最长 30 天，可更短。
- 可随时撤销。
- 不能转授权或叠加成永久权限。
- 不能包含主所有权转让、租户删除、订阅变更和所有者管理。
- 委托操作记录 `on_behalf_of`。
- 到期、撤销、成员暂停或能力变化立即失效。
- 委托不能绕过 quorum、自审批限制或安全红线。
- OwnerDelegation 用于治理能力委托；日常升级审批使用独立 ApprovalDelegation，见 [36 审批委托与缺席](./36-approval-delegation-and-absence.md)。

## 9. 租户切换

```text
选择目标租户
  → 校验 Account + Active Membership
  → 计算有效能力和委托
  → 创建新的租户作用域会话
  → 撤销旧租户上下文
  → 清空前端缓存和当前上下文
  → 记录审计
```

- 每个登录会话一次只有一个 Active Tenant。
- 切换后签发带目标 tenant_id 的新会话。
- 不接受请求体中的 Tenant ID 作为权限依据。
- 深链接必须先解析成员关系再加载。
- 切换失败保留原会话或要求重新登录。
- 不能通过切换绕过限流、封禁或安全事件冻结。

## 10. ActiveTenantContext

```text
session_id
account_id
tenant_id
membership_id
membership_version
effective_capabilities
issued_at
```

- 上下文由服务端签发和维护。
- membership_version 变化使旧上下文失效。
- 前端只保存展示用租户列表，不保存权限真相。
- 租户列表接口必须过滤 Suspended 和 Removed。

## 11. 数据与隔离

- 所有租户业务表继续使用 tenant_id 和强制 RLS。
- 成员关系表属于控制面身份范围，使用显式 system scope。
- 跨租户唯一约束和索引包含 tenant_id。
- 同一账户不能绕过租户过滤。
- 不共享模型密钥、Runner 凭据、员工、岗位、主控、编制、预算、项目、聊天、记忆、Artifact、仓库、审计和计费。
- 当前提供 Owner 自助租户导出与校验迁移包，不提供跨租户导出；跨环境导入和托管退出仍后置。
- Runner 调度仍按 Tenant 公平分配。
- 一个员工的工号全局唯一，但默认只属于一个租户公司。

## 12. 订阅与计费

- 每个 Tenant 独立订阅、套餐、配额和账本。
- Primary Owner 是付款和订阅管理责任人。
- Co-Owner 不自动获得账单信息或支付权限。
- 多公司不能通过拆分租户绕过平台总配额。
- 删除一个 Tenant 不影响其他 Tenant。
- 本地套餐包含 Starter/Pro/Enterprise，员工、项目和存储配额在创建前检查；门禁位于 `OrganizationService`/`ProjectService`/`TaskExecutionService` 应用层，HTTP、Agent/Tool 与 Runner 自动建员工入口不能绕过，terminated 员工不占用员工配额。Primary Owner 可创建发票并通过本地支付适配激活套餐。
- 订阅发票与只增账本按 Tenant 隔离，记录金额、币种、价格版本和支付引用；真实支付、税务、退款和生产定价后置。
- 数据导出只允许 Owner 生成含 manifest/SHA-256 的 ZIP 迁移包，排除凭据、会话、恢复码和 KeyVault；跨环境导入仍后置。

## 13. OIDC 集成

- OIDC 身份绑定全局 UserAccount。
- 每个 Tenant 独立邀请和映射能力。
- 同一 issuer+sub 可在不同租户有不同 ownership。
- 组映射不自动创建 Owner。
- Co-Owner 邀请必须由 Primary Owner 发起。
- required SSO 租户的邀请接受必须经过 IdP 登录。

## 14. API

- `GET /account/tenants`：列出可访问租户。
- `POST /tenants`：创建新租户和公司。
- `POST /tenants/{id}/switch`：切换当前租户。
- `POST /tenants/{id}/owner-invitations`：邀请 Co-Owner。
- `POST /owner-invitations/{token}/accept`：接受邀请。
- `DELETE /tenants/{id}/owners/{account_id}`：移除 Co-Owner。
- `POST /tenants/{id}/primary-transfer`：发起转让。
- `POST /primary-transfer/{id}/accept`：接受转让。
- `POST /tenants/{id}/delegations`：创建代理。
- `DELETE /delegations/{id}`：撤销代理。

## 15. 审计事件

- `TenantCreated`
- `OwnerInvitationCreated`
- `OwnerInvitationAccepted`
- `CoOwnerAdded`
- `CoOwnerRemoved`
- `PrimaryOwnershipTransferStarted`
- `PrimaryOwnershipTransferred`
- `OwnerDelegationCreated`
- `OwnerDelegationRevoked`
- `OwnerDelegationExpired`
- `ActiveTenantChanged`

审计记录操作者、目标、租户、前后版本和 on_behalf_of。

## 16. 测试场景

1. 一个账户可以创建两个租户。
2. 同一账户在两个租户权限独立。
3. 一个租户有多个 Co-Owner。
4. 数据库拒绝两个 Primary Owner。
5. 不能移除最后一个所有者。
6. Primary 转让需要目标接受。
7. 邀请 Token 单次使用且过期失效。
8. 代理到期后不能继续操作。
9. 代理不能转授权。
10. 代理不能删除租户或修改订阅。
11. 移除 Co-Owner 只撤销目标租户会话。
12. 切换租户后旧 tenant_id Token 失效。
13. 请求体伪造 Tenant ID 被拒绝。
14. 跨租户 RLS 查询被拒绝。
15. 两个租户的密钥、预算和记忆不共享。
16. OIDC 组不自动授予 Owner。
17. Primary 缺席时普通决定可由 Co-Owner 处理。
18. 删除一个租户不影响其他租户。

## 17. 验收场景

1. 账户与租户解耦。
2. 一个账户可管理多个公司。
3. 一个租户可有多个所有者。
4. Primary Owner 唯一。
5. 最后所有者始终存在。
6. 代理限时、限范围且可撤销。
7. 主所有者专属操作不能被代理。
8. 租户切换具有服务端鉴权和会话隔离。
9. 跨租户数据默认不共享。
10. 订阅和计费按租户独立。
11. 第一梯队仍是一租户一公司。
12. 所有权和委托全程可审计。
