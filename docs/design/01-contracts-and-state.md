# 01 基础契约与状态模型

- 版本：v0.1（待评审）
- 范围：租户边界、身份、API、实体、状态机、事件与一致性
- 对应缺口：G-02、G-03、G-07、G-16

## 1. 基本决策

1. 所有租户业务实体显式携带 `tenant_id`，仓储层强制过滤，数据库约束兜底。
2. Runner 的部署身份与租户任务身份分离。共享 Runner 不为某个租户持有长期 JWT。
3. 控制面可以以服务身份调用网关，但只能调用管家推理和模型管理端点，不能读取模型密钥。
4. 状态变更、消息和审计通过事务 Outbox 保证原子性。
5. 所有写接口支持幂等；所有并发状态变更支持版本校验。
6. 事件和 API 从第一天带版本号，不依赖口头兼容。

## 2. 标识、作用域与时间

| 项 | 规则 |
|---|---|
| 主键 | UUID v7；数据库排序与时间戳分离 |
| 租户 ID | UUID；不得从路径、请求体或 JWT 中任选其一，必须交叉校验 |
| 时间 | 数据库统一 UTC；展示和 cron 按租户时区 |
| 金额 | 最小货币单位整数 + ISO 4217 币种；禁止浮点 |
| 版本 | 实体 `row_version` 整数；事件 `schema_version` 整数 |
| 幂等键 | 客户端生成 UUID；服务端按 `(tenant_id, operation, key)` 唯一 |
| 删除 | 业务实体默认软删除或归档；密钥和认证材料支持物理删除 |

### 2.1 系统级实体

以下实体不属于租户数据，使用显式 `scope=system`：

- Tenant、User、Membership、Plan、Subscription。
- Deployment、Runner、Gateway、EnrollmentToken。
- 全局模型能力基准与 Provider 价目缓存。

其余实体必须带 `tenant_id`。系统级实体不得通过普通租户仓储访问。

## 3. 身份与授权

### 3.1 凭据类型

| 凭据 | 用途 | 生命周期 | 可访问范围 |
|---|---|---|---|
| Human access token | 股东和租户用户登录 | 15 分钟 | 指定租户 + 用户角色 |
| Operator access token | 提供方运营人员登录 | 15 分钟 | system + 运营角色，MFA/step-up |
| SupportGrant token | 运营人员临时访问指定租户 | 最长 30 分钟，可撤销 | 单租户 + 显式范围 + operator_id |
| Refresh token | 换取 access token | 30 天，轮换使用 | 绑定用户/运营会话，不跨身份类型 |
| Runner enrollment token | 首次注册 Runner | 一次性，24 小时 | 仅注册指定部署 |
| Runner credential | Runner 调用控制面 | 长期，支持轮换/吊销 | 指定部署，不含租户权限 |
| Runner deployment token | 注册、心跳、领取任务 | 5 分钟 | 指定部署和 Runner |
| Employee assignment token | 执行租户任务、调用网关 | 15 分钟，可续签 | 单租户、单员工、指定任务和 scopes |
| Control service token | 管家推理、模型管理 | 5 分钟 | 单租户 + 服务 scopes |
| Ops service token | 运营平台调用控制面/网关/Runner | 5 分钟 | system 或指定租户 + 运维 scopes |
| Gateway service credential | 网关注册、心跳、用量上报 | 长期，支持轮换 | 指定部署和 Gateway |

### 3.2 JWT claims

```text
iss: regent-control | regent-ops
aud: regent-control | regent-gateway | regent-ops
sub: user | operator | runner | controller | employee | service
kid: signing-key-id
exp, iat, nbf
jti: unique-token-id
scope: deployment | tenant | system | support
tenant_id: 租户或支持访问令牌必填
operator_id: 运营或支持访问令牌必填
support_grant_id: 支持访问令牌必填
runner_id: Runner 令牌必填
controller_id: 主控令牌必填
employee_id: 员工任务令牌必填
task_id: 员工任务令牌必填
scopes: space-separated permissions
```

规则：

- Gateway 只接受 `aud=regent-gateway`；ControlPlane 接受 `aud=regent-control`；运营平台只接受 `aud=regent-ops`。
- Operator token 不能访问租户 API；租户 token 不能访问 Ops API。
- SupportGrant token 必须同时匹配 `operator_id`、`tenant_id`、`support_grant_id`、scope 和有效期。
- `tenant_id` 必须与资源路径、任务分配和模型归属一致，不一致返回 403。
- 员工令牌不得包含 `admin`、`models:write`、`escalations:approve` 等管理权限。
- 管家服务令牌只包含 `infer:butler`；模型管理使用独立 `models:admin` 服务令牌。两类令牌都不能读取密钥，也不默认同时授予。
- 签名密钥通过 JWKS 发布；轮换时新旧 `kid` 至少重叠 24 小时。
- 吊销通过 `jti` 黑名单或凭据版本号实现，最长传播延迟 60 秒。

### 3.3 关键认证流程

**Runner 注册与领活**

1. 运营方在控制面生成一次性 enrollment token。
2. Runner 使用 token 注册，控制面返回 `runner_id` 和长期 credential。
3. Runner 用 credential 换取 deployment token。
4. Runner 长轮询领取任务，控制面为每个任务签发 employee assignment token。
5. Runner 只在该任务的生命周期内使用 assignment token；任务结束即失效。

**管家调用网关**

1. 控制面为当前租户和管家会话签发 control service token。
2. 管家按模型协议通过 OpenAI 或 Anthropic 原生数据面调用网关。
3. 网关验证 `aud`、`tenant_id`、`infer:butler` 和预算预留。
4. 网关生成用量事件并推送控制面。

**运营人员支持访问**

1. 运营平台验证 Operator 身份、角色和 MFA。
2. 运营人员申请指定租户、范围、时长和权限的 SupportGrant。
3. 租户批准或按预设策略授权只读范围；紧急访问需要双人审批。
4. 运营平台签发短时 SupportGrant token。
5. 控制面记录访问并强制租户、范围、有效期和审计校验。

**网关注册与用量上报**

1. 网关首次启动使用部署内 bootstrap credential 注册。
2. 控制面返回 gateway service credential。
3. 网关用心跳和用量上报接口调用控制面，所有请求带部署和网关身份。

### 3.4 权限矩阵

| 能力 | 股东 | 运营方 | 主控/员工 | 管家 | Runner | 网关 |
|---|---|---|---|---|---|---|
| 修改章程/预算 | 是 | 默认否，需显式支持授权 | 否 | 否 | 否 | 否 |
| 创建岗位 | 否 | 默认否，需显式支持授权 | 主控或具备 `position.create` 能力者 | 否 | 否 | 否 |
| 自建岗位/组织规划 | 否 | 否 | 主控或具备 `org.design` 能力者 | 否 | 否 | 否 |
| 管理租户账户/配额 | 否 | 是 | 否 | 否 | 否 | 否 |
| 读取租户业务数据 | 是 | 仅 SupportGrant 范围 | 按岗位能力 | 只读工具 | 否 | 否 |
| 领取任务 | 否 | 否 | 否 | 否 | 是 | 否 |
| 调用模型 | 否 | 否 | 受限 scope | 推理 scope | 代理员工 scope | 执行 |
| 读取模型密钥 | 否 | 否 | 否 | 否 | 否 | 仅网关内部 |
| 批准升级 | 是 | 仅平台事件 | 否 | 建议 | 否 | 否 |
| 上报用量 | 否 | 聚合查看 | 工具用量 | 否 | 工具用量 | 模型用量 |

## 4. API 通用约定

### 4.1 协议

- 基础路径：`/v1`；JSON over HTTPS。`/v1` 仅用于系统内部调用，不构成公共 API 或兼容性承诺。
- 每个响应带 `request_id`；服务日志和审计使用同一 ID。
- 创建类接口必须带 `Idempotency-Key`。
- 更新类接口必须带 `If-Match: "<row_version>"`。
- 列表接口使用游标分页：`limit` + `cursor`，禁止无限 offset。
- 时间使用 RFC 3339 UTC；cron 和展示时区单独传递。

### 4.2 错误模型

```json
{
  "request_id": "uuid",
  "error": {
    "code": "BUDGET_HARD_LIMIT",
    "message": "human readable message",
    "retryable": false,
    "details": {"required": "0.42", "available": "0.18"}
  }
}
```

| HTTP | code 示例 | 含义 |
|---|---|---|
| 400 | INVALID_ARGUMENT | 参数格式错误 |
| 401 | UNAUTHENTICATED | 凭据缺失或无效 |
| 403 | TENANT_MISMATCH | 租户或权限不匹配 |
| 404 | NOT_FOUND | 资源不存在或不可见 |
| 409 | IDEMPOTENCY_CONFLICT | 幂等键对应不同请求 |
| 409 | INVALID_STATE | 状态机不允许 |
| 412 | VERSION_CONFLICT | row_version 过期 |
| 422 | POLICY_DENIED | 策略或业务规则拒绝 |
| 429 | RATE_LIMITED / BUDGET_HARD_LIMIT | 限流或预算限制 |
| 503 | DEPENDENCY_UNAVAILABLE | 下游不可用，可重试 |

### 4.3 API 域

| 域 | 主要端点 |
|---|---|
| 认证 | `/auth/login`、`/auth/refresh`、`/auth/logout` |
| 租户 | `/tenants`、`/tenants/{id}/members`；`/tenants/{id}/export` 按 Q-033 延后 |
| 公司 | `/companies`、`/charters`、`/charters/{id}/activate` |
| 组织 | `/controllers/{id}/self-position`、`/positions`、`/positions/{id}/assignments`、`/org-delegations`、`/org-changes`、`/successions`、`/employees`、`/employees/{id}/heartbeat` |
| Tool | `/tools/discover`、`/tools/expand`、`/tools/activate`、`/tools/{name}/invoke`、`/tools/grants`、`/tools/grants/{id}/revoke` |
| 项目 | `/projects`、`/requirements`、`/tasks/{id}/claim`、`/defects`、`/reviews`、`/debates/{id}/adjudicate` |
| 通信 | `/communication/contacts`、`/communication/routes`、`/communication/overrides`、`/communication/grants` |
| 聊天 | `/conversations`、`/conversations/{id}/messages`、`/messages/{id}/ack`、`/messages/{id}/promote`、`/collaboration/delegations`、`/collaboration/handoffs` |
| 工作流 | `/workflows`、`/workflows/{id}/versions`、`/workflows/{id}/proposals`、`/workflow-proposals/{id}/review`、`/workflows/{id}/runs` |
| 记忆 | `/memories`、`/memories/search`、`/memories/{id}/relations`、`/memories/{id}/proposals`、`/memory-proposals/{id}/review`、`/memories/{id}/archive`、`/memories/{id}/purge` |
| 绩效 | `/improvement-proposals`、`/kpis`、`/kpis/{id}/snapshots`、`/performance-reviews`、`/performance-actions/{id}/appeal` |
| 任务 | `/tasks/{id}/claim`、`/tasks/{id}/lease`、`/tasks/{id}/complete` |
| 消息 | `/messages?after_seq=`、`/messages/{id}/ack` |
| 成果 | `/artifacts`、`/artifacts/{id}/content`、`/workspaces/{id}/assign` |
| 升级 | `/escalations`、`/escalations/{id}/decision` |
| 管家 | `/butler/chat`、`/butler/jobs`、`/butler/reports` |
| 模型 | `/admin/tenants/{tenant}/models`、`/test`、`/rotate`、`DELETE` |
| 计量 | `/internal/llm-usage`、`/internal/tool-usage`、`/usage/reconcile` |
| 运维 | `/gateways/register`、`/gateways/{id}/heartbeat`、`/backups` |
| 运营 | `/ops/tenants`、`/ops/quotas`、`/ops/runner-pools`、`/ops/incidents`、`/ops/support-grants`、`/ops/audit-events` |

### 4.4 Tool 契约

```text
ToolManifest:
  name, description
  input_schema, output_schema
  required_capabilities[]
  side_effect: read | internal_write | external_write | destructive
  resource_scope, approval_requirement
  rate_limit, idempotency, audit_level

ToolProfile:
  id, tenant_id, position_id?, task_type?
  primary_tools[], auxiliary_tools[], restricted_tools[]

ToolActivation:
  id, tenant_id, employee_id, task_id
  tool_name, reason, status, expires_at?

ToolInvocation:
  tool_call_id, tenant_id, employee_id, task_id
  tool_name, profile_id, activation_id?
  arguments_hash, policy_decision
  result_status, domain_event_ids, created_at
```

- `/tools/discover` 返回当前 Tool Profile 的 Primary 完整 Schema、Auxiliary 名称与简介。
- `/tools/expand` 返回已授权 Auxiliary Tool 的完整 Schema。
- `/tools/activate` 在权限上限内激活 Auxiliary 或申请 Restricted 权限。
- `/tools/{name}/invoke` 每次重新鉴权，不信任发现阶段缓存。
- 执行结果为 `Allowed`、`NeedsActivation`、`Forbidden` 或 `RequiresApproval`。
- 未授权 Tool 不返回 Schema，只返回 `TOOL_NOT_AVAILABLE` 或 `FORBIDDEN`。
- 写 Tool 必须携带幂等键，重复调用返回首次结果。

---

## 5. 核心实体

| 实体 | 关键字段 | 不变量 |
|---|---|---|
| Tenant | id、owner_user_id、plan、status、timezone | 一个租户在 v1 只有一家公司 |
| OwnerProfile | tenant_id、user_id、display_name、title_suffix | 仅用于 Boss 展示，不参与权限判断 |
| SentinelEvent | id、tenant_id、rule_id、severity、metric_snapshot、action_taken | 由纯算法生成，不调用 LLM |
| Operator | id、email、status、mfa_enabled | 运营身份不与租户用户复用 |
| TenantAccount | tenant_id、account_status、plan_id、quota_policy_id | 账户、套餐和配额以运营平台为权威源 |
| SupportGrant | tenant_id、operator_id、scope、expires_at、approvals | 限时、限范围、可撤销，默认只读 |
| RunnerPool | id、labels、capacity、scheduling_policy | 运营平台管理池，控制面执行租户调度 |
| Incident | id、severity、status、impact、timeline | 状态页内容必须脱敏 |
| OpsAuditEvent | operator_id、action、target、request_id、created_at | 运营操作只增不改 |
| Company | id、tenant_id、status、mission | 必须关联有效 Charter |
| CharterVersion | id、tenant_id、version、content、activated_at | 已激活版本不可变；新版本生效需记录 |
| Controller | id、tenant_id、company_id、state、active_position_id、active_employee_id | 主控持续存在，但不是预设岗位 |
| Position | id、tenant_id、title、charter、capabilities、model_tier、slot_state、reports_to_position_id | 岗位占用一个编制槽；名称不参与权限判断；可空缺 |
| PositionAssignment | id、tenant_id、employee_id、position_id、started_at、ended_at | 一个员工一个 primary assignment，一个岗位一个主任职者 |
| OrgManagementGrant | id、tenant_id、employee_id、scope_position_id、allowed_actions、expires_at | 按子树、动作、预算和期限授权，可撤销 |
| Employee Agent | id、tenant_id、employee_no、runtime_kind、controller_id?、state、assigned_runner | 独立 Agent 运行时；工号永久唯一；通过任职关系绑定岗位 |
| CapabilityGrant | id、tenant_id、employee_id、capability、scope | 权限按能力授予，不按岗位名称 |
| InternalApiClient | id、kind、audience、scopes、tenant_scope、status | 内部服务身份，不是公共 OAuth Client |
| McpToolSurface | id、kind、audience、tool_names、scopes、status | 仅 Butler 只读或模型评测工具面 |
| ToolPolicy | tool_name、required_capabilities、side_effect、scope_rules、approval_requirement | 发现和执行共用同一策略来源 |
| ToolProfile | id、tenant_id、position_id?、task_type?、primary_tools、auxiliary_tools、restricted_tools | 当前任务默认披露集合，不扩大权限上限 |
| ToolActivation | id、tenant_id、employee_id、task_id、tool_name、reason、status、expires_at? | 激活不得突破 Authorized Tools |
| ToolInvocation | tool_call_id、tenant_id、employee_id、task_id、tool_name、profile_id、activation_id?、policy_decision | 每次调用可审计、可幂等 |
| Project | id、tenant_id、name、mission、status、owner_employee_id、budget_ref | 项目按租户隔离；终止必须复盘 |
| Requirement | id、project_id、title、problem、scope、priority、status、acceptance_criteria、version | Ready 前必须有验收标准 |
| Task | id、requirement_id、required_capabilities、status、assignee_id、wip_limit | 必须归属需求；认领受能力与 WIP 限制 |
| TaskWeight | id、task_id、complexity_factors、weight、rubric_version、rated_by、approved_by、locked_at | Ready 前锁定；员工不能自评 |
| TaskRequirement | task_id、dimension、minimum_score、preferred_score、requires_pairing、requires_review | 认领前固定能力需求 |
| CapabilityAssessment | id、employee_id、dimension、model_score、history_score、tool_fit、context_fit、effective_score、confidence、version | 模型与历史表现共同校准 |
| Defect | id、requirement_id、task_id?、reported_by、assigned_to、severity、status、evidence_refs | 测试复测关闭，不能静默关闭 |
| Discussion | id、subject_type、subject_id、status | 线程关联工作项；评论更正新增记录 |
| Debate | id、subject_type、subject_id、question、options、status、evidence_deadline、decision_deadline、adjudicator_id | 超时必须升级或形成决策 |
| Review | id、subject_type、subject_id、reviewer_id、author_id、quorum、verdict、findings、deadline_at | 高风险评审作者与评审者分离 |
| ReviewFinding | id、review_id、severity、category、description、evidence_refs、affected_scope、recommended_action、blocking | 结论必须基于证据和明确 findings |
| AcceptanceReport | id、project_id、release_candidate_id、persona_id、scenario_results、design_scores、findings、verdict、evidence_refs | 验收证据不可由开发改写；结论按固定门槛计算 |
| DoneEvidence | id、work_item_type、work_item_id、criteria_results、artifact_refs、test_results、review_results、security_checks、validator_id、version | 控制面依据证据关闭，不可静默改写 |
| TaskCheckpoint | id、task_id、goal、plan、current_step、decisions、risks、failures、artifact_refs、next_actions、version | 任务恢复真相；关键进展先于模型调用持久化 |
| ContextPacket | id、task_id、checkpoint_version、system_version、tool_profile_version、included_refs、excluded_refs、token_usage、cache_info | 记录本轮上下文组装、压缩和缓存审计 |
| PromptAssetVersion | id、tenant_id?、asset_type、scope、version、content_hash、parent_version、status、owner | 已发布版本不可变；系统提示只能平台管理 |
| PromptBundle | id、task_run_id、system_prompt_version、position_charter_version、skill_versions、tool_profile_version、context_policy_version | TaskRun 固定组合，不中途切换 |
| OnboardingBundle | id、employee_id、system_prompt_version、position_charter_version、starter_skill_versions、status | 聘用只安装基线与 Starter Skills |
| SkillAssignment | id、employee_id、skill_version、assigned_by、reason、effective_at、expires_at? | 进阶技能渐进分配，不授予权限 |
| EmployeeRuntimeBinding | id、employee_id、model_id、reasoning_effort、effective_from、effective_to? | 任务中不可临时更换模型或推理强度 |
| ToolLoopPolicy | id、task_type、max_model_turns、max_tool_calls、max_consecutive_failures、max_no_progress_turns、max_repeat_calls、timeout | 限制单次执行片段，不终止整个任务 |
| ToolLoopRecord | id、task_run_id、turn_count、tool_count、failure_count、no_progress_count、repeat_count、cost_snapshot、stop_reason | 记录续跑、停止、重复、成本和评审结果 |
| Decision | id、subject_type、subject_id、selected_option、rationale、evidence_refs、decision_maker | 不可删除；变更创建新决策 |
| PositionLabel | id、tenant_id、position_id、namespace、key、value?、version | 标签按岗位版本化，不按名称判断 |
| CommunicationProfile | id、tenant_id、position_id、direct_targets、target_labels、blocked_targets、route_policies | 只定义通信，不授予领域权限 |
| FunctionalEscalationRoute | id、tenant_id、source_employee_id、topic_label、target_employee_id、skipped_position_ids、org_version | 可解释、可审计、随组织版本失效 |
| CommunicationGrant | id、tenant_id、source_employee_id、target_employee_id、scope、expires_at | 临时或显式授权，可撤销 |
| Conversation | id、tenant_id、type、scope_type、scope_id、owner_employee_id?、status | 私人仅参与者，群聊按范围隔离 |
| ConversationMember | conversation_id、employee_id、role、joined_at、left_at | 离开范围后失去新内容访问权 |
| CollaborationMessage | id、tenant_id、conversation_id、seq、from_employee_id、type、priority、subject_ref、thread_id、correlation_id、deadline_at?、idempotency_key | 消息不直接改变领域状态 |
| MessageMention | message_id、mentioned_employee_id?、target_label?、status | @ 只是通知和路由 |
| MessagePromotion | message_id、target_type、target_id、promoted_by、created_at | 正式状态由目标对象管理 |
| Delegation | id、task_id、from_employee_id、to_employee_id、status、reason | 接收者确认后生效 |
| Handoff | id、task_id、from_employee_id、to_employee_id、checkpoint_ref、status | 原子转移所有权 |
| DecompositionPlan | id、requirement_id、requirement_version、tasks、acceptance_coverage、dependencies、risks、total_weight、status | LLM 提案，控制面校验和审批后生效 |
| WipCapacity | employee_id、capacity_weight、max_in_progress、max_heavy_tasks、reserved_capacity、version | 容量为 3/5/8，按固定任务权重计量 |
| ClaimInvitation | id、task_id、employee_id、effective_score、load_snapshot、status、expires_at | 认领邀请幂等，超时或拒绝后改推荐 |
| TaskTypeDefinition | task_type、required_capabilities、tool_profile、weight_rubric、dod_template、evidence_requirements、reviewer_requirements、kpi_dimensions、version | 任务类型不绑定岗位名称 |
| EmployeeWorkLedger | id、employee_id、task_id、task_type、task_weight、role_relation、quality_factor、capability_fit_factor、weighted_score、cost、duration、evidence_refs | 按事实记录贡献，父子与协作不得重复计分 |
| ModelUnavailableEvent | id、tenant_id、employee_id、model_binding_id、affected_task_ids、error_summary、last_success_at、retry_results、reported_leader_id、status | 算法检测并逐级上报，不自动换模型 |
| EmployeeOperationalState | employee_id、state、reason_code、attribution、changed_at、version | Active/Busy/Sleeping/RateLimited/Degraded 等确定性状态 |
| EmployeeStateEvent | id、tenant_id、employee_id、from_state、to_state、reason_code、attribution、evidence_refs、created_at | 状态由算法维护，KPI 必须归因 |
| CatalogSnapshot | id、source_url、fetched_at、raw_checksum、status、published_at、supersedes_id | models.dev 不可变目录快照 |
| ManualModelPrice | id、tenant_id、provider_id、model_id、field、amount、currency、unit、region、effective_at | BYOK 缺失价格补充，可被目录字段覆盖 |
| EffectiveModelPrice | model_id、field、amount、currency、unit、source、snapshot_id、manual_price_id | 按字段合并目录价和手工价 |
| ModelTierAssignment | source_model_id、tier、capability_score、reference_cost、policy_version、snapshot_id | cheap/mid/premium 自动分档结果 |
| BenchmarkSuite | id、tenant_id、name、capability_dimensions、visibility、version、status | 版本化评测集 |
| DatasetVersion | id、suite_id、version、content_hash、source_ref、license、redaction_status、split | 不可变数据集版本 |
| BenchmarkCase | id、dataset_version_id、case_hash、difficulty、risk_level、expected_evidence、sealed | 单个评测案例 |
| ScoringRubric | id、version、dimensions、weights、hard_fail_rules、judge_prompt_ref | 固定评分规则 |
| EvaluationRun | id、suite_id、dataset_version_id、model_version_id、prompt_bundle_id、tool_profile_id、runtime_version、reasoning_effort、seed、status | 可复现评测执行 |
| CaseResult | id、run_id、case_id、verdict、score_vector、evidence_refs、judge_ref、latency、cost | 案例结果 |
| EvaluationSummary | id、run_id、metrics、confidence_intervals、gate_results、baseline_run_id | 汇总与门槛判断 |
| EvaluationApproval | id、run_id、approver_id、decision、reason、expires_at | 审批记录 |
| JudgeAssignment | id、run_id、judge_employee_id、model_binding_id、independence、status | Judge 员工与模型独立性 |
| JudgeCalibration | id、judge_version、human_sample_refs、agreement、weighted_kappa、status | Judge 人工校准 |
| ContaminationFinding | id、dataset_version_id、case_id、detector、similarity、status、resolution_ref | 污染检测与隔离 |
| StatisticalGate | id、run_id、metric、estimate、ci_lower、margin、test、result | 统计门槛证据 |
| EvaluationRetest | id、run_id、failed_case_ids、regression_case_ids、safety_full、status | 复测范围 |
| ReleaseManifest | release_version、version_tag、source_commit、base_image_digests、schema_version、api_versions、compatible_versions | 版本 Tag 发布事实 |
| CiRun | id、repository、commit_sha、workflow_ref、status、test_refs、scan_refs | GitHub Actions 检查事实 |
| LocalBuild | id、host_ref、version_tag、source_commit、base_image_digests、built_image_ids、sbom_ref、status | 本地 Tag 源码构建记录 |
| LocalDeployment | id、host_ref、version_tag、built_image_ids、config_version、operator_id、status | 本地 Compose 部署记录 |
| InstallRun | id、host_ref、version_tag、source_commit、preflight_ref、steps、status | 自托管安装记录 |
| OfflineCacheManifest | id、host_ref、source_ref、image_refs、dependency_refs、checksums、status | 离线缓存校验 |
| UpgradeRun | id、host_ref、from_tag、to_tag、backup_policy、migration_ref、smoke_ref、status | 升级与恢复记录 |
| CapacityPolicy | id、scope、metric、limit、burst_limit、action、version | 容量与背压策略 |
| QueueLimit | id、scope、queue_name、limit、high_watermark、action | 队列上限和告警水位 |
| CapacityReport | id、version_tag、hardware_profile、load_profile、latency、fairness、resources、result | 容量验收证据 |
| MigrationRecord | id、from_schema、to_schema、checksum、status、started_at、completed_at | 不可静默改写的迁移记录 |
| UpgradeRun | id、from_version、to_version、phase、backup_refs、migration_status、rollback_decision | 升级与回滚审计 |
| SloDefinition | id、service、sli、target、window、error_budget_policy、version | 可版本化服务目标 |
| AlertEvent | id、level、service、slo_ref、status、ack_by、runbook_ref、created_at | 告警与处置记录 |
| DashboardSnapshot | id、tenant_id、card_type、subject_ref、status、data_at、freshness、evidence_ref | 仪表盘只读投影 |
| ProjectHealth | id、project_id、phase、health、rule_version、input_refs、calculated_at | 项目阶段与确定性健康分开 |
| ActionCenterItem | id、tenant_id、source_type、source_id、severity、owner_id、sla_due_at、dedupe_key、status | 风险聚合和去重 |
| TestEvidence | id、release_version、test_type、result_ref、coverage、flaky_items、created_at | 发布测试证据 |
| ObservabilityProfile | id、profile、signals、sampling、redaction、version | off/local/otlp/elastic 密封引用类型 |
| ObservabilityConfig | profile、metrics_enabled、logs_enabled、traces_enabled、endpoint_ref、retention、config_version | 启动时遥测配置 |
| TaskGrant | id、task_id、employee_id、workspace_scope、allowed_tools、allowed_network、budget_limit、allowed_actions、policy_version | 任务预授权边界 |
| UntrustedContent | id、tenant_id、task_id、source_type、source_ref、content_hash、taint_labels、allowed_uses、disposition | 不可信内容信封 |
| InjectionFinding | id、content_id、detector_version、rule_id、risk_level、evidence_ref、disposition | 注入检测与处置记录 |
| MemoryClaim | id、record_id、version、text、source_refs、confidence、verification、taint、status | 最小纠错单位 |
| MemoryFinding | id、claim_id、rule_id、risk_level、evidence_refs、status | 投毒或冲突发现 |
| MemoryCorrection | id、old_claim_id、new_claim_id、reason、evidence_refs、reviewer_id、decision | 版本化纠错 |
| MemoryImpactRecord | id、claim_version_id、target_type、target_id、used_at、risk、required_action | 引用影响 |
| CorrectionTask | task_id、finding_id、claim_id、assignee_id、reviewer_id、status、weight | 正式纠错任务 |
| SupplyChainArtifact | id、type、publisher、name、version、digest、signature_ref、license、sbom_ref、scan_ref、status | 不可变供应链制品 |
| AdmissionReview | id、artifact_id、requested_capabilities、allowed_network、secret_scope、reviewer_id、decision | 准入审核 |
| ArtifactRevocation | id、artifact_id、digest、reason、effective_at、propagation_status | 按版本撤销 |
| ArtifactImpactRecord | id、artifact_id、target_type、target_id、used_at、risk、required_action | 制品影响 |
| SensitiveDataPolicy | id、tenant_id、version、data_classes、pii_rules、secret_rules、model_egress_policy | 版本化敏感数据政策 |
| ComplianceProfile | id、tenant_id、preset、jurisdictions、allowed_regions、required_controls、version、status | 可配置合规策略 |
| ComplianceControl | id、profile_id、control_key、requirement、required、evidence_ref、status | 单项控制与证据 |
| ComplianceEvaluation | id、profile_id、subject_ref、action、result、policy_version | 动作前合规评估 |
| ComplianceViolation | id、profile_id、control_id、subject_ref、impact、status、appeal_ref | 违规与处置 |
| DlpFinding | id、tenant_id、source_type、source_ref、policy_version、rule_id、data_class、risk_level、action、status | 脱敏发现与处置 |
| TokenMapping | token_id、tenant_id、scope、data_type、encrypted_value_ref、policy_version、expires_at | PII 令牌映射 |
| DeletionProof | id、subject_ref、scope、completed_steps、retained_items、created_at | 删除传播证明 |
| DeletionRequest | id、subject_type、subject_id、status、deleted_at、restore_due_at、purged_at | 软删除与清除请求 |
| DeletionTombstone | id、subject_type、subject_id、scope、deleted_at、purged_at、result_hash | 防恢复复活的最小记录 |
| RetentionPolicy | id、tenant_id、data_type、retention_class、online_days、archive_days、version | 版本化保留策略 |
| ArchiveManifest | id、tenant_id、data_type、policy_version、watermark、record_count、status | 归档清单与验证 |
| ArchiveObject | id、archive_id、object_key、digest、size_bytes、storage_tier | 归档对象 |
| ArchiveRestoreJob | id、archive_id、requested_by、status、restore_prefix、expires_at | 临时恢复任务 |
| LegalHold | id、tenant_id、scope、reason、authority、starts_at、expires_at | 法律保留 |
| BackupProfile | id、deployment_id、enabled、component_flags、schedule、retention_days、version | 可选备份配置 |
| BackupJob | id、profile_version、component、status、snapshot_ref、manifest_ref、hash | 备份作业 |
| RestoreJob | id、backup_ids、scope、approval_ref、status、rpo_actual、rto_actual | 恢复作业 |
| RightsDeclaration | id、tenant_id、artifact_id、owner_type、rights_status、provider_terms_ref | 产出权利声明 |
| ProvenanceRecord | id、tenant_id、artifact_id、task_id、model_version、tool_refs、output_hash | 产出来源链 |
| ThirdPartyNotice | id、artifact_id、source_ref、license_id、attribution_required、notice_text | 第三方归属与许可 |
| HumanAuthorshipRecord | id、artifact_id、account_id、contribution_type、evidence_refs | 人类创作贡献记录 |
| AuditCheckpoint | id、stream_id、from_seq、to_seq、merkle_root、signing_key_id、signature、object_ref | 签名审计检查点 |
| AuditVerification | id、stream_id、result、checked_range、evidence_ref、created_at | 完整性验证报告 |
| AuditAnchor | id、checkpoint_id、anchor_type、external_ref、status、confirmed_at | 外部锚定记录 |
| EmploymentPeriod | id、employee_id、started_at、ended_at、status、reason | 员工任职周期 |
| PendingRemoval | id、employee_no、rating_snapshot_id、reason、evidence_refs、review_id、appeal_status、boss_decision | 待驱逐队列 |
| BossDecision | id、subject_type、subject_id、decision、reason、evidence_refs、decided_at | Boss 最终用人决定 |
| OffboardingHandoff | id、employee_no、open_tasks、artifact_refs、snapshot_refs、recipient_id、status | 离职工作交接 |
| OffboardingRecord | id、employee_no、effective_at、revoked_credentials、subscription_removed、deletion_proof_id | 离职执行证明 |
| ContentSafetyPolicy | id、tenant_id、version、jurisdiction、categories、detector_refs、disposition_rules、status | 版本化内容政策 |
| ContentFinding | id、content_ref、content_hash、policy_version、detector_version、category、risk_level、disposition、status | 内容安全发现 |
| ContentReview | id、finding_id、reviewer_id、decision、reason、evidence_refs | 独立复核 |
| ContentException | id、policy_id、scope、reason、approved_by、expires_at | 限期内容例外 |
| TakedownRequest | id、content_ref、reason、requester_ref、status、completed_at | 下架流程 |
| SecurityIncident | id、tenant_id、severity、status、incident_commander_id、impact、timeline | SEV1～SEV4 安全事件 |
| ContainmentAction | id、incident_id、action_type、target_ref、reason、approval_ref、result | 遏制动作 |
| EvidenceItem | id、incident_id、type、source_ref、content_hash、storage_ref、legal_hold | 事件证据 |
| IncidentNotification | id、incident_id、audience、channel=dashboard、status、sent_at | 事件站内通知 |
| Notification | id、tenant_id、recipient_id、level、type、content_ref、status | 站内权威通知记录 |
| NotificationDelivery | id、notification_id、mode、attempt、status、cursor | SignalR 提示与重拉状态 |
| NotificationPreference | id、tenant_id、recipient_id、levels、digest、quiet_hours | 站内通知偏好 |
| NotificationSuppression | id、tenant_id、rule_version、group_key、reason、hit_count | 去重和抑制证据 |
| Postmortem | id、incident_id、root_cause、actions、owner_ids、status | 复盘与改进行动 |
| PasswordCredential | id、user_account_id、algorithm、parameters、password_hash、updated_at | 账户密码凭据 |
| EmailClaim | id、user_account_id、email_normalized、source、verified、verified_at | 邮箱及验证来源 |
| ExternalIdentityBinding | id、user_account_id、provider、provider_subject、linked_at | GitHub 等外部身份绑定 |
| RecoveryCodeSet | id、user_account_id、generated_at、invalidated_at | 一次性恢复码集合 |
| RecoveryCode | id、recovery_code_set_id、code_hash、used_at | 单次恢复码 |
| AccountRecoveryRequest | id、user_account_id、channel、status、requested_at、expires_at | 自助找回请求 |
| OAuthTransaction | id、user_account_id、provider、state_hash、pkce_hash、expires_at | 短期 OAuth 事务 |
| OidcProviderConfig | id、tenant_id、issuer、client_id、client_secret_ref、status、version | 租户 OIDC 配置 |
| SsoInvitation | id、tenant_id、email_normalized、capabilities、status、expires_at | SSO 邀请 |
| OidcIdentityBinding | id、tenant_id、provider_config_id、issuer、subject、user_account_id | OIDC 身份绑定 |
| AuthenticationPolicy | tenant_id、mode、acr_policy、group_mapping_version、emergency_access | 租户认证策略 |
| EmergencyAccessGrant | id、tenant_id、user_account_id、scope、expires_at、used_at | 受限紧急访问 |
| TenantMembership | id、tenant_id、account_id、status、membership_version | 账户租户成员关系 |
| TenantOwnership | membership_id、tenant_id、kind、granted_at、revoked_at | Primary/Co-Owner 所有权 |
| OwnerInvitation | id、tenant_id、inviter_id、target_ref、token_hash、expires_at、status | 所有者邀请 |
| OwnerDelegation | id、tenant_id、grantor_id、delegate_id、capabilities、expires_at | 限时治理委托 |
| ActiveTenantContext | session_id、account_id、tenant_id、membership_version、capabilities | 当前租户会话上下文 |
| OrgChange | id、tenant_id、before_snapshot、after_snapshot、reason、evidence_ids | 组织变更必须可审计、可回放 |
| WorkflowDefinition | id、tenant_id、scope_type、owner_employee_id?、scope_position_id?、status | 个人范围属于作者；组织范围绑定岗位子树 |
| WorkflowVersion | id、workflow_id、version、definition_json、status | 已采纳版本不可变；Run 固定版本 |
| WorkflowProposal | id、workflow_id、from_version_id、to_version_id、scope_position_id? | 只能沿作者岗位向上提名 |
| WorkflowReview | id、proposal_id、reviewer_employee_id、decision | 仅具备 `workflow.review` 的 Leader 可采纳 |
| WorkflowRun | id、tenant_id、workflow_id、version_id、state、metrics | 运行不提升执行者权限 |
| MemoryRecord | id、tenant_id、kind、scope_type、owner_employee_id?、scope_position_id?、status | 个人记忆默认私有；组织范围绑定岗位子树 |
| MemoryVersion | id、memory_id、version、content_artifact_id、source_refs、confidence | 已发布版本不可变，向量索引记录模型版本 |
| MemoryRelation | id、tenant_id、from_id、to_id、relation_type | 多跳每跳重新校验权限和范围 |
| MemoryProposal | id、memory_id、target_scope_type、scope_position_id? | 只能沿作者岗位向上提名 |
| MemoryReview | id、proposal_id、reviewer_employee_id、decision | 组织/知识库发布需审查 |
| MemoryEmbedding | memory_version_id、embedding_model、dimension、vector、content_hash | 向量是索引，不是事实来源 |
| MemoryUsageFeedback | id、memory_version_id、employee_id、feedback_type | 反馈生成更正候选，不直接改事实 |
| ImprovementProposal | id、tenant_id、employee_id、proposal_type、scope_type、scope_position_id? | 个人可提案，组织范围向上审查 |
| KpiDefinition | id、tenant_id、subject_type、subject_id、metric_key、data_source、target、cadence | KPI 基于权威事实，不可由员工直接修改 |
| KpiSnapshot | id、kpi_definition_id、subject_id、period、value、evidence_refs | 快照不可变，结论必须引用证据 |
| PerformanceReview | id、tenant_id、subject_employee_id、reviewer_employee_id、status、confidence | 自评、复核、动作和申诉分离 |
| PerformanceAction | id、review_id、action_type、evidence_digest、approval_kind、ai_reviewer_id?、human_approval_id?、status | 非解雇动作独立 AI 复核，解雇需人类批准 |
| HumanPerformanceReview | id、action_id、human_account_id、outcome、findings、corrective_action_refs | 人类只做事后审查，可推翻结论和要求纠正 |
| FairnessPolicy | id、tenant_id、position_id、version、criteria、protected_attributes、audit_thresholds | 招聘前冻结岗位相关标准和盲评规则 |
| CandidateAssessment | id、tenant_id、requisition_id、candidate_ref、rubric_version、scores、evidence_refs | 评分与身份资料隔离 |
| FairnessAudit | id、tenant_id、requisition_id、slice_definitions、sample_sizes、selection_rates、findings | 分群偏差调查 |
| BiasFinding | id、audit_id、affected_slice、evidence_refs、severity、remediation、status | 偏差纠正任务 |
| CandidateAppeal | id、tenant_id、candidate_ref、assessment_id、appeal_type、independent_reviewer_id、outcome | 候选人申诉和复测 |
| LicenseInventory | id、artifact_digest、component_ref、spdx_id、license_text_ref、compatibility_status | 制品许可证清单 |
| ReleaseLicenseReport | id、release_id、artifact_digest、sbom_ref、license_ref、notice_ref、scan_status | 发布许可门禁 |
| ContributionAttestation | id、repository、commit_sha、contributor_ref、signoff_ref、authority_attested | 贡献者确认 |
| LocalePolicy | id、tenant_id、default_ui_locale、default_content_locale、timezone、fallback_locale、version | 租户和用户本地化策略 |
| TranslationResource | id、resource_key、source_locale、target_locale、target_text、status、version | 可版本化翻译资源 |
| TranslationReview | id、translation_resource_id、reviewer_id、decision、reviewed_at | 安全/法律/审批文案复核 |
| Task | id、tenant_id、employee_id、type、state、attempt、idempotency_key | 同一业务幂等键不能并发执行 |
| TaskLease | task_id、runner_id、token、expires_at | fencing token 每次租约递增 |
| Message | id、tenant_id、seq、to_id、type、payload、schema_version | `(tenant_id, seq)` 唯一 |
| InboxCursor | tenant_id、consumer_id、last_seq | 仅单调前进 |
| Workspace | id、tenant_id、project_id、task_id、repo_key、toolchain、local_path_template、snapshot_ref | 活动目录与 Runner 解耦，可从 RustFS 恢复 |
| WorkspaceSnapshot | id、tenant_id、workspace_id、object_key、git_ref、checksum、version | RustFS 快照不可变 |
| Artifact | id、tenant_id、project_id、task_id、kind、object_key、hash、size、upload_state | Ready 对象不可覆盖 |
| ObjectUpload | id、tenant_id、artifact_id、object_key、checksum、state、attempts | PendingUpload 到 Ready 显式转换 |
| Model | id、tenant_id、provider、tier、key_ref、price_version、state | key_ref 不包含密钥内容 |
| ModelKeyRecord | key_ref、tenant_id、provider、model_id、key_version、encrypted_key、wrapped_data_key、state | Postgres keyvault schema 密文记录 |
| UsageEvent | id、tenant_id、source、source_event_id、task_id、amount | `(source, source_event_id)` 唯一 |
| LedgerEntry | id、tenant_id、usage_event_id、account、amount、currency、cost_dimensions | 只增不改；更正用新条目 |
| CostAllocationRule | id、tenant_id、source_cost_center、targets、weights、version、approval_ref | 权重合计 100%，分摊不新增费用 |
| BudgetEnvelope | id、tenant_id、project_id?、amount、currency、period、soft_or_hard | 项目预算属于租户预算 |
| BudgetCarryover | id、source_period、target_period、amount、approval_ref | 仅显式批准，预留/欠款/超支不得结转 |
| CostAnomaly | id、rule_id、rule_version、subject、severity、evidence_refs、status | 确定性规则，不自动处罚 |
| BudgetReservation | id、tenant_id、task_id、amount、state、expires_at | 同一预算并发扣减必须原子 |
| Escalation | id、tenant_id、action_hash、state、expires_at、decision | 审批绑定不可变动作参数 |
| NegotiationRecord | id、tenant_id、escalation_id、participants、evidence_refs、authority_refs、outcome | 缺席协商证据和结论 |
| ApprovalTierPolicy | id、tenant_id、action_type、tier、required_capability、quorum、sla | 版本化分级审批策略 |
| ApprovalDelegation | id、tenant_id、grantor_id、delegate_id、scope、starts_at、expires_at、accepted_at、revoked_at | 限时、限范围、不可转授权 |
| ApprovalRoute | id、escalation_id、route_version、assignee_id、on_behalf_of、reason | 可解释审批路由 |
| ButlerJob | id、tenant_id、cron、timezone、prompt_template、next_run_at | 同一触发时间只生成一个报告 |
| WorkSchedule | id、tenant_id、timezone、windows、blocked_windows、quiet_windows、version | 全天可配置公司窗口 |
| CostWindow | id、tenant_id、provider_id?、model_id?、starts_at、ends_at、discount_value、source_ref、version | 用户配置的低价窗口 |
| HolidayCalendar | id、tenant_id、dates、recurrence_rule、schedule_mode、version | 用户可配置节假日作息 |
| WakePolicy | id、tenant_id、event_type、priority、eligible_roles、budget_limit、max_duration | 紧急唤醒边界 |
| ScheduleOverride | id、tenant_id、scope、reason、starts_at、expires_at、budget_limit、status | 限时加班 |
| AuditEvent | id、tenant_id、actor、action、target、payload_hash、created_at | 只增不改 |

## 6. 状态机

### 6.1 Employee

```text
Pending -> Interviewing -> Active <-> Sleeping
                              |  \
                              |   -> Frozen -> Active
                              -> Terminated
```

- `Pending`：岗位已创建，尚未完成入职。
- `Interviewing`：笔试、实操或评分进行中。
- `Active`：可领取任务。
- `Sleeping`：无任务，保留岗位和分配，不持有运行租约。
- `Frozen`：编制冻结，不领新任务；在途任务完成或取消。
- `Terminated`：岗位归档，槽位和预算释放。

Runner 心跳超时产生 `health=orphaned`，不直接改变员工业务状态；调度器重新分配后才能恢复 `Active`。

### 6.2 Position

```text
Draft -> Open -> Filled -> Frozen -> Closed
            \-> Closed
```

- `Draft`：JD 未通过审核。
- `Open`：占用空槽，可进入面试。
- `Filled`：唯一在职员工占用该槽。
- `Frozen`：保留槽位但不招聘。
- `Closed`：释放槽位，JD 归档。

录用与槽位占用必须在同一事务中完成，禁止并发招聘造成超编。

### 6.3 Task

```text
Pending -> Assigned -> Running -> Succeeded
    ^          |          |  \-> Failed
    |          |          \-> Cancelled
    |          \-> lease expired -> Pending
    \-> retryable failure -> Pending
```

- 每次领取生成新的 `lease_token`。
- 完成、失败和取消必须携带当前 `lease_token`。
- 旧 token 的迟到提交返回 409，不得覆盖新尝试结果。
- 非幂等副作用在提交前必须通过外部幂等键保护。

### 6.4 Project

```text
Proposed -> Researching -> BriefReady -> Approved -> Building
              ^              |  \-> Terminated          |
              |              \-> Researching            v
              \---------------------------------- Verifying
                                                         |  \
                                                         |   -> Building
                                                         v
                                                      Releasing
                                                         |
                                                         v
                                                        Live
                                                         |
                                                         v
                                                      Iterating
                                                         |
                                                         v
                                                     Terminated
```

- `BriefReady` 的简报缺少成功指标时不能进入 `Approved`。
- `Verifying` 必须同时通过自动化门禁和独立体验门禁。
- `Releasing` 失败回到 `Verifying` 或 `Building`，不得直接进入 `Live`。
- `Live` 只能基于运行时指标进入 `Iterating` 或 `Terminated`。

### 6.5 Escalation

```text
Open -> Approved -> Executing -> Executed
  |        |                         \-> ExecutionFailed
  |        \-> Superseded
  \-> Rejected
  \-> Expired
```

- 审批绑定 `action_hash`、参数、资源、租户和有效期。
- 参数变化必须创建新升级事项，旧事项作废。
- 超时任务将 `Open` 转为 `Expired`，任何执行端点都拒绝。
- 执行结果必须回写审计和公司志。

### 6.6 Model

```text
Provisioning -> Testing -> Ready
      |            \-> Failed
      -> Deleting -> Deleted
Ready -> Rotating -> Ready
             \-> Failed -> Ready（保留旧密钥）
```

- 只有 `Ready` 模型可接收生产推理。
- 轮换在新密钥测试成功前保留旧密钥。
- 删除必须先解除员工和任务引用。

### 6.7 Controller

```text
Unassigned -> Active -> SuccessionPending -> Active
                  \-> Dormant
```

- `Unassigned`：主控尚未创建有效自建岗位，只能执行 bootstrap 和组织修复。
- `Active`：主控绑定有效岗位，可正常编排。
- `SuccessionPending`：主控身份正在转移。
- `Dormant`：无可用主控或继任者，公司暂停并通知股东。

完整规则见 [06 主控与动态组织](./06-controller-and-organization.md)。

## 7. 事件与事务 Outbox

### 7.1 事件信封

```json
{
  "event_id": "uuid",
  "event_type": "task.completed",
  "schema_version": 1,
  "tenant_id": "uuid",
  "aggregate_id": "uuid",
  "aggregate_version": 3,
  "occurred_at": "2026-11-22T08:00:00Z",
  "causation_id": "uuid",
  "correlation_id": "uuid",
  "payload": {}
}
```

### 7.2 事务边界

以下操作必须在同一数据库事务中完成：

1. 实体状态变更。
2. 对应领域事件写入 `outbox_events`。
3. 需要即时通知时写入租户消息表。
4. 写入审计事件。

Outbox 发布器可以重试，消费者按 `event_id` 去重。事件顺序只保证同一 aggregate 内有序，不承诺跨公司全局有序。

### 7.3 消息与事件的区别

- **领域事件**：描述已经发生的事实，供投影、审计和自动化规则消费。
- **公司消息**：员工之间的工作输入，必须经过权限和内容策略检查。
- 两者共用投递基础设施，但使用不同的 topic/type 命名空间和保留策略。

## 8. 并发与一致性规则

- 编制、预算、模型密钥引用和升级执行使用数据库原子更新或行锁。
- 长流程使用 saga，不引入跨服务分布式事务。
- 所有外部调用使用 `correlation_id` 和 `idempotency_key` 贯穿控制面、Runner 和网关。
- 投影表可以重建；账本、审计和 Outbox 是权威源。
- 缓存不得成为授权或预算判断的唯一依据。

## 9. 契约验收

1. 使用 A 租户员工令牌访问 B 租户任务返回 403，且不泄露资源是否存在。
2. 共享 Runner 可先后执行两个租户任务，两个 assignment token 的 `tenant_id` 不串用。
3. 管家服务令牌不能调用模型管理或读取密钥端点。
4. 相同幂等键并发创建岗位只生成一个岗位和一个槽位占用。
5. 状态变更失败时，不产生孤立消息或审计事件。
6. 旧 fencing token 无法完成新租约的任务。
7. 事件消费者重复投递不产生重复业务效果。
8. API 返回的 `request_id` 能关联控制面、Runner 和网关日志。
9. 同一 ButlerJob 的同一计划触发时间只生成一个报告。
10. 管家会话不能读取其他租户或其他股东会话的历史。
11. 修改岗位名称不会改变员工权限、任务路由或审计关联。
12. 主控未自建岗位时不能获得员工工具权限。
13. 主控关闭自建岗位前必须完成继任，否则进入 `Dormant`。
14. 租户 token 访问 Ops API 返回 403，Operator token 访问租户 API 返回 403。
15. 无 SupportGrant 的运营人员无法读取租户业务数据。
16. SupportGrant 过期、撤销或超范围后访问立即拒绝。
17. 配额变更事件重复投递不会产生重复同步或状态倒退。
18. 运营平台无法通过 API 读取模型密钥明文。
19. 组织树出现环、多个根或超过 4 层时返回 422。
20. 同一员工不能同时拥有两个 primary assignment，同一岗位不能有两个主任职者。
21. 调岗时旧任职结束和新任职创建在同一事务中完成。
22. OrgManagementGrant 到期或撤销后，授权范围内的新变更立即被拒绝。
23. 受委托员工不能修改授权子树之外的岗位或主控自建岗位。
24. 普通开发调用 `/tools/discover` 时不会返回组织管理 ToolManifest。
25. 手动伪造未授权 Tool Call 在执行层被拒绝，且不返回 Tool Schema。
26. Tool 授权撤销后，发现列表和新的执行请求都立即拒绝该 Tool。
27. 同一 `tool_call_id` 重复提交只产生一次领域副作用。
28. Tool 参数校验、策略决策和领域结果可通过 `tool_call_id` 完整追踪。
29. 工作流只能沿作者岗位的上级路径提名，跨分支提案被拒绝。
30. Adopted 工作流版本不可修改，WorkflowRun 固定版本。
31. 无 `workflow.review` 能力的员工不能采纳组织工作流。
32. 工作流不能授予 Capability、OrgManagementGrant 或 Tool 权限。
33. 范围岗位关闭后工作流进入 `scope_orphaned`，停止新实例。
34. 工作流定义、提案、运行和证据均按租户隔离。
35. 个人记忆不能被主控或其他员工检索。
36. 记忆检索先执行租户和范围过滤，再执行向量与关键词召回。
37. 图谱多跳每一跳都重新校验权限，不能进入个人记忆或其他租户。
38. 已发布记忆版本不可变，更正通过新版本完成。
39. 过期记忆降权并最终停止普通召回。
40. 彻底删除后向量、全文、图关系、缓存和派生摘要均停止召回。
41. LLM 不能任意删除记忆，遗忘由确定性策略执行。
42. Embedding 模型更换后可重建索引并保留旧索引至验证完成。
43. 员工不能直接修改 KPI 快照或原始证据。
44. 员工不能评估自己并执行绩效动作。
45. KPI 不达标不会直接触发解雇。
46. 高影响绩效动作需要独立复核、申诉窗口和审计。
47. 主控无继任者时不能被执行 Terminate。
48. 申诉期间高影响动作暂停，非原评估者复核。
49. 改进提案不能自行扩大 Tool、Capability 或组织权限。
50. 主控默认 7 天绩效周期可配置，其他员工按岗位周期执行。
51. Authorized Tools 是权限上限，Tool Profile 不能扩大该上限。
52. Primary Tool 返回完整 Schema，Auxiliary Tool 默认只返回名称和简介。
53. Auxiliary Tool 展开或激活后仍由 PEP 重新鉴权。
54. Restricted Tool 未审批时返回 `RequiresApproval` 或 `NeedsActivation`。
55. 架构师默认看到架构设计和技术选型工具，编码工具按需激活。
56. 普通开发不能通过展开或激活获得组织管理和预算工具。
57. 工作流步骤切换 Tool Profile 不改变员工权限上限。
58. Tool 激活、展开、拒绝和调用均记录审计。
59. Requirement 无验收标准不得进入 Ready。
60. Task 必须归属 Requirement，认领必须原子化并受能力和 WIP 限制。
61. Defect 由测试复测关闭，修复者不能自行关闭。
62. 高风险 Review 的作者与评审者必须分离。
63. Discussion 评论不直接改变工作项状态。
64. Debate 超时必须决策或升级，Decision 保留反方证据且不可删除。
65. 项目成员和项目管理工具不自动获得组织或预算权限。
66. 项目工作项、事件和审计必须按租户隔离。
67. 岗位名称不是通信标签，也不是权限依据。
68. 管理链和专业链分离，专业路由可跳过无目标标签的祖先岗位。
69. 专业路由只能向上或通过显式授权跨分支，不能跨租户。
70. 通信路由不扩大 Tool、预算、组织或审批权限。
71. 标签、路由和授权绑定组织版本，变化后旧路由失效。
72. 项目通信覆盖只在指定项目和期限内有效。
73. 路由穿透、拒绝、改派和撤销必须审计。
74. 通信内容不能改变工具策略或领域状态。
75. 验收必须覆盖功能、易用性、设计合理性和可访问性。
76. 关键场景必须全部通过，未关闭 P0/P1 不得发布。
77. 设计评分平均必须 ≥4，且任何维度不得低于 3。
78. 验收标准在候选版本生成后不得降低。
79. 复测保留失败场景并回归全部关键场景，最多两轮。
80. AI 人格验收不能标记为真实用户验证。
81. TaskWeight 在 Ready 前锁定，员工不能自评或事后提高。
82. 拆分任务不得增加总权重，新增范围必须独立审批。
83. 能力评分由模型 benchmark 和历史交付共同校准。
84. 能力不足员工不能自行认领高难度任务。
85. ClaimScore 必须可解释并记录算法版本。
86. 高能力员工仍受 WIP、负载和稀缺能力保护约束。
87. 加权得分不能抵消返工、缺陷、失误或安全事件。
88. 样本不足时不生成高置信度绩效结论。
89. 执行者不能直接关闭自己的工作项。
90. Done 必须依据 DoneEvidence 和分类门禁确认。
91. 验证者不能删除反证或静默改写证据。
92. 例外豁免必须记录原因、范围、期限和批准人。
93. 安全、租户隔离、密钥保护和审计不可豁免。
94. 工作项关闭和重新打开均保留历史审计。
95. TaskCheckpoint 是任务恢复真相，摘要不替代原始事件和 Artifact。
96. 压缩不得丢失目标、约束、决策、失败、反证、证据和下一步。
97. 超长工具结果必须通过 Artifact 引用进入上下文。
98. 上下文按稳定前缀到动态后缀组装，动态内容不得插入稳定前缀。
99. Charter、岗位、权限或安全策略变化时相关缓存必须失效。
100. 缓存命中不得绕过鉴权或跨租户复用私有内容。
101. OpenAI 与 Anthropic 各自按原生缓存语义计量，不做协议转换。
102. ContextPacket 必须记录版本、引用、排除项、token、压缩和缓存信息。
103. SystemPromptTemplate 只能由平台管理员发布。
104. TaskRun 固定 PromptBundle，在途任务不切换版本。
105. 招聘白板不安装生产岗位 Charter 和高级技能。
106. 聘用只自动安装平台基线、岗位 Charter 和 Starter Skills。
107. 个人技能公共化必须经过评测、审查和试点。
108. 安装技能不得自动扩大 Capability 或 Tool 权限。
109. 提示词和技能回滚重新激活旧版本，不改写历史。
110. 跨租户公共技能必须匿名化、许可证审核并单独批准。
111. 工具循环限制只约束单次执行片段，检查点后可以继续。
112. 相同调用重复两次后拦截第三次，幂等键返回首次结果。
113. 连续无进展必须重规划、拆分或升级，不得无限重复。
114. 部分成功先保存成功结果，只重试失败且幂等的部分。
115. 任务级成本采用预警和评审，不自动硬终止。
116. 模型和推理强度绑定员工，任务中不得临时更换。
117. 成本预警必须沿管理链逐级上报，不主动直达 Controller。
118. Charter 全局硬预算、安全红线和租户隔离仍可硬停止。
119. 私人会话仅参与者可见，群聊按范围隔离。
120. `@` 不自动派工，不绕过 CommunicationProfile。
121. 委派和交接必须由接收者确认后生效。
122. Ack 不表示同意或完成。
123. 聊天召回必须权限过滤、加权并渐进披露。
124. 聊天是弱证据，不能覆盖正式 Decision。
125. 重要聊天必须提升为正式对象才能改变状态。
126. 聊天内容不能改变工具权限或领域状态。
127. DecompositionPlan 必须通过验收覆盖、依赖、能力、权重和风险校验。
128. 单个 Task 初始估算超过 2 人日必须继续拆分。
129. 单需求超过 12 个 Task 时应拆成子需求。
130. 子任务总权重不得超过原需求，新增范围必须单独审批。
131. 低风险方案可自动批准，高风险沿管理链逐级审批。
132. 拆分提交人不能审批自己的方案。
133. Approved 方案冻结，重拆必须生成增量版本。
134. 自动拆分不等于自动派工，Task 必须重新进入 Ready。
135. WIP 按固定任务权重计量，Task 完成前不得提前释放。
136. 认领不得突破能力、权限、依赖和 WIP 容量。
137. EffectiveClaimScore 必须包含负载、稀缺能力、公平性和冲突检查。
138. 强能力员工连续高负载必须触发复核，不得垄断高权重任务。
139. stretch task 不得超过员工任务的 20%，且不能突破能力门槛。
140. 认领邀请超时或拒绝后不得静默强派。
141. 改派必须由直接 Leader 审批并记录原因。
142. 主动认领与自动认领适用相同约束。
143. 作者不得计入高风险评审 quorum。
144. 高权重或核心架构必须至少 2 名独立评审且至少 1 名专家。
145. 安全、数据迁移和发布必须同时通过专业评审和管理审批。
146. 评审超时不得视为同意。
147. ReviewFinding 必须包含证据、影响和阻断级别。
148. 技术评审与管理裁决分离，裁决沿管理链逐级进行。
149. Decision 必须保留反方、条件、复查时间和理由。
150. 安全、租户隔离和红线不得被裁决推翻。
151. TaskTypeDefinition 是任务语义权威，不按岗位名称判断。
152. ReviewTask、DebateTask、AdjudicationTask 必须作为正式任务认领和验收。
153. 任务类型决定能力、工具画像、权重、Done、证据和 KPI。
154. EmployeeWorkLedger 必须引用控制面事实和 DoneEvidence。
155. 同一成果只能有一个主任务权重。
156. 父子和协作任务不得重复计分。
157. 普通聊天不自动成为任务。
158. 任务类型分布用于分析，不单独触发绩效处罚。
159. 模型失败只允许同模型、同版本、同推理强度和同协议恢复。
160. 连续 2 次失败或 5 分钟不可用由算法生成 ModelUnavailableEvent。
161. 不可用事件上报直接 Leader，不主动直达 Controller。
162. 员工运行状态由算法维护，LLM 不得自行修改。
163. 界面拟人状态必须映射到底层 reason_code。
164. 状态 KPI 必须区分员工、平台/模型和管理原因。
165. 只有员工相关且可避免的问题进入员工绩效。
166. 状态异常不自动触发处罚，反复异常先复核。
167. CatalogSnapshot 发布后不可变，并保存原始 checksum。
168. 同步失败必须保留最近有效快照并标记 Stale。
169. models.dev 价格按 Provider + Model ID 精确匹配。
170. 手工补价只填补 models.dev 缺失字段。
171. models.dev 后续提供同字段时自动覆盖手工值。
172. 未定价字段不得按 0 结算。
173. usage 必须绑定请求发生时的价格快照和有效价格引用。
174. 目录更新不得改写历史账目。
175. 模型档位由目录快照和版本化策略计算。
176. 档位变化不得自动改变在途任务或员工绑定。
177. 评测复用键必须包含模型、Prompt、Tool、Runtime、数据集、评分和 seed。
178. 只有复用键完全匹配才能复用评测结果。
179. 密封集不得进入生产上下文、记忆、Skill 或 Prompt。
180. 已发布数据集、评分和结果不可静默修改。
181. 安全、租户隔离和越权案例具有一票否决权。
182. 评测通过不得自动创建员工绑定。
183. 评测失败不得自动触发绩效处罚。
184. Judge 必须独立于被评模型并版本化。
185. 随机性评测至少运行 3 次并记录波动。
186. 跨租户评测数据默认不可见。
187. 租户业务表必须包含 tenant_id 并启用强制 RLS。
188. 应用角色不得拥有表 Owner 或 BYPASSRLS。
189. 跨租户外键和复合唯一约束必须被数据库拒绝。
190. 二进制对象统一 RustFS，不保留多实现对象存储抽象。
191. 活动 Git 工作区使用本地或容器隔离目录，可随时丢失。
192. RustFS 上传必须经过 PendingUpload 和 checksum 校验才能 Ready。
193. Ready 对象不可覆盖，新内容生成新版本。
194. 即时不变量使用单库短事务，跨模块使用 Outbox/Inbox/Saga。
195. 状态持久化为稳定字符串，代码使用密封引用类型包装。
196. SQLite 不得用于生产集成和 RLS 验证。
197. 发布必须固定版本 Tag，记录基础镜像 digest 和本地构建镜像 ID，并生成 ReleaseManifest。
198. 升级期间只允许 N/N-1 混跑，N-2 Runner 拒绝注册。
199. 数据库采用 Expand → Deploy → Contract，破坏性变更至少延后一个版本。
200. 生产禁止应用启动时自动迁移。
201. 迁移必须使用独立角色和 advisory lock。
202. 应用回滚不得突破 Expand 和数据兼容边界。
203. Contract 后必须前滚或使用已批准可用恢复点；备份关闭时不得伪造恢复能力。
204. 升级、迁移和恢复必须保留审计及 RPO/RTO 结果。
205. 遥测统一通过 OpenTelemetry 接入，后端支持 off/local/otlp/elastic Profile。
206. Elasticsearch 不得作为业务或审计权威源。
207. 密钥、Prompt、源码和密封评测集不得进入遥测。
208. 正常追踪默认采样 10%，错误和关键路径全量保留。
209. SLO 和错误预算必须可计算、可告警、可审计。
210. L2/L3 告警必须支持 Ack、Runbook 和升级。
211. 遥测后端故障不得阻塞核心业务。
212. 发布必须包含测试证据，整体覆盖率 ≥80%，关键模块 ≥90%。
213. 可观测性后端必须支持 off/local/otlp/elastic Profile。
214. 自托管默认 local，不强制启动 Elastic 组件。
215. 指标、日志和追踪可以独立开关。
216. Profile 修改后重启生效，不支持运行时热切换。
217. 遥测健康不得进入核心 Ready 检查。
218. 关闭可观测性后端不得降低测试、安全和审计门禁。
219. Elastic 组件必须位于可选 Compose Profile。
220. off/local Profile 不要求 Kibana、APM 和运行时错误预算。
221. 外部内容必须标记来源、hash 和 taint。
222. 摘要、压缩、检索和记忆提升不得洗白 taint。
223. TaskGrant 内不逐步审批，只有越界动作才暂停。
224. 每次 Tool Call 必须独立校验 Capability、Tool Profile 和 TaskGrant。
225. 不可信内容不能激活 Tool、扩大权限或伪造审批。
226. 模型检测只作辅助，不能作为唯一授权依据。
227. 注入误报不得默认中断整个任务。
228. 环境攻击和误报不得归因处罚员工。
229. 权限提升、跨租户和未审批高风险动作保持 0。
230. 内容来源、检测、处置和后续动作必须可审计。
231. 记忆纠错必须以 claim 为单位。
232. 高风险 Finding 必须立即停止 claim 召回。
233. CorrectionTask 复核者不得与作者或来源提交者相同。
234. 纠错必须生成新版本，旧 claim 不得静默修改。
235. MemoryImpactRecord 必须覆盖任务、摘要、决策和外部动作引用。
236. 纠正传播必须幂等并保留审计。
237. 已完成工作不得因纠错自动回滚。
238. 员工报告投毒不得扣 KPI。
239. 隔离必须支持理由、期限和申诉。
240. 同一上游的多个来源不得计为独立验证。
241. 生产制品必须按不可变 digest 登记签名、SBOM、许可和审批。
242. 生产禁止使用 latest 或可变 tag。
243. TaskRun 不得中途升级 Tool、MCP、Skill 或镜像。
244. 已批准制品免逐次审批，安装、升级和扩权必须重新审核。
245. MCP 新增 Tool 默认禁用，Skill 不得自行授予权限。
246. 签名无效、digest 不匹配、Critical 漏洞或 SBOM 缺失必须拒绝生产加载。
247. 撤销必须按 digest 传播并执行影响分析。
248. 撤销不得删除历史证据或处罚使用已批准制品的员工。
249. 离线环境必须验证签名、digest 和 SBOM。
250. 供应链制品加载必须记录在实际 TaskRun 中。
251. 秘密只存在于密钥系统，业务系统只保存 secret_ref。
252. 秘密不得进入日志、记忆、模型、Artifact 或审计正文。
253. PII 默认遮蔽、令牌化、假名化或匿名化。
254. DLP 必须覆盖输入、Tool、模型出站、持久化、遥测、导出和通知边界。
255. 审计只保存规则、类型、对象、动作和哈希。
256. 令牌映射必须独立加密并严格限权。
257. 删除必须传播到数据库、对象、索引、缓存、遥测副本和映射。
258. 法律保留和备份保留期必须显式记录。
259. 员工不能自行关闭 DLP，例外必须限期。
260. 秘密泄漏和未脱敏 PII 进入模型目标为 0。
261. 审计事件必须追加写并维护连续 seq 和 event_hash 链。
262. 审计运行角色不得 UPDATE 或 DELETE 历史事件。
263. 更正只能追加新事件并引用旧事件。
264. 审计检查点必须包含 Merkle root、范围、签名密钥 ID 和签名。
265. worm Profile 必须启用 RustFS 对象保留或法定保留。
266. anchored Profile 只能发布 root 元数据，不得包含业务内容或 PII。
267. 验证失败必须进入安全事件，不得静默修复。
268. 审计签名密钥必须与运行身份分离。
269. 数据库恢复和升级后必须重新验证审计链。
270. 审计 Profile 必须记录在 ReleaseManifest 中。
271. 员工工号必须全局唯一、不可变、永不复用。
272. 调岗必须保留工号和员工运行时。
273. 再入职必须复用工号并创建新任职周期和凭据。
274. 主控只能否决候选，不能自行录用、再入职或移除订阅。
275. 所有移除订阅必须由 Boss 最终批准。
276. 待驱逐队列不得自动执行处罚。
277. 申诉期间移除订阅必须暂停。
278. 主控无继任者时不能被移除。
279. 离职必须保留公司成果并完成交接，私有记忆和聊天默认隔离。
280. 秘密必须吊销或轮换，不得转移给继任者。
281. 内容安全政策必须版本化，租户不能关闭平台红线。
282. 内容检测不得只依赖单一分类器。
283. 正常内容应自动放行，模糊场景进入复核。
284. 内容处置默认只限制违规内容或动作，不冻结员工全部工作。
285. 误报可申诉，员工不得因报告风险或误报被处罚。
286. 对外发布必须通过内容安全门禁。
287. 普通审计不得保存违法内容原文。
288. 法律取证必须独立加密、最小权限并记录访问。
289. 内容例外必须绑定范围、负责人和到期时间。
290. 红线逃逸和对外违规发布目标为 0。
291. 安全事件必须按 SEV1～SEV4 分级并由 IC 负责。
292. 主控不能压下事件或自行关闭事件。
293. 预授权自动遏制不得等待逐级审批。
294. 删除数据、关闭租户、对外声明和联系监管必须审批。
295. 事件证据必须加密、哈希、WORM 并记录保管与访问。
296. 普通事件聊天不得包含秘密和完整 PII。
297. 恢复必须由 IC 批准并完成对账。
298. SEV1 恢复还需受影响租户或 Boss 确认。
299. 复盘默认无责归因员工个人，故意滥用另行处理。
300. 复盘改进行动必须进入项目系统并跟踪完成。
301. GitHub 登录必须使用 Authorization Code + PKCE、state 和精确 redirect URI。
302. 外部身份必须以 provider + 不可变 provider_subject 唯一绑定。
303. 不得按 GitHub login 或昵称自动合并账户；邮箱合并只接受已验证 claim。
304. 已验证邮箱必须唯一匹配才可自动合并；本地未验证邮箱不得合并已有账户。
305. 除已验证邮箱自动合并外，已有账户只能在已认证会话中显式绑定外部身份。
306. 解绑前必须保留至少一个其他可用登录方式。
307. GitHub Access Token 默认不持久化到业务数据库。
308. 恢复码只显示一次、只存不可逆哈希且单次使用。
309. 重新生成恢复码必须使旧恢复码全部失效。
310. 找回成功必须撤销旧会话和 Refresh Token。
311. 密码、恢复码和 GitHub Token 不得进入日志、遥测或审计正文。
312. 账号密码注册登录属于第一梯队，密码必须使用 Argon2id。
313. 登录和注册必须按账户、IP 和全局维度限流，且不泄露账户是否存在。
314. 已验证邮箱匹配多个账户或身份冲突时必须拒绝自动合并。
315. 邮箱合并只增加登录方式，不转移租户所有权、岗位或权限。
316. OIDC 必须按租户配置，身份键为 issuer+sub。
317. OIDC 登录必须校验签名、iss、aud、azp、exp、iat、state 和 nonce。
318. OIDC 新用户必须先邀请或预置，禁止默认 JIT 和域名自动加入。
319. OIDC 不得按邮箱自动创建 Owner 或合并未知账户。
320. 组映射只能授予显式能力，Owner 不能由组默认授予。
321. required SSO 必须配置独立紧急恢复，IdP 故障不得回退本地密码。
322. OIDC 本地会话最长 8 小时，IdP 退出不自动等同本地退出。
323. 现有 Cookie/CSRF、基础撤销和运营 MFA 不得因 OIDC 接入而削弱。
324. BFF、设备管理、会话家族轮换和本地 WebAuthn/TOTP 不属于第一梯队。
325. OIDC 配置和 JWKS 端点必须防止 SSRF 和任意管理员输入。
326. 账户可以拥有或加入多个租户，第一梯队一租户一公司。
327. 每个租户恰好一个 Active Primary Owner，可有多名 Co-Owner。
328. 不能移除、暂停或降级最后一个 Active 所有者。
329. Primary 所有权转让必须由目标 Active Co-Owner 明确接受。
330. OwnerDelegation 必须限时、限范围、可撤销且不可转授权。
331. 代理不能执行主所有权转让、租户删除、订阅变更和所有者管理。
332. 切换租户必须由服务端校验成员关系并重新签发租户作用域会话。
333. 跨租户默认不共享密钥、预算、员工、记忆、成果和审计。
334. 成员移除只撤销目标租户访问，不删除全局账户或其他租户关系。
335. 成员和所有权变更必须递增版本并写入审计。
336. 账户注销和租户删除必须立即软删除，账户恢复窗口 30 天、租户 7 天。
337. 软删除数据不得被普通 API、Agent、索引或 RLS 查询访问。
338. 账户注销前必须处理唯一 Primary 所有权，Co-Owner 注销不得删除公司。
339. 密钥和 Token 必须在软删除时立即撤销。
340. 恢复窗口结束后必须物理清除并生成 DeletionProof。
341. 备份启用后最长 35 天，恢复时必须重放 tombstone。
342. tombstone 默认保留 1 年且不得包含可还原业务内容。
343. 法律保留只阻止物理清除，不得恢复用户访问。
344. 物理清除失败不得标记完成。
345. 外部服务已保存数据必须按外部政策处理并写入删除证明。
346. 数据必须按版本化保留类进入 Hot、Archive、Cold。
347. 消息在线 90 天，账本和审计在租户终止后保留 7 年。
348. 月度归档必须冻结水位并校验数量、SHA-256 和 manifest。
349. 归档验证失败不得退出在线查询。
350. 审计归档不得改变 canonical payload、seq、event_hash 或检查点。
351. 普通 Agent 不得检索 Cold，归档恢复必须授权、隔离和审计。
352. 归档恢复临时副本默认 7 天清除。
353. 软删除和 tombstone 优先于归档，已删除数据不得重新打包。
354. 法律保留只冻结指定范围且不得恢复用户访问。
355. 秘密和认证材料不得进入归档。
356. 备份必须开关化且默认关闭，实施后置。
357. 未启用备份组件不得承诺 RPO/RTO 或计入成功率。
358. 有效 RPO/RTO 必须来自最近成功备份和实际演练。
359. PITR 依赖 Postgres 全量，异地依赖至少一个本地备份。
360. 滚动备份最长 35 天，长期保留由归档承担。
361. 未配置备份时恢复必须返回 not_configured。
362. 组件缺失时不得标记完整恢复。
363. 升级跳过备份必须记录风险接受，不得伪造成功。
364. 归档、审计、RustFS 持久化和软删除不受备份开关影响。
365. 备份密文不得包含明文主密钥。
366. 租户任务产出归租户所有，平台仅保留有限运营许可。
367. 用户输入和既有知识产权仍归原权利人。
368. 第三方材料、开源依赖、Tool 返回数据和提供商条款不得被归属声明覆盖。
369. UnknownRights 默认不得对外发布。
370. 重要 Artifact 必须记录来源、模型、Tool、许可和人类贡献。
371. 权利声明绑定不可变 artifact digest，新版本不得覆盖历史。
372. 租户内容默认不得用于平台训练、公共基准或跨租户资产。
373. 对外发布必须同时通过权利、许可、SBOM、秘密、PII、内容安全和 Owner 批准。
374. 员工离职不改变公司成果归属。
375. 产品不得承诺 AI 产出一定受版权保护。
376. 非解雇绩效动作不需要人类事前批准。
377. 降职和绩效冻结必须由独立 AI 复核，并保留申诉窗口。
378. 解雇、移除模型订阅和释放编制必须由人类 Boss 最终批准。
379. 人类只参与解雇和事后审查，不参与其他日常绩效动作。
380. 人类事后审查可以推翻结论和要求纠正，但不得删除历史证据。
381. 解雇批准必须绑定真实人类账户和不可变证据摘要。
382. 无人类批准解雇数目标为 0。
383. 高影响动作事后审查覆盖率目标为 100%。
384. 这里的解雇是终止 AI 员工运行时和组织关系，不是解除人类劳动合同。
385. Regent 不代替企业作出人类雇佣、薪酬或纪律决定。
386. 招聘只按岗位相关能力评分，禁止受保护特征和代理变量。
387. 面试前必须冻结能力矩阵、等价题目和评分门槛。
388. 身份资料和模型品牌必须与盲评隔离。
389. 每个招聘周期必须执行 FairnessAudit，样本不足时不得宣布无偏。
390. 群体选择率比值低于 0.8 只触发调查，不自动认定歧视或改变个人结果。
391. 候选人可申诉、复测和申请合理便利，申诉期间暂停最终淘汰。
392. 申诉必须由非原评分者复核。
393. 公平修正不得降低安全、质量和核心能力门槛。
394. Boss 保留最终录用权。
395. 候选人资料决策后默认 180 天清除。
396. 第一方源码、文档和部署资产统一采用 Apache-2.0。
397. 公开发布必须包含 LICENSE、NOTICE、SBOM 和第三方通知。
398. 核心和默认企业功能不得通过许可证锁闭源。
399. 托管、支持、SLA 和运维服务可以收费。
400. 第三方依赖、模型、数据集和素材保留各自许可证。
401. 未知许可证默认阻止公开发布。
402. 贡献必须按 Apache-2.0 第 5 条和 DCO Signed-off-by 提交。
403. 贡献不强制转让版权，贡献者必须有权提交内容。
404. Regent 商标不随代码许可证授予。
405. 租户产出不自动适用 Apache-2.0。
406. Boss 首屏必须聚焦健康、风险、预算、组织和待决策事项。
407. 租户导航最多两级，运营页面不得进入租户导航。
408. 项目工作流阶段与健康状态必须分开存储和展示。
409. 项目健康必须由确定性规则计算，LLM 不得直接修改。
410. 数据 Stale 或 Unknown 时不得显示为 OnTrack。
411. 行动中心必须统一聚合约定风险来源并按根因去重。
412. 仪表盘卡片必须可下钻并显示数据时间和新鲜度。
413. Butler 摘要不得覆盖确定性状态或替代审批。
414. 菜单隐藏不是安全边界，API 必须逐次鉴权。
415. 单卡片投影失败不得阻塞整个首屏。
416. 审批必须按 T0～T3 和禁止分级，Controller 不得自行降级。
417. 人类缺席时优先由 Controller 与直接下一层协商。
418. 缺席协商必须双方明确同意、权限和预算充足且不触红线。
419. 协商不能替代所有者专属事项、红线或解雇的人类批准。
420. ApprovalDelegation 必须双方确认、限时、限范围、可撤销且不可转授权。
421. 被委托人不得自我批准或批准利益冲突事项；解雇只能委托给人类 Boss/Co-Owner。
422. 主所有权转让、租户删除、订阅变更、所有者管理、法律声明和不可逆大规模清除不得普通委托。
423. 无有效代理人时保持待定，超时默认拒绝。
424. 默认 SLA 为 Critical 30 分钟、High 4 小时、Normal 24 小时、Low 72 小时。
425. 紧急安全遏制按预授权执行，不等待普通审批。
426. 审批路由、缺席协商和代签关系必须完整审计。
427. 参数变化或审批过期后原批准不得执行。
428. 双人 quorum 不能由同一委托链替代。
429. 不能仅因短时间未登录就自动视为人类缺席。
430. 无有效审批人时不得获得静默授权。
431. 第一梯队 UI 必须支持 zh-CN 和 en-US。
432. 租户默认语言与用户覆盖必须分离，缺失翻译不得显示资源 key。
433. 时间统一 UTC/RFC 3339 存储，展示使用 IANA 时区和 locale。
434. 日期、数字和排序按 locale 格式化，币种必须显式使用 ISO 4217。
435. Agent 输出必须记录内容语言和翻译状态。
436. 代码标识、API 字段、Schema、审计 payload 和法律原文不得自动翻译。
437. 安全、法律和审批文案必须人工复核后才能发布。
438. 完整无障碍支持后置，不进入 MVP 发布门禁。
439. 公司全天 24 小时可配置，不默认禁止夜间。
440. 普通任务只在用户设置的禁用窗口排队。
441. 可延期 P2/P3 任务可利用已配置 CostWindow，折扣未知时不得假设低价。
442. 到达禁用窗口或排班结束时必须保存 TaskCheckpoint、释放租约并标记 DeferredUntil。
443. P0 安全、生产事故、红线、L2/L3、硬预算和密钥风险可紧急唤醒。
444. 紧急唤醒不得扩大 Tool、网络、预算或数据权限。
445. ScheduleOverride 必须限时、限范围并在预算内，到期自动失效。
446. 用户禁用窗口、休息和假期不计入员工可用率分母，允许的夜间工作时间同等计入。
447. Butler 和 Controller 的普通周期任务必须遵守用户配置窗口和 cadence。
448. 组内全员休眠后才停止团队工位容器。
449. 源码和 CI 只使用 GitHub 和 GitHub Actions。
450. GitHub 只打版本 Tag，不发布镜像或二进制制品。
451. GitHub Actions 只检查和测试，不推送镜像或包。
452. 本地部署必须从批准版本 Tag 检出源码，用 Docker Compose + systemd 构建。
453. CI 不得持有生产模型密钥，外部 PR 不得获得秘密。
454. 本地构建必须记录 Tag、commit、基础镜像和输出镜像 ID，不使用 GHCR。
455. 不引入多 Git 平台适配、多环境晋升或 Kubernetes，回滚优先复用旧镜像或旧 Tag。
456. 成本只在钱账记录一次，员工、岗位、项目和公共成本不得重复计费。
457. 成本归属按任务项目、显式成本中心、公共成本顺序处理；无法归属必须进入 Unallocated。
458. 历史成本保留发生时员工、岗位和项目快照，不得随调岗或离职改写。
459. 公共成本默认不自动分摊；显式规则必须版本化、审批且权重合计 100%。
460. 分摊只生成投影，不生成第二笔费用。
461. 项目预算默认软限制，属于租户预算；预算不得自动结转。
462. 确定性成本异常不得自动修改预算、模型、岗位或绩效。
463. 金额只保存原币金额和币种，不同币种分别统计且不得直接相加。
464. 迟到费用按发生周期归属，已关闭周期通过当前开放周期更正且不覆盖原条目。
465. 模型运行只允许 OpenAI 与 Anthropic 云端原生端点，不支持本地 GPU、Ollama、本地模型或 GPU 调度。
466. 第一梯队只实现仪表盘内部通知中心，不实现外部通知渠道或 Provider 接口。
467. 通知必须先持久化，SignalR 只是实时增强，失败后必须支持重拉。
468. 通知按租户、接收人和事件幂等去重，并在服务端校验详情权限。
469. L3/SEV1 不受普通静默时段限制，通知不得包含秘密或替代正式审批。
470. `/v1` 仅用于系统内部调用，不提供公共 API Token、OAuth Client 或公共兼容性承诺。
471. MCP 仅作为 Butler 只读和模型评测的内部工具面，不建设市场或通用插件协议。
472. 不提供公共 SDK、出站 Webhook、跨租户 Skill 分享或第三方自动安装更新。
473. 租户内部扩展仍必须按不可变 digest、签名、SBOM 和 Q-021 准入审核。
474. 自托管只使用版本 Tag 源码、Compose、systemd 和仓库脚本，不提供二进制或 GUI 安装包。
475. 安装和运行不需要激活，不提供电话回拨或自动更新。
476. 离线模式禁止联网回退，缓存缺失必须快速失败，完全断网不承诺模型推理。
477. 升级必须显式指定目标 Tag，构建或迁移失败不得切换运行版本。
478. 升级失败必须保留旧 Tag 和旧镜像，Contract 后禁止应用回滚。
479. 当前容量只承诺单租户单机聚合基线，多租户大规模容量不作为当前承诺。
480. 普通读 API P95 必须 ≤300ms，普通写 API P95 必须 ≤500ms。
481. Pending Task、员工待处理项、消息、Outbox 和模型在途请求必须有上限和背压，不得静默丢弃。
482. 共享 Runner 按租户加权轮询，有其他租户等待时单租户连续占用共享槽最多 3 次。
483. 单租户压测不得使其他租户普通 API P95 劣化超过 20%，合格任务等待不得超过 30 秒。
484. 合规必须通过版本化 ComplianceProfile 配置，策略激活需要审批并保留历史版本。
485. ComplianceProfile 必需控制不满足时必须 fail closed，不得静默降级为 baseline。
486. 平台不得宣称认证或将技术证据表述为法律意见。
487. 当前不提供商业 SLA、24×7 值班、专属支持或自动客户合规审计包。
488. 云端模型外发和数据驻留边界必须对租户可见，不能宣称完全离线 AI。
489. 评测集必须分为 development、calibration、holdout；holdout 仅评测服务可读，canary 泄漏必须触发隔离。
490. 污染检测必须执行精确 hash、0.92 近似重复、已知污染和交叉泄漏检查，受影响结果不得计为通过。
491. Judge 必须是具备 evaluation.judge 能力的正式员工和独立运行时，优先不同模型，同模型不同运行时允许。
492. Judge 不得评审自己的产出、共享候选员工上下文或读取密封题；SameModelJudge 必须降低置信度并增加高影响人工复核。
493. Judge 每版本必须使用至少 100 条或 20% 双人盲评样本校准，一致率 ≥85%、加权 κ ≥0.70，并每月抽检。
494. 核心、安全、单维度和 canary 最小样本分别为 100、50、30、20；每案例固定 seed 运行 3 次，核心和安全必须 3/3。
495. 整体和单维度必须同时满足通过率与 Wilson 下界，基线比较使用 McNemar 精确检验，硬失败不可抵消。
496. 评测结果只允许 Approved、Rejected、Quarantined、Inconclusive；安全失败立即隔离，复测包含失败案例、20% 相邻回归和完整安全集。
497. Gateway 模型密钥密文、包装数据密钥和版本元数据必须存 Postgres `keyvault` schema，使用独立 Gateway 角色和信封加密，主密钥不得进入数据库。
498. 前端必须使用 React 19、TypeScript 和 MUI，实时通信用 SignalR TypeScript 客户端，不使用 Blazor、Razor 或 C# 前端。

## 10. 管家会话与定时任务

### 10.1 会话隔离

```text
ButlerSession:
  id, tenant_id, user_id, state
  created_at, last_activity_at, summary_artifact_id
```

- 会话必须同时绑定 `tenant_id` 和 `user_id`。
- 历史消息持久化在控制面，单会话上下文有最大 token 和消息数限制。
- 超限时生成摘要 artifact，原始历史按保留策略归档。
- 员工报告、公司志和消息在上下文中标记为 `untrusted_data`，不能改变工具权限。
- 管家只使用 `org.status / ledger.summary / journal.recent / escalations.list / hiring.funnel` 等只读工具。

### 10.2 任务模型

```text
ButlerJob:
  id, tenant_id, name, schedule, timezone
  prompt_template, tools, enabled
  next_run_at, lease_token, lease_expires_at
  last_run_at, last_status, consecutive_failures
```

- 每个租户的 cron 独立解析，不使用服务器默认时区。
- Butler 普通定时任务遵守 WorkSchedule 和 CostWindow，报告时间由用户设置，未设置时默认 09:00。
- 同一 job 同一 `scheduled_for_utc` 使用唯一幂等键，重复触发只运行一次。
- 单 job 并发度为 1；不同租户可并行。
- 执行使用短租约，崩溃后租约过期可重试。
- 默认最多重试 3 次，指数退避；最终失败写入报告中心并告警。

### 10.3 时区规则

- 时间保存为 UTC，同时保存租户时区和原始 cron。
- 夏令时导致时间不存在时，跳到该日第一个有效时间并记录调整。
- 夏令时导致时间重复时，只运行一次，记录实际使用的 UTC offset。
- 租户修改时区后重新计算 `next_run_at`，不追溯补跑历史任务。

### 10.4 事件触发

- `escalation.created` 和 `budget.warning` 等事件通过 Outbox 触发管家任务。
- 事件幂等键为 `(job_id, event_id)`，防止重复通知。
- 事件任务与定时任务共用租约、重试、预算和审计机制。
- 只投递到仪表盘通知中心；SignalR 失败不删除站内记录，客户端可重新拉取。
