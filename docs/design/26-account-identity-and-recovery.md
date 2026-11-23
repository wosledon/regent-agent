# 26 账户身份与找回

- 版本：v0.1（待评审）
- 范围：账号密码、GitHub OAuth、已验证邮箱合并、恢复码自助找回、限流和审计
- 对应需求：FR-068、BR-084、BR-085、BR-086、BR-087
- 对应缺口：G-39

## 1. 目标与边界

- 第一梯队实现账号密码注册登录、GitHub OAuth 和一次性恢复码自助找回。
- 不依赖邮件服务完成登录和密码找回。
- 仅已验证邮箱可触发自动合并，未验证邮箱不得合并。
- 其他身份提供商和完整人工找回暂缓。
- 企业 SSO、MFA、设备管理和会话策略在 Q-028 单独讨论。

## 2. 身份模型

```text
UserAccount
  ├── PasswordCredential?
  ├── EmailClaim[]
  ├── RecoveryCodeSet
  ├── ExternalIdentityBinding[]
  └── LoginSession[]
```

- `UserAccount` 是账户主键，与租户、员工和岗位身份分开。
- 一个 UserAccount 可以拥有或加入多个租户，成员关系见 [28 账户、租户与所有权](./28-account-tenancy-and-ownership.md)。
- `ExternalIdentityBinding` 以 `provider + provider_subject` 唯一。
- GitHub 身份使用不可变 user id，不使用 login 或昵称作为主键。
- `EmailClaim` 记录邮箱、来源和验证状态；只有已验证 claim 可参与合并。
- 本地注册邮箱在当前版本保持未验证状态。

### 2.1 EmailClaim

```text
id
user_account_id
email_normalized
source
verified
verified_at?
provider_subject?
```

- 邮箱统一去空格、域名和本地部分小写，不做 fuzzy 匹配。
- 已验证邮箱在平台内唯一。
- 本地注册来源为 `local_unverified`，不能自动合并。
- GitHub 来源只有在 API 明确返回 verified 时才能标记已验证。
- 邮箱变更必须创建新 claim，旧 claim 保留历史。

### 2.2 PasswordCredential

- 密码使用 Argon2id 哈希，参数版本化并可升级。
- 不保存明文或可逆加密密码。
- 首次密码设置和找回共用同一密码策略。
- 登录按账户、IP 和全局维度限流。
- 注册和登录使用统一提示，不泄露账户是否存在。

## 3. GitHub OAuth

### 3.1 流程

```text
创建短期 OAuth transaction
  → 跳转 GitHub Authorization Code + PKCE
  → 校验 state、PKCE 和 redirect URI
  → 服务端交换 code
  → 获取 GitHub user id 和已验证邮箱
  → 登录、注册或按已验证邮箱自动合并
  → 丢弃临时 code 和 token
```

- 客户端密钥只保存在密钥系统。
- `state` 绑定浏览器会话并短期过期。
- Authorization Code 只能使用一次。
- 重定向 URI 必须精确匹配白名单。
- 回调错误使用统一提示，不泄露内部细节。

### 3.2 最小权限

- 读取完成登录和邮箱合并所需的最小用户资料。
- 需要自动合并时申请 `user:email`，只接受 verified 的邮箱。
- 不申请 `repo`、组织管理或工作流权限。
- 不在业务数据库保存 GitHub Access Token。
- 若未来需要 API 集成，单独申请授权并加密保存。

### 3.3 登录、注册和绑定

- 已绑定的 GitHub 用户直接登录。
- 未绑定但已验证邮箱唯一匹配已有账户时自动绑定并登录。
- 未绑定且不存在匹配账户时，可按注册策略创建新账户。
- 已验证邮箱匹配到多个账户、账户已绑定其他 GitHub 身份或来源冲突时拒绝自动合并。
- 已认证会话可显式绑定其他外部身份。
- 同一 GitHub 身份不能绑定多个 Regent 账户。
- 同一 Regent 账户可绑定多个登录方式。

## 4. 密码、邮箱与外部身份绑定

### 4.1 账号密码注册登录

```text
注册
  → 规范化邮箱
  → 检查唯一性
  → 创建 UserAccount + 未验证 EmailClaim
  → 设置 Argon2id 密码
  → 生成恢复码
  → 创建登录会话
```

- 邮箱已存在时不创建重复账户，也不自动合并。
- 引导已有用户直接登录、使用 GitHub 登录或执行找回。
- 登录验证密码哈希并创建新会话。
- 登录失败不泄露“邮箱不存在”或“密码错误”的差异。
- 密码重置走第 5 节恢复码流程。

### 4.2 已验证邮箱自动合并

```text
GitHub OAuth 完成
  → 获取已验证邮箱
  → 规范化并唯一匹配 EmailClaim
  → 检查冲突
  → 创建 ExternalIdentityBinding
  → 登录唯一匹配账户
```

- 只有 `verified=true` 的 GitHub 邮箱可触发自动合并。
- 本地未验证邮箱不能触发合并。
- 匹配必须唯一，多个候选账户时拒绝。
- 目标账户已绑定其他 GitHub 身份时拒绝并记录安全信号。
- 合并只增加登录方式，不转移租户所有权、岗位或权限。
- 合并和冲突拒绝全部写入审计。

### 4.3 ExternalIdentityBinding

```text
id
user_account_id
provider
provider_subject
provider_login
display_name?
avatar_url?
linked_at
linked_by
last_login_at?
```

- `provider_subject` 不可变。
- `provider_login` 和展示字段可更新，不参与鉴权。
- 解绑必须由已认证账户发起。
- 解绑前至少保留一个其他可用登录方式。
- 绑定和解绑全部写入审计。

## 5. 恢复码

### 5.1 生成

- 账户初始设置时生成恢复码。
- 默认 10 个一次性恢复码。
- 每个码由密码学安全随机源生成。
- 明文只在生成时显示一次，支持下载和打印。
- 服务端只存带盐不可逆哈希。
- 重新生成会使旧恢复码全部失效。

### 5.2 使用

- 用户输入账户标识和恢复码。
- 校验成功后创建短期、单次恢复会话。
- 用户直接设置新密码。
- 恢复码在事务内核销。
- 完成后撤销该账户全部 LoginSession 和 Refresh Token。
- 个人 API Token 默认撤销，租户模型密钥不自动删除。

### 5.3 安全要求

- 不在日志、遥测、审计正文或聊天记录恢复码。
- 不在 URL 查询参数中传递恢复码。
- 恢复码尝试按账户、IP 和全局维度限流。
- 连续失败达到阈值后锁定恢复入口。
- 提示不得泄露账户是否存在。
- 使用恢复码后建议立即重新生成整组恢复码。

## 6. 恢复请求

```text
AccountRecoveryRequest
  id
  user_account_id?
  channel
  status
  requested_at
  expires_at
  failure_reason?
```

状态：

`Requested → Verified → Completed`

失败进入 `Denied` 或 `Expired`。

- 账户不存在和恢复码错误返回统一提示。
- 恢复会话短期有效且只能使用一次。
- 完成事件记录账户、时间和结果，不记录恢复码。

## 7. 会话撤销

密码找回成功后：

- 撤销全部 access/refresh token 家族。
- 撤销个人 API Token。
- 使旧 Cookie 失效。
- 通过站内安全通知告知用户。
- 异常信号关联 Q-026 安全事件。

租户密钥、员工凭据和公司数据不因个人密码重置自动删除或转移。

账户注销按 Q-030 软删除，凭据保持禁用并在恢复窗口内隔离。

## 8. 防滥用

- 登录、OAuth 回调、绑定和找回统一限流。
- OAuth transaction 与浏览器会话绑定。
- state 不匹配、code 重放和 PKCE 失败立即拒绝。
- 不允许员工或 Tool 调用身份绑定和找回接口。
- 运营人员不能直接修改密码哈希或恢复码哈希。
- 所有账户安全操作写入租户或平台审计边界。

## 9. 暂缓项

- 本地邮件验证、备用邮箱和邮件找回。
- 短信。
- 安全问题。
- Google、Apple 等其他社交登录。
- 完整人工找回和双人审批流程。
- 企业 OIDC 在 [27 企业 OIDC 与认证策略](./27-enterprise-oidc-and-authentication-policy.md) 单独设计。
- 本地 WebAuthn/TOTP 和设备管理后置。

暂缓不等于未来自动支持；新增方式必须重新做安全设计。

## 10. API

- `POST /auth/register`：账号密码注册。
- `POST /auth/login`：账号密码登录。
- `GET /auth/github/start`：创建 OAuth transaction。
- `GET /auth/github/callback`：处理 OAuth 回调。
- `POST /auth/github/link`：显式绑定 GitHub。
- `DELETE /auth/github/link`：解绑 GitHub。
- `POST /auth/recovery-codes`：生成恢复码。
- `POST /auth/recovery/start`：验证恢复码并创建恢复会话。
- `POST /auth/recovery/complete`：设置新密码并撤销会话。

- 恢复码生成要求已认证会话。
- 解绑要求已认证会话且存在其他登录方式。
- 找回接口不要求登录，但必须限流。

## 11. 审计事件

- `PasswordAccountRegistered`
- `PasswordLoginSucceeded`
- `PasswordLoginFailed`
- `EmailMergeSucceeded`
- `EmailMergeRejected`
- `GitHubLoginStarted`
- `GitHubLoginSucceeded`
- `GitHubLoginFailed`
- `ExternalIdentityLinked`
- `ExternalIdentityUnlinked`
- `RecoveryCodesGenerated`
- `RecoveryCodesInvalidated`
- `AccountRecoveryRequested`
- `AccountRecoveryCompleted`
- `AccountRecoveryFailed`
- `AccountSessionsRevoked`

审计只记录账户、provider subject 的受保护标识、时间、IP 元数据和结果。

## 12. 测试场景

1. 账号密码可以注册并登录。
2. 密码只以 Argon2id 哈希存储。
3. 本地未验证邮箱不得自动合并已有账户。
4. GitHub 已验证邮箱唯一匹配时自动合并并登录。
5. 已验证邮箱匹配多个账户时拒绝合并。
6. 目标账户已绑定其他 GitHub 身份时拒绝合并。
7. OAuth state 不匹配时拒绝。
8. PKCE 不匹配时拒绝。
9. Authorization Code 重放时拒绝。
10. Redirect URI 不匹配时拒绝。
11. GitHub login 改变后仍识别同一账户。
12. GitHub 未验证邮箱不触发自动合并。
13. 已登录账户才能显式绑定或解绑其他身份。
14. 最后一个登录方式不能解绑。
15. 恢复码明文不可从数据库恢复。
16. 恢复码只能使用一次。
17. 重新生成使旧恢复码失效。
18. 找回成功撤销全部旧会话。
19. 找回不删除租户密钥或转移所有权。
20. 日志和遥测不包含恢复码、密码或 GitHub Token。
21. 找回入口支持限流和锁定。

## 13. 验收场景

1. 无邮件服务时可通过账号密码注册登录。
2. 无邮件服务时可通过 GitHub 登录。
3. 无邮件服务时可通过恢复码自助找回。
4. 账户绑定基于不可变 GitHub user id。
5. GitHub 已验证邮箱唯一匹配时自动合并。
6. 本地未验证邮箱不触发自动合并。
7. 解绑前保留其他登录方式。
8. 恢复码单次使用且可整体重生成。
9. 找回成功撤销旧会话。
10. 普通运营人员不能直接修改账户凭据。
11. 审计不包含秘密和完整 PII。
12. 暂缓身份方式不会意外启用。
