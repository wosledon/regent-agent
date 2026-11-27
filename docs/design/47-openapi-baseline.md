# 47 OpenAPI 基线

- 版本：v0.2（本地全局写契约与签名游标已验收）
- 范围：内部 HTTP API 的 OpenAPI、认证、幂等、分页、错误、版本、限流和代码生成
- 关联设计：[01 基础契约与状态模型](./01-contracts-and-state.md)、[42 内部 API 与扩展边界](./42-internal-api-and-extension-boundary.md)、[后端技术文档](../Regent-Agent-后端技术文档.md)、[前端技术文档](../Regent-Agent-前端技术文档.md)

## 1. 范围

- OpenAPI 只描述系统内部 HTTP API。
- 不提供公共 REST API、公共 OAuth Client 或公共兼容性承诺。
- Tenant API、Ops API、Gateway Admin API 和 Gateway 数据面使用独立文档或明确分离的 Path/Security Scheme。
- OpenAI 与 Anthropic 数据面保持各自原生协议，不合并为统一 DTO。
- SignalR Hub 方法不写入 OpenAPI，使用单独的 Hub 契约文档和 TypeScript 类型。

## 2. 规范版本

- 使用 OpenAPI 3.1。
- JSON over HTTPS。
- 基础路径 `/v1`。
- Schema 使用 JSON Schema 2020-12 能力。
- 时间使用 RFC 3339 UTC。
- ID 使用 UUID 字符串。
- 枚举使用稳定小写 `snake_case`。
- 金额使用最小货币单位整数，并显式携带 ISO 4217 币种。
- 示例数据必须脱敏，不包含真实租户、密钥、Prompt 或 PII。

## 3. 文档权威源

- ASP.NET Core 端点、路由和 Schema 是 HTTP 契约来源。
- 构建时生成 OpenAPI JSON/YAML。
- TypeScript API Client 从 OpenAPI 生成。
- `RegentAgent.Contracts` 继续保存内部事件、状态引用类型和跨服务消息，不维护重复 HTTP DTO。
- 前端不手写与服务端重复的请求/响应类型。
- OpenAPI 变更进入 PR diff 和契约测试。
- checked-in OpenAPI 与运行时文档都省略环境相关的 `servers`；调用方显式提供 base URL，契约测试拒绝服务器地址漂移。

## 4. 认证与授权

| 调用方 | 认证方式 | 说明 |
|---|---|---|
| 租户浏览器 | HttpOnly Cookie 会话 | CSRF、SameSite、租户上下文 |
| 运营浏览器 | Operator Cookie（生产目标含 MFA/step-up；本地 demo 不含） | 独立 audience |
| Runner/Gateway 服务 | Bearer service token | 独立 audience 和 scope |
| 员工任务 | Assignment token | 单租户、单员工、单任务 |
| 支持访问 | SupportGrant token | operator、tenant、scope、有效期 |
| OIDC 用户 | Authorization Code 跳转 | 前端不接触 client secret |

规则：

- 租户 Token 不能访问 Ops API。
- Operator Token 不能直接访问 Tenant API。
- 请求体中的 `tenant_id` 不作为身份依据。
- 每个 Operation 声明所需 scope 或 Capability。
- 404 与 403 的使用必须避免泄露不可见资源。

当前控制面命名策略与最小 Capability：

| 策略 | 要求 | 主要范围 |
|---|---|---|
| `TenantWriter` | 已认证 + `tenant.write`；Owner/CoOwner 保留特权 | 身份/所有权、组织招聘、模型运行时预算、Agent evolution、治理审批、合规生命周期、事件、工作流发布审查、绩效任务治理、审计、账单支付导出写操作 |
| `ChatRead` | 已认证 + 权威 viewer resolver；`OwnerObserver` 或具有 `chat.read` 的有效 `EmployeeMember` | 人类会话列表与消息读取；员工继续按会话成员过滤，观察者仅读取白名单共享会话 |
| `CollaborationRead` | 已认证 + 权威 viewer resolver；`OwnerObserver` 或具有 `collaboration.read` 的有效 `EmployeeMember` | 人类协作图、trace、请求列表与详情；观察者使用过滤后的共享图 |

Capability handler 从当前 `MembershipRecord` 解析租户/用户对应的角色与 capability，不信任 Cookie 中可能过期的 role/capability claims；缺少有效 `tenant_id`、`user_id` 或 membership 时 fail closed。聊天/协作 viewer resolver 还要求可选 `employee_id` 对应租户内未终止且具有有效任职周期的员工；只有当前数据库 `Owner`/`CoOwner` 且没有 employee identity 时才解析为 `OwnerObserver`。Password 与 Development 认证方式本身都不授予权限。授权中间件必须先于幂等中间件执行，普通已认证读取不因为 `TenantWriter` 被额外阻断。

`OwnerObserver` 的会话可见性要求租户相等，并且 `Kind` 与 `ScopeType` 同时属于 `Company`/`Project`/`Topic`；任一字段为 `Direct`、`Position`、`System`、未知、空或损坏值时拒绝。观察者读取不修改 `MemberEmployeeIds`，协作图/trace 必须过滤会话、消息、摘要、边、权重、活动、聊天审计和计数，聊天审计缺少可用 `conversation_id` 时拒绝。完整 `message.created` 只在会话组发送，租户失效载荷不得包含 `conversation_id` 或 `message_id`。

## 5. 通用请求头

| Header | 用途 |
|---|---|
| `Authorization` | 服务调用 Bearer Token |
| `Cookie` | 浏览器会话 |
| `X-Request-Id` | 请求关联；缺失时服务端生成 |
| `Idempotency-Key` | 创建和外部写操作幂等 |
| `If-Match` | 更新操作的 row_version/ETag |
| `Accept-Language` | UI 和错误文案 locale |
| `Content-Type` | JSON 请求类型 |
| `X-CSRF-Token` | Cookie 会话写请求 |
| `X-Regent-Metering-Token` | Gateway 用量投影服务凭据；生产 ControlPlane 必填 |

- 响应始终返回 `X-Request-Id`。
- 幂等键在同一租户、同一操作和同一资源范围内唯一。
- 幂等记录默认保留 24 小时，长任务按任务保留策略延长。
- 相同幂等键不同请求哈希返回 `409 IDEMPOTENCY_CONFLICT`。

## 6. 响应模型

### 6.1 单资源

- 直接返回资源 JSON，不额外包裹通用 `data` 层。
- 创建成功返回 `201` 和 `Location`。
- 无返回值的写操作返回 `204`。
- 需要异步处理的操作返回 `202` 和 Operation/Job 引用。

### 6.2 列表

```json
{
  "items": [],
  "next_cursor": "opaque-cursor",
  "snapshot_at": "2026-01-01T00:00:00Z"
}
```

- 使用 `limit + cursor`，禁止无限 offset。
- `next_cursor` 为不透明字符串。
- 游标绑定租户、筛选、排序和数据快照。
- 筛选或排序变化后旧游标失效。

### 6.3 错误

```json
{
  "request_id": "uuid",
  "error": {
    "code": "BUDGET_HARD_LIMIT",
    "message": "human readable message",
    "retryable": false,
    "details": {"required": "8000", "available": "5600"}
  }
}
```

| HTTP | code 示例 | 含义 |
|---|---|---|
| 400 | `INVALID_ARGUMENT` | 参数格式错误 |
| 401 | `UNAUTHENTICATED` | 凭据缺失或无效 |
| 403 | `TENANT_MISMATCH` / `FORBIDDEN` | 租户或权限不匹配 |
| 404 | `NOT_FOUND` | 资源不存在或不可见 |
| 409 | `IDEMPOTENCY_CONFLICT` / `INVALID_STATE` | 幂等或状态冲突 |
| 412 | `VERSION_CONFLICT` | row_version 过期 |
| 422 | `POLICY_DENIED` | 策略或业务规则拒绝 |
| 429 | `RATE_LIMITED` / `BUDGET_HARD_LIMIT` | 限流或预算限制 |
| 503 | `DEPENDENCY_UNAVAILABLE` | 下游不可用 |

- `details` 只包含授权且脱敏的诊断字段。
- 错误消息可本地化，`code` 保持稳定。
- 不返回堆栈、SQL、密钥或内部文件路径。

## 7. 版本与兼容

- `/v1` 是内部主版本边界。
- 新增可选字段向后兼容。
- `DashboardCard` 的可选 `titleKey`、`summaryKey`、`summaryValues`、`metricPresentation` 与 `ActionCenterItem.nextActionKey` 用于稳定本地化；旧文案字段保持必填并继续填充，消费者必须支持缺失或未知 key 的旧文案回退。
- 删除字段、改语义、收窄类型或改变状态机属于破坏性变更。
- 破坏性变更升级主版本或提供明确迁移期。
- 内部 API 不承诺公共 SemVer 和长期弃用周期。
- 事件使用独立 `schema_version`，消费者忽略未知字段并拒绝不支持的主版本。

## 8. 幂等、并发与长任务

- 所有创建类接口必须支持 `Idempotency-Key`。
- JSON 写请求在路由、模型绑定和幂等分流前拒绝重复属性；属性名按 `OrdinalIgnoreCase` 比较，嵌套对象同样校验，返回 `400 INVALID_ARGUMENT`，避免幂等/授权解析与模型绑定使用不同值。
- 所有更新类接口必须支持 `If-Match` 或等价版本条件。
- 任务认领、预算预留、编制占用和审批执行使用原子条件更新。
- 长任务返回 `202` 和任务/作业 ID，客户端通过状态端点轮询或 SignalR 接收完成事件。
- 重试只适用于幂等安全操作。
- 非幂等外部动作在结果未知时进入核对，不盲目重放。

### 8.1 当前强制边界

- `/v1` 与 `/agent/v1` 的所有 POST/PUT/PATCH/DELETE 请求必须携带 `Idempotency-Key`；中间件在调用端点前写入 InProgress 记录，成功后保存状态码、Content-Type 和响应体。
- 授权先于幂等记录和重放。`POST /v1/approvals/{id}/decision` 由 `ApprovalService` 按当前稳定用户 ID 先执行自我批准检查，再按规范决策哈希处理服务层幂等重放；`POST /v1/agent-evolution/tools/disclose` 每次按当前 claims/role 计算，不使用跨 actor 的通用响应重放。两项豁免按规范化、大小写不敏感且允许尾斜杠的路径段识别。
- 相同租户、操作和幂等键只能重放字节一致的请求；请求哈希不同返回 `409 IDEMPOTENCY_CONFLICT`，原请求仍在进行或结果未知时返回 `409 IDEMPOTENCY_IN_PROGRESS`。
- PUT/PATCH/DELETE 必须携带 `If-Match`；请求体含 `version`/`rowVersion` 时必须精确匹配，无版本字段的动作型更新只能使用 `If-Match: *`，不匹配返回 `412 VERSION_CONFLICT`。
- 所有响应携带 `X-Request-Id`；错误体统一使用 `request_id` 与稳定 `error.code`。
- 游标使用 HMAC 签名的不透明字符串，绑定租户、筛选条件、快照时间和排序键；跨租户、跨筛选、篡改或过期游标均拒绝。
- Tenant Dashboard API Client 自动生成幂等键、请求 ID 和版本条件；Runner 完成任务使用稳定幂等键，支持安全重试。
- `/internal/metering/usage` 使用独立服务凭据，按 `usage_event_id` 业务幂等；成功返回 204，重复事件不重复写成本或项目支出。
- 紧急访问响应包含 `sessionKind=Emergency`；前端展示轮换凭据后仍通过 `/auth/me` 建立服务端认证状态。
- `GET /auth/me` 返回 Tenant 安全会话摘要，其中 `effective_access.can_read_chat`、`effective_access.can_read_collaboration` 和 `effective_access.collaboration_scope`（`none|participant|observer`）由当前数据库 authority 计算，不返回 employee/user/session ID；`GET /ops/auth/me` 返回不含租户上下文的 Operator 安全摘要。两者缺失会话时返回 401 而非重定向。整个 `/ops` 路由组要求 `OperatorReader`，所有状态变更端点另要求 `OperatorWriter`；Reader 包含全部现有 Operator 角色，Writer 仅排除 `Ops Viewer`。匿名返回 401，已认证非 Operator 返回 403，Ops Viewer 写入返回 403。
- ControlPlane 与 Operations 的 `POST /auth/dev-login` 都是默认关闭的本地演示入口：分别只在 `Development` 且 `Identity:AllowDemoLogin=true`、`Operations:AllowDemoLogin=true` 时可用，否则返回 404。CI e2e 仅对临时 Development 进程显式 opt-in，本地 Compose override 只启用 Operations；生产配置不启用，生产 Operator SSO/MFA/step-up 仍是独立工作。
- Tenant Dashboard API Client 使用 `createUuidV4()` 生成请求 ID 和幂等键；非安全 HTTP 上下文缺少 `crypto.randomUUID` 时回退到 `crypto.getRandomValues`，没有 CSPRNG 时失败关闭。可选 `X-Request-Id` 生成失败可省略并由服务端补齐。
- `contracts/openapi/controlplane.json` 与 `apps/tenant-dashboard/src/lib/api-client/schema.d.ts` 从运行应用生成；`OpenApiContractTests` 比较运行时 schema 和状态码，字段或响应漂移会使测试失败。审批契约包含员工/候选人/岗位目标、决定身份和消费状态；performance Terminate 文档为 202，offboard 为 204，`GET /v1/performance-actions/{id}` 提供 202 Location 对应资源。`GET/PUT /v1/compliance/profile` 的 200 响应均显式声明 `ComplianceProfile`，新租户读取会原子创建确定性 baseline；更新在原子 mutation 内校验请求 `Version` 与存储版本，过期时返回 `412 VERSION_CONFLICT`。`GET /v1/projects/{projectId}/delivery/tasks/{taskId}/artifacts` 返回 Ready 工件的权威 ID、object key 与 SHA-256，供独立评审和 DoneEvidence 引用。
- 内部 Outbox 不提供租户发布或处理端点；`POST /v1/outbox` 返回 405，`POST /v1/outbox/process` 返回 404，处理仅由后台 Worker 执行。

## 9. 限流与背压

- 租户、员工、Endpoint 和资源池分别设置限流。
- `429` 响应返回 `Retry-After`。
- 队列满时返回明确错误，不静默丢弃。
- L3/SEV1 和紧急安全动作有保留容量。
- Provider 限流与 Regent API 限流分别统计。

## 10. API 域

| 文档域 | 主要路径 | 所有者 |
|---|---|---|
| 认证租户 | `/auth`、`/tenants`、`/memberships` | ControlPlane Identity |
| 组织 | `/positions`、`/employees`、`/assignments` | ControlPlane Organization |
| 项目 | `/projects`、`/requirements`、`/tasks`、`/defects` | Projects |
| 通信聊天 | `/conversations`、`/messages`、`/routes`、`/agent/v1/collaboration` | Communication/Chat |
| Tool | `/tools/discover`、`/tools/{name}/invoke` | Tool Registry/PEP |
| 预算账本 | `/budgets`、`/usage`、`/ledger`、`/internal/metering/usage` | ControlPlane Ledger |
| 评测 | `/benchmark-suites`、`/evaluations` | Evaluation |
| 治理 | `/escalations`、`/compliance`、`/notifications` | Governance |
| 模型管理 | `/admin/tenants/{tenant}/models` | Gateway Admin |
| 运行接入 | `/runners`、`/tasks/{id}/lease` | ControlPlane Runtime |
| 运营 | `/ops/tenants`、`/ops/support-grants`、`/ops/backups` | Operations |

Gateway 数据面：

- OpenAI：`/v1/chat/completions`。
- Anthropic：`/v1/messages`。
- 保留各自原生 Schema、流式事件和错误结构。

## 11. 事件契约

- 业务事件通过 Outbox 投递，不直接作为 HTTP 响应事实。
- 事件信封包含 `event_id`、`tenant_id`、`event_type`、`schema_version`、`occurred_at`、`causation_id`、`correlation_id` 和 payload。
- 消费者按 `(consumer_id, event_id)` 去重。
- SignalR 只推送事件摘要或增量，完整事实仍从 API 获取。
- 事件 payload 不得包含密钥、完整 Prompt、未脱敏 PII 或密封评测集。

## 12. 代码生成

- 后端构建生成 OpenAPI。
- 前端生成 TypeScript Client 和类型。
- 生成产物不手改。
- API 路径、参数和 Schema 变化必须更新契约测试。
- SignalR Hub 使用单独 TypeScript 生成或手写薄封装，并验证方法名和 payload。

## 13. CI 门禁

- OpenAPI Schema 校验通过。
- 不存在重复 OperationId。
- 所有 Operation 声明认证和错误响应。
- 所有写操作声明幂等或说明为何不需要。
- 所有列表声明游标分页。
- 示例和描述中无秘密或真实 PII。
- 生成 TypeScript Client 无类型错误。
- Provider 原生契约测试不依赖跨协议转换。

## 14. 验收场景

1. Tenant API 与 Ops API 的认证方案明确分离。
2. OpenAI 与 Anthropic 数据面不合并 Schema。
3. 所有创建和更新操作具备幂等/并发契约。
4. 列表使用游标分页。
5. 错误模型稳定且脱敏。
6. 前端类型从 OpenAPI 生成。
7. SignalR 不替代 HTTP 事实来源。
8. OpenAPI 变更进入 CI 契约门禁。
