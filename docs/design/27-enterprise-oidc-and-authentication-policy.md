# 27 企业 OIDC 与认证策略

- 版本：v0.2（本地实现已验收；真实 IdP 连通性未验证）
- 范围：租户 OIDC、issuer+sub、邀请预置、组映射、IdP MFA、SSO 模式和紧急恢复
- 对应需求：FR-069、BR-088、BR-089、BR-090
- 对应缺口：G-40

## 1. 优先级与边界

- OIDC 企业登录属于第一梯队。
- SAML、BFF、设备管理、会话家族轮换和本地 WebAuthn/TOTP 后置。
- 保留现有 Cookie/Token、CSRF、基础退出撤销和运营 MFA。
- OIDC 只改变人类登录，不改变服务 Token、员工身份或租户权限模型。

## 2. 认证策略

每个租户可配置：

| 模式 | 本地密码 | GitHub | OIDC |
|---|---|---|---|
| optional | 允许 | 允许 | 允许 |
| required | 仅紧急恢复 | 禁止 | 必须 |

- 新租户默认 optional。
- required 模式启用前必须配置并验证紧急恢复路径。
- 切换到 required 时撤销现有非 OIDC 会话。
- IdP 不可用时 fail closed，不静默回退本地密码。
- 一个租户第一梯队只启用一个 active OIDC provider。

## 3. 核心实体

```text
OidcProviderConfig
SsoInvitation
OidcIdentityBinding
AuthenticationPolicy
EmergencyAccessGrant
```

### 3.1 OidcProviderConfig

```text
id
tenant_id
display_name
issuer
client_id
client_secret_ref
redirect_uri
scopes
acr_policy
group_claim
group_mapping_version
status
version
```

- 客户端密钥只存 `secret_ref`。
- issuer、redirect URI、scopes 和映射版本化。
- 配置变更需要授权、审计和重新测试。
- 生产 issuer 必须使用 HTTPS。
- 配置端点不能由普通用户任意填写。

## 4. OIDC 登录流程

```text
创建短期 OIDC transaction
  → Authorization Code + PKCE + state + nonce
  → 服务端交换 code
  → 验证 ID Token
  → 读取邮箱和组声明
  → 匹配邀请或已有 issuer+sub 绑定
  → 应用认证策略和能力映射
  → 创建 Regent 本地会话
```

### 4.1 请求参数

- `response_type=code`。
- `client_id`、精确 `redirect_uri`。
- `scope=openid profile`，邮箱按需申请。
- PKCE `S256`。
- 随机 state 和 nonce，绑定浏览器事务。
- 需要 MFA 时使用配置的 `acr_values` 和 `max_age`。
- 可选 `prompt=login` 或 `select_account`。

### 4.2 ID Token 校验

- 签名算法必须来自允许列表，拒绝 `none`。
- 使用 discovery/JWKS 验证签名并支持密钥轮换。
- 精确校验 `iss`、`aud`、`exp`、`iat` 和 nonce。
- 多 audience 时校验 `azp`。
- 校验 `sub` 存在且非空。
- `auth_time` 和 `acr/amr` 必须满足租户策略。
- 时钟偏差最多 60 秒。
- Token 重放和 state 不匹配立即拒绝。

## 5. 邀请与预置

- 默认禁止 JIT 创建账户。
- 默认禁止按邮箱域名自动加入租户。
- 管理员邀请时记录邮箱、预期能力和过期时间。
- OIDC 登录使用 `email_verified=true` 的邮箱匹配邀请。
- 邀请接受后绑定 `issuer + sub`，后续不再依赖邮箱。
- 邮箱变更不影响已有身份绑定。
- 邀请可撤销、过期和审计。

### 5.1 SsoInvitation

```text
id
tenant_id
email_normalized
intended_capabilities
status
expires_at
accepted_by?
accepted_at?
```

状态：`Pending → Accepted`，或 `Expired/Revoked`。

- Co-Owner 邀请使用 Q-029 的 OwnerInvitation，不能由普通 SsoInvitation 自动授予所有权。
- required SSO 租户必须通过 OIDC 接受所有者邀请。

## 6. 身份键与账户合并

```text
OidcIdentityBinding
  id
  tenant_id
  provider_config_id
  issuer
  subject
  user_account_id
  linked_at
```

- 唯一键为 `provider_config_id + issuer + subject`。
- 同一 subject 不能绑定多个账户。
- 已有账户只能通过有效邀请或已认证显式绑定接入。
- 不按邮箱自动创建 Owner 或自动合并未知账户。
- 外部 subject 不可变，展示名和邮箱可更新。

## 7. 组与能力映射

- 组声明来源和路径必须显式配置。
- 映射到能力，不映射到固定岗位名称。
- 每次登录和策略刷新时重新计算有效能力。
- 删除组映射后及时收回能力。
- 无法读取必需组声明时拒绝授权，不保留过期权限。
- Owner 默认不能由组自动授予。
- 确需组授予 Owner 时，必须二次审批并审计。

## 8. IdP MFA

- SSO 用户优先使用 IdP MFA。
- 租户配置可接受的 `acr` 和 `amr`。
- IdP 未返回所需保证等级时拒绝或要求重新认证。
- `auth_time` 超过策略时限时要求重新登录。
- 本地 MFA 不重复要求，除非租户策略明确需要。
- 运营人员 MFA 保持独立且强制。

## 9. 本地会话

- OIDC 登录后由 Regent 创建自己的本地会话。
- OIDC 本地会话默认最长 8 小时，空闲 30 分钟。
- 不因 IdP 会话长期有效而无限延长。
- 本地退出立即撤销 Regent 会话。
- IdP 退出不自动等同本地退出。
- 管理员可强制撤销单个或全部会话。
- 密码找回和账户安全操作仍撤销全部会话。
- 浏览器仍使用现有 Secure HttpOnly Cookie 和 CSRF 基线。

## 10. 紧急恢复

required 模式必须包含：

- 独立紧急恢复码或受限紧急账户。
- 紧急会话最长 15 分钟。
- 只允许恢复 SSO 配置和账户访问。
- 强制审计和安全通知。
- 使用后立即轮换紧急凭据。
- 不与普通账户恢复码混用。

- IdP 故障时普通登录 fail closed。
- 紧急恢复不能绕过租户隔离或授予新的 Owner 权限。
- 自托管可由本地管理员执行紧急恢复。
- 完整人工找回仍按 Q-027 暂缓。

## 11. 可用性与故障

| 故障 | 行为 |
|---|---|
| Discovery 或 JWKS 不可用 | 新登录失败，已有会话按本地有效期继续 |
| ID Token 签名失败 | 拒绝且不创建会话 |
| nonce/state 不匹配 | 拒绝并记录安全信号 |
| 组声明缺失 | required 策略拒绝授权 |
| IdP 超时 | 有界重试后返回统一错误 |
| 配置错误 | 阻止激活，保留上一可用版本 |
| SSRF 风险端点 | 拒绝解析和访问 |

- 不得把 IdP Token 或用户资料写入普通日志。
- 新登录故障不得导致已有任务数据丢失。

## 12. 安全配置

- OIDC metadata 和 JWKS 缓存有界并支持刷新。
- 管理接口校验 SSRF、重定向和私网地址。
- 仅管理员可配置 issuer 和端点。
- client secret 轮换不中断已激活的旧版本，直到验证完成。
- 不默认持久化 IdP Access Token 或 Refresh Token。
- 不请求组织管理、仓库或工作流权限。

## 13. API

- `GET /auth/{provider}/start`、`GET /auth/{provider}/callback`：授权事务与回调。
- `PUT /v1/tenants/{tenantId}/identity-providers/{provider}`：保存配置草稿。
- `POST /v1/tenants/{tenantId}/identity-providers/{provider}/activate`：验证并激活。
- `GET|PUT /v1/tenants/{tenantId}/authentication-policy`：读取和更新 SSO 策略。
- `PUT /v1/tenants/{tenantId}/oidc/group-mapping`：更新组能力映射并撤销变化权限的会话。
- `POST /v1/tenants/{tenantId}/oidc/group-mapping/refresh`：按绑定中的组声明重新计算能力。
- `POST /v1/tenants/{tenantId}/emergency-access`：Owner 配置紧急凭据。
- `POST /auth/emergency-access`：使用紧急凭据，创建 15 分钟受限会话并返回轮换凭据。
- `POST /auth/logout`：撤销本地会话。

## 13.1 当前实现状态

- required 模式启用前校验 active HTTPS OIDC 与未过期紧急凭据；启用后撤销非 OIDC 会话并禁止密码、GitHub 和非 OIDC 租户切换。
- OIDC 策略执行 `acr`、`amr`、`auth_time` 和必需组声明；组只映射显式能力，禁止 Owner 与高影响治理能力。
- 会话中间件校验持久会话、用户/租户绑定、绝对过期和空闲超时；组能力变化撤销受影响会话。
- 紧急会话只允许 `sso.restore` 与 `account.access`，普通 `/v1`、`/agent/v1` 和 Hub 请求返回 403；凭据单次使用后轮换并写入审计与安全通知。
- Tenant Dashboard 设置页提供策略、Provider、组映射和紧急凭据管理；登录页提供受限紧急恢复入口。
- 真实 IdP、JWKS 轮换和跨区域容灾仍按约束未连接验证；SAML、BFF、设备管理和本地 WebAuthn/TOTP 保持后置。

## 14. 审计事件

- `OidcProviderConfigured`
- `OidcProviderActivated`
- `OidcLoginStarted`
- `OidcLoginSucceeded`
- `OidcLoginRejected`
- `SsoInvitationCreated`
- `SsoInvitationAccepted`
- `OidcIdentityLinked`
- `OidcGroupMappingChanged`
- `SsoSessionRevoked`
- `EmergencyAccessUsed`

审计只记录 issuer、受保护 subject、租户、结果和安全元数据。

## 15. 测试场景

1. Authorization Code + PKCE 登录成功。
2. state 不匹配拒绝。
3. nonce 不匹配拒绝。
4. 错误 iss、aud、azp、exp 或签名拒绝。
5. JWKS 轮换可平滑验证。
6. 未邀请用户不能 JIT 登录。
7. 相同邮箱不同 subject 不自动合并。
8. subject 绑定后邮箱变化不影响登录。
9. 组映射只授予显式能力。
10. 组声明缺失时 required 模式拒绝。
11. 组不能默认授予 Owner。
12. IdP MFA 等级不足时拒绝。
13. required 模式禁止本地密码登录。
14. 紧急恢复只创建 15 分钟受限会话。
15. IdP 故障不回退本地密码。
16. 本地退出可立即撤销 Regent 会话。
17. OIDC 会话最长 8 小时。
18. 普通日志不包含 Token 和用户资料。
19. 恶意 issuer 不能访问内网元数据。
20. required 模式未配置紧急恢复时不能启用。

## 16. 验收场景

1. 租户可配置并启用 OIDC。
2. 已邀请用户可通过 IdP 登录。
3. 未邀请用户被拒绝。
4. 身份基于 issuer+sub。
5. 组映射符合显式能力策略。
6. IdP MFA 可按保证等级委托。
7. required 模式禁止普通本地登录。
8. 紧急恢复独立、限时且可审计。
9. IdP 故障 fail closed。
10. 现有 Cookie/CSRF 和基础撤销不被削弱。
11. BFF 和设备管理没有误进第一梯队。
12. SAML 没有误启用。
