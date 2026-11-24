# 07 运营平台（Operations Platform）

- 版本：v0.1（待评审）
- 范围：提供方管理端、租户与控制面边界、运营数据、支持访问和运维工作流
- 对应缺口：G-24

## 1. 定位

Regent-Agent 分为四个逻辑部分：

1. **ControlPlane（控制面）**：租户公司运行与租户仪表盘。
2. **AI Gateway（AI 网关）**：模型密钥、原生协议代理、路由和计量。
3. **Runner**：无状态员工与主控计算宿主、沙箱执行。
4. **Operations Platform（运营平台）**：提供方管理端，用于租户生命周期、资源池、计费、监控、审计和恢复。

运营平台不是租户控制面的另一个页面，也不直接读写租户业务库。它是提供方的独立管理面，具有独立身份、权限、界面和审计。

## 2. 使用对象与界面

| 对象 | 使用者 | 界面 | 可见范围 |
|---|---|---|---|
| 租户控制面 | 股东、租户用户 | Tenant Dashboard | 当前租户公司、模型元数据、预算、升级事项 |
| 运营平台 | 提供方运营、运维、安全、财务 | Ops Console | 系统级租户账户、套餐、资源池、健康、事件和审计 |
| 公开状态页 | 租户与公众 | Status Page | 脱敏后的服务可用性和事件 |
| 本地管理面 | 自托管管理员 | Local Admin | 本机四部分配置、备份和升级 |

租户界面与运营界面使用不同路由、认证 audience 和授权策略。托管模式下建议使用不同 origin，例如 `app.example.com` 与 `ops.example.com`。

运营页面不得进入 Tenant Dashboard 导航；租户首屏与行动中心只聚合租户授权范围内的公司风险，详见 [35 租户仪表盘信息架构](./35-tenant-dashboard-information-architecture.md)。

## 3. 职责边界

### 3.1 运营平台负责

- 租户注册账户、状态、套餐、配额、订阅和停用。
- Runner 池、Gateway、部署版本、镜像、供应链制品和容量管理。
- 全局健康、SLO、错误预算、Kibana/Elastic 可观测性、DLP、事件、值班和状态页。
- 备份策略编排、恢复审批、审计完整性和演练记录。
- Provider 价目目录、全局匿名 benchmark 和供应限制。
- 支持访问申请、临时授权和双人紧急访问。
- 运营审计、安全调查、内容安全和滥用处置。
- 租户终止；Owner 自助数据导出已提供，跨环境迁移协助与托管退出仍后置。

### 3.2 运营平台不负责

- 不代替主控管理公司岗位、任务和项目。
- 不直接修改租户消息、账本或审计。
- 不默认读取租户对话、源码、报告和记忆。
- 不读取或展示模型密钥明文。
- 不通过共享数据库绕过控制面 API。
- 不把运营方权限授予租户用户。

## 4. 数据权威源

| 数据 | 权威源 | 运营平台获取方式 |
|---|---|---|
| 租户账户、套餐、配额、订阅 | Operations DB | 本系统权威数据 |
| 公司、章程、组织、任务、项目 | ControlPlane | 内部 Ops API，默认聚合或脱敏 |
| 租户模型元数据 | ControlPlane | 内部 Ops API |
| 模型密钥与密钥版本 | Gateway / Postgres `keyvault` schema | 只获取 key_ref、状态和版本，不获取密文或主密钥 |
| 模型与工具用量 | Gateway / Runner | 聚合事件和账本投影 |
| Runner 容量与健康 | Runner + ControlPlane | 指标和注册信息 |
| 部署版本、镜像、配置策略 | Operations DB | 本系统权威数据 |
| 事件、支持授权、运营审计 | Operations DB | 本系统权威数据 |

任何组件都不直接写另一个组件的权威表。跨组件同步通过 API 和事件完成。

## 5. 同步契约

### 5.1 Operations → ControlPlane

- `tenant.account.status_changed`
- `tenant.quota.updated`
- `tenant.plan.changed`
- `tenant.feature_flags.updated`
- `deployment.policy.updated`

控制面消费后更新租户可用性、编制上限、Runner 优先级和功能开关。同步事件必须幂等，并记录 source event ID。

### 5.2 ControlPlane → Operations

- `tenant.created`
- `company.status_changed`
- `usage.summary.updated`
- `capacity.requested`
- `export.requested`（租户 Owner 创建导出包时产生）
- `incident.detected`

运营平台只接收完成聚合所需的数据，不默认接收完整任务载荷、消息正文或模型提示词。

### 5.3 Operations ↔ Gateway

- Operations → Gateway：Provider 价目、租户状态、速率策略、允许的模型目录。
- Gateway → Operations：健康、容量、聚合用量、密钥引用状态、Provider 错误率。
- 密钥内容不进入运营平台。

### 5.4 Operations ↔ Runner

- Operations → Runner：镜像白名单、标签、drain 指令、资源池策略。
- Runner → Operations：心跳、容量、资源使用和版本。
- Runner 仍通过 ControlPlane 领活，运营平台不直接给 Runner 派租户任务。

## 6. 运营身份与权限

### 6.1 身份

- 运营人员使用独立 Operator 账号，不与租户账号复用。
- Operator access token：`iss=regent-ops`、`aud=regent-ops`、`sub=operator_id`、`scope=system`。
- 访问指定租户时额外携带 `support_grant_id` 和 `tenant_id`。
- 高风险操作要求 MFA 或 step-up authentication。
- 服务间调用使用 `aud=regent-control` 或 `aud=regent-gateway` 的独立 service token。

### 6.2 角色

| 角色 | 典型权限 |
|---|---|
| Ops Viewer | 查看聚合健康、容量、租户状态 |
| Tenant Admin | 管理租户账户、套餐、配额和停用 |
| SRE | 管理 Runner/Gateway、部署、事件、备份恢复 |
| Security Auditor | 只读审计、调查和支持授权 |
| Billing Admin | 管理订阅、发票和价目，不访问业务内容 |
| Break-glass Operator | 限时紧急访问，需双人审批 |

### 6.3 默认拒绝

- 未显式授予的租户数据访问一律拒绝。
- 租户内容默认脱敏，支持访问按字段和工具范围授权。
- 运营角色不能自行扩大 support grant。
- 所有授权、访问和拒绝都写入不可变运营审计。

### 6.4 租户 OIDC 运营边界

- 租户 OIDC 配置由租户管理员在控制面管理。
- 运营人员只能查看脱敏配置状态、版本和登录审计。
- 运营人员不能替换 issuer、client secret 或组映射。
- required 模式启用前必须验证紧急恢复路径。
- IdP 故障不回退本地密码，紧急访问必须限时并审计。
- OIDC 属于第一梯队；BFF 和设备管理后置。

## 7. 支持访问

### 7.1 正常支持流程

1. 运营人员提交申请：租户、原因、范围、时长、只读/写权限。
2. 租户股东批准，或按租户预设策略自动批准只读范围。
3. 系统生成短时 `SupportGrant`。
4. 运营人员通过控制面 Ops API 访问，所有请求带 grant ID。
5. 到期自动撤销，访问记录可被租户查看。

### 7.2 紧急访问

- 仅在服务中断、安全事件或数据恢复场景使用。
- 需要两名授权人员批准。
- 默认 30 分钟，可提前撤销。
- 自动通知租户和审计团队。
- 紧急访问不能读取模型密钥，也不能绕过红线策略。

### 7.3 审计字段

```text
operator_id, tenant_id, support_grant_id
action, resource, request_id
before_hash, after_hash
reason, approved_by, expires_at
```

## 8. 核心运营工作流

### 8.1 租户生命周期

```text
Pending -> Active -> Suspended -> Active
                    \-> SoftDeleted -> Purged
```

- 创建租户和默认配额；账户可以拥有多个租户。
- 激活后允许注册控制面公司。
- 每个租户保留唯一 Primary Owner，运营人员不能代替租户转让所有权。
- 暂停时拒绝新任务和模型调用，保留数据。
- 删除请求立即软删除，账户恢复窗口 30 天、租户 7 天。
- 窗口结束后物理清除并生成 DeletionProof，失败保持可重试。
- 备份启用后最长 35 天，恢复时重放 tombstone，法律保留不恢复访问。
- 订阅和计费按租户独立，Primary Owner 是管理责任人。

### 8.2 配额与公平调度

- 运营平台定义套餐上限和资源池策略。
- 控制面执行租户级编制、任务和预算限制。
- Runner 池按租户公平调度，SRE 可设置 drain 和权重。
- 配额变更通过事件同步，不直接修改控制面数据库。

### 8.3 事件与状态页

- 安全事件按 SEV1～SEV4 分级，由 Incident Commander 分配技术、取证、沟通和记录角色。
- 预授权自动遏制可撤销凭据、冻结 Runner/Tool/制品、隔离容器和暂停任务。
- 删除数据、关闭租户、对外声明和联系监管必须审批。
- 证据加密、哈希、WORM 并记录保管和访问。
- 状态页只展示脱敏影响范围，不展示租户名称、提示词或业务数据。
- L3 事件自动生成状态页草稿和租户通知。
- 恢复需 IC 批准，关闭后生成复盘、指标和改进行动。

### 8.4 备份与恢复

- 备份默认关闭，运营平台通过 BackupProfile 分别控制全量、PITR、RustFS、密钥库、异地和演练。
- 未启用组件不产生备份作业、RPO/RTO 承诺或成功率指标。
- 备份作业记录组件、快照、哈希、加密状态和 35 天保留期。
- 恢复需要审批，并按租户或全量执行；未配置时返回 not_configured。
- 恢复后执行一致性校验和可选演练报告。
- 密钥库密文和包装数据密钥存 Postgres `keyvault` schema，由网关备份，主密钥分离保管。

### 8.5 计费与对账

- 运营平台管理套餐、订阅、发票和付款状态。
- 模型费用仍由 BYOK 租户直接承担，平台只收订阅费。
- Provider 账单差异进入对账队列，不修改原始用量。
- 本地套餐、配额、发票和订阅账本已实现；真实支付渠道、生产价格、税务和退款按 Q-046 后置。

### 8.6 账户身份与找回支持

- 账号密码、GitHub OAuth 和恢复码自助找回由控制面身份服务提供。
- 已验证邮箱自动合并由身份服务执行，运营人员不能手工强制合并。
- 运营人员只能查看授权的登录和找回审计元数据。
- 运营人员不能直接修改密码哈希、恢复码或外部身份绑定。
- 完整人工找回流程暂缓；未来必须双人审批并保留证据。
- 异常找回批量失败或 OAuth 异常进入安全事件流程。

### 8.7 保留与归档

- 运营平台执行版本化 RetentionPolicy、月度 ArchiveJob 和冷存储生命周期。
- 归档必须冻结水位并校验数量、SHA-256 和 manifest。
- 验证失败保留 Hot，不发布半成品清单。
- 冷恢复需授权、隔离、审计并默认 7 天清除。
- 软删除、tombstone 和法律保留优先于普通归档。
- 运营人员不能直接执行任意 SQL 或绕过租户权限读取归档。

### 8.8 合规与支持边界

- 合规通过租户 ComplianceProfile 配置，必需控制不满足时 fail closed。
- 保留技术证据，但不宣称认证、法律意见或商业 SLA。
- 当前仅提供公开 GitHub Issue 和文档支持，专属支持后置。
- 完整策略和证据边界见 [45 企业合规与支持边界](./45-enterprise-compliance-and-support-boundary.md)。

## 9. 部署模式

| 模式 | 运营平台形态 | 网络与信任 |
|---|---|---|
| 托管服务 | 独立 Ops Console，仅运营网络可访问 | 提供方与租户身份、数据和审计分离 |
| 全套自托管 | 本地管理面，可禁用远程运营访问 | 只信任部署者，无外部运营方参与 |
| 开发环境 | 单机 Docker Compose + systemd + 本地 Ops Console | 使用测试身份和假密钥 |

自托管用户不需要把运营平台暴露到公网。托管模式下运营平台也不能绕过控制面直接访问租户数据。

升级由 ReleaseManifest 驱动，运营平台或自托管 CLI 执行 Drain、Backup、Expand、Deploy、Smoke、Resume 和 Contract，并记录 UpgradeRun。数据库迁移使用独立角色，运营平台不直接执行任意 SQL，也不共享运行角色。

## 10. 核心实体

| 实体 | 关键字段 |
|---|---|
| Operator | id、email、status、mfa_enabled |
| OperatorRole | operator_id、role、scope |
| TenantAccount | tenant_id、account_status、plan_id、quota_policy_id |
| Plan | id、limits、features、price |
| Subscription | tenant_id、plan_id、status、billing_period |
| QuotaPolicy | tenant_id、max_employees、runner_weight、rate_limits |
| RunnerPool | id、labels、capacity、scheduling_policy |
| Deployment | id、release_version、component_versions、built_image_ids、status |
| ReleaseManifest | release_version、schema_version、api_versions、compatible_versions、backup_required |
| UpgradeRun | id、from_version、to_version、phase、backup_refs、migration_status、rollback_decision |
| Incident | id、severity、status、impact、timeline |
| StatusPageEvent | incident_id、public_message、published_at |
| SupportGrant | tenant_id、operator_id、scope、expires_at、approvals |
| ComplianceProfile | tenant_id、preset、jurisdictions、allowed_regions、required_controls、version、status |
| ComplianceViolation | profile_id、control_id、subject_ref、impact、status、appeal_ref |
| BackupProfile | id、deployment_id、enabled、component_flags、schedule、retention_days、version |
| BackupJob | id、components、snapshot_refs、status、hash |
| RestoreJob | id、backup_ids、scope、approval_ref、status、rpo_actual、rto_actual |
| OpsAuditEvent | operator_id、action、target、request_id、created_at |
| ObservabilityBackend | id、profile、endpoint_ref、version、status、signals、retention_policy |
| TenantMembership | tenant_id、account_id、status、membership_version |
| TenantOwnership | tenant_id、membership_id、kind、granted_at、revoked_at |
| OwnerDelegation | tenant_id、grantor_id、delegate_id、capabilities、expires_at |
| SloDefinition | id、service、sli、target、window、error_budget_policy |
| AlertEvent | id、level、service、slo_ref、status、ack_by、runbook_ref、created_at |
| SupplyChainArtifact | id、type、publisher、version、digest、signature_ref、sbom_ref、status |
| AdmissionReview | id、artifact_id、requested_capabilities、scan_refs、reviewer_id、decision |
| ArtifactRevocation | id、artifact_id、digest、reason、effective_at、propagation_status |
| SensitiveDataPolicy | id、tenant_id、version、data_classes、pii_rules、secret_rules、egress_policy |
| DlpFinding | id、tenant_id、source_ref、rule_id、data_class、risk_level、action、status |
| DeletionProof | id、subject_ref、scope、completed_steps、retained_items、created_at |
| DeletionRequest | id、subject_type、subject_id、status、deleted_at、restore_due_at、purged_at |
| DeletionTombstone | id、subject_type、subject_id、scope、deleted_at、purged_at、result_hash |
| RetentionPolicy | id、tenant_id、data_type、retention_class、online_days、archive_days、version |
| ArchiveManifest | id、tenant_id、data_type、watermark、record_count、status、verification_result |
| ArchiveRestoreJob | id、archive_id、requested_by、status、restore_prefix、expires_at |
| LegalHold | id、tenant_id、scope、reason、authority、starts_at、expires_at |
| AuditCheckpoint | id、stream_id、from_seq、to_seq、merkle_root、signing_key_id、signature、object_ref |
| AuditVerification | id、stream_id、result、checked_range、evidence_ref、created_at |
| AuditAnchor | id、checkpoint_id、anchor_type、external_ref、status、confirmed_at |
| ContentSafetyPolicy | id、tenant_id、version、jurisdiction、categories、detector_refs、status |
| ContentFinding | id、content_ref、content_hash、category、risk_level、disposition、status |
| ContentReview | id、finding_id、reviewer_id、decision、reason、evidence_refs |
| TakedownRequest | id、content_ref、reason、requester_ref、status、completed_at |
| SecurityIncident | id、tenant_id、severity、status、incident_commander_id、impact、timeline |
| ContainmentAction | id、incident_id、action_type、target_ref、approval_ref、result |
| EvidenceItem | id、incident_id、type、source_ref、content_hash、storage_ref、legal_hold |
| IncidentNotification | id、incident_id、audience、channel、status、sent_at |
| Postmortem | id、incident_id、root_cause、actions、owner_ids、status |

## 11. 管理 API

| 域 | 端点示例 |
|---|---|
| 租户账户 | `/ops/tenants`、`/ops/tenants/{id}/suspend`、`/ops/tenants/{id}/quota` |
| 套餐计费 | `/ops/plans`、`/ops/subscriptions`、`/ops/invoices` |
| 资源池 | `/ops/runner-pools`、`/ops/gateways`、`/ops/deployments` |
| 事件状态页 | `/ops/incidents`、`/ops/status-page` |
| 支持访问 | `/ops/support-grants`、`/ops/support-grants/{id}/revoke` |
| 备份恢复 | `/ops/backups`、`/ops/restores` |
| 审计 | `/ops/audit-events` |

所有 Ops API 使用独立 audience、幂等键和运营审计。租户 API 不接受 Operator token，Ops API 也不接受普通租户 token。

## 12. 验收场景

1. 租户账号无法访问任何 Ops API。
2. 无 support grant 的运营人员无法读取租户业务内容。
3. 只读 grant 不能修改岗位、任务、预算或升级事项。
4. grant 到期后访问立即拒绝，租户可查看访问记录。
5. 运营平台不能读取模型密钥明文或 key 字节。
6. 配额变更通过事件同步到控制面，不产生双写不一致。
7. SRE 可以 drain Runner，但不能直接给 Runner 派租户任务。
8. 暂停租户后，新任务和新模型调用被拒绝，已有数据保留。
9. 状态页不展示租户名称、提示词、源码或消息正文。
10. 备份恢复需要审批，并生成完整校验报告。
11. 自托管模式可以不启动或暴露运营平台。
12. 所有高风险运营操作都有操作者、原因、审批和审计关联。
