# 03 模型网关、计量与预算

- 版本：v0.2（本地金额预留/结算/对账已验收；真实 Provider 账单未连接）
- 范围：BYOK、模型路由、用量、账本、预算和成本异常
- 对应缺口：G-02、G-04、G-08、G-09、G-16

## 1. 核心决策

1. 控制面保存模型元数据和 `key_ref`；网关使用 Postgres `keyvault` schema 保存信封加密后的密钥密文，主密钥不进入数据库。
2. 模型成本以网关 `llm_usage` 事件为权威源；Runner 只上报工具成本。
3. 每次模型调用前必须获得预算预留；调用后按实际用量结算。
4. 账本只增不改，价格更正和供应商账单差异通过新条目表达。
5. 所有路由和重试都限制在同一租户、同一员工绑定模型和授权端点内。
6. OpenAI 与 Anthropic 使用各自原生协议，不做请求、响应、工具调用或错误结构的跨协议转化。
7. 控制面调用网关必须使用服务身份，不能借用员工令牌。
8. 模型元数据和价格以 models.dev 每日快照为首选来源，BYOK 手工补价可被目录字段自动覆盖。
9. 价格和档位均版本化；历史 usage 按请求发生时快照结算，不追溯改写。
10. 成本只在钱账记录一次，员工、岗位、项目和公共成本不重复计费。
11. 归属按任务项目、显式成本中心、公共成本顺序处理；无法归属进入 Unallocated。
12. 公共成本默认不自动分摊，项目预算不自动结转，异常不自动改预算、换模型或惩罚员工。
13. 模型运行只支持 OpenAI 与 Anthropic 云端端点，不支持本地 GPU、Ollama 或本地模型。

## 2. 模型与 BYOK 生命周期

### 2.1 模型元数据

```text
Model:
  id, tenant_id, provider, provider_model_id
  protocol, endpoint_kind
  tier, capability_tags, context_window
  input_price_per_mtok, output_price_per_mtok
  currency, price_version, effective_at
  key_ref, state, health, last_test_at
```

`protocol` 取值：`openai_chat_completions`、`anthropic_messages`。`endpoint_kind` 由协议推导，客户端必须选择对应原生端点。

`key_ref` 是网关内的不透明引用，不包含密钥、密钥哈希或可逆标识。Gateway 通过独立数据库角色访问 Postgres `keyvault` schema，ControlPlane 角色无权读取该 schema。

```text
ModelKeyRecord:
  key_ref, tenant_id, provider, model_id
  key_version, encrypted_key, wrapped_data_key
  encryption_algorithm, state
  created_at, activated_at, retired_at
```

### 2.2 新增模型 saga

1. 控制面创建 `Model(state=Provisioning)`，写入幂等记录。
2. 仪表盘服务端调用网关管理 API，提交租户 ID、模型 ID、provider 和密钥。
3. 网关加密存储密钥，返回 `key_ref`；控制面更新模型元数据。
4. 网关执行最小测试调用，生成测试用量和审计事件。
5. 测试成功且预算允许后，控制面将模型置为 `Ready`。
6. 任一步失败进入 `Failed` 或 `Deleting`，由补偿任务清理未完成的密钥引用。

该流程不追求跨进程分布式事务，使用幂等步骤、状态机和补偿保证最终一致。

### 2.3 轮换

1. 创建新密钥版本，保持当前版本可用。
2. 新版本测试成功。
3. 原子切换 `active_key_version`。
4. 旧版本保留 24 小时供在途请求结束。
5. 删除旧版本并记录审计。

### 2.4 删除

1. 控制面将模型置为 `Deleting`，拒绝新调用。
2. 等待在途请求结束或达到最大等待时间。
3. 解除员工、岗位和默认路由引用。
4. 网关删除密钥密文及版本。
5. 控制面归档模型元数据和历史用量，不删除账本。

### 2.5 对账任务

每小时检查：

- `Provisioning/Rotating/Deleting` 超时项。
- 控制面存在但网关缺少的 `key_ref`。
- 网关存在但控制面无引用的孤立密钥。
- 密钥版本与模型状态不一致。

发现差异时停止相关模型调度并生成运维告警，不自动跨租户猜测归属。

### 2.6 models.dev 模型目录

- 每日下载 `https://models.dev/api.json`，保存原始快照、来源、获取时间和 checksum。
- 校验 JSON、Provider ID、Model ID、单位、币种和价格字段后生成不可变 `CatalogSnapshot`。
- 对比变更并原子发布；失败时保留最近有效快照并标记 `Stale`。
- 目录只包含模型元数据和匿名价格，不包含租户密钥。
- BYOK 从最新已发布快照预填模型、限制、能力和价格。
- 目录同步、发布、手工补价和自动覆盖均写入审计。

### 2.7 价格来源与覆盖

- 每个价格字段独立记录 `models.dev` 或 `manual` 来源。
- models.dev 已有字段优先；缺失字段由 BYOK 用户补充币种、单位、区域和生效时间。
- 每日同步按 Provider + Model ID 精确匹配，models.dev 新增字段自动覆盖对应手工字段。
- 手工值保留历史版本并通知管理员，不用模糊匹配覆盖其他模型。
- 仍然缺失的价格标记为 `Unpriced`，不得按 0 结算。
- models.dev 暂时缺价时保留最近有效价格并标记 `Stale`，进入对账而不是静默改价。

## 3. 推理请求与路由

### 3.1 请求身份

Runner 使用 employee assignment token；管家使用 control service token。租户、员工和预算信息通过 JWT 与请求头传递，请求体保持 provider 原生结构：

```text
Authorization: Bearer <assignment|service token>
X-Regent-Model: <tenant model alias>
X-Regent-Task-Id: <task id，可空>
X-Regent-Budget-Reservation-Id: <reservation id>
X-Regent-Request-Id: <correlation id>

OpenAI body: /v1/chat/completions 原生 schema
Anthropic body: /v1/messages 原生 schema
```

- 网关不信任请求体或 Header 中的 `tenant_id`、`employee_id`、`task_id`，只使用已验证 Employee Assignment Token claim；Token audience 必须为 `regent-gateway`，scope 必须包含 `infer:employee`。
- 网关只把 `X-Regent-Model` 解析为实际 `provider_model_id` 并写入该协议的原生 `model` 字段。
- 不读取、改写或补造其他协议字段；工具调用、流式事件和错误结构保持原生形状。
- `max_tokens`、`max_completion_tokens`、`thinking`、`tools` 等参数由对应协议客户端设置，不由统一 DTO 转换。

### 3.2 路由顺序

1. 验证 JWT、租户、模型权限和任务状态。
2. 检查请求端点协议与模型 `protocol` 一致；不一致返回 422，不尝试转化。
3. 检查模型 `Ready`、健康和租户套餐策略。
4. 校验预算预留金额与请求最大成本匹配。
5. 解析模型别名，注入密钥和实际 provider model id。
6. 使用对应原生插件调用 provider。
7. 记录 usage，结算预算预留，返回原生响应。

### 3.3 同模型重试与恢复

- 模型、模型版本和推理强度通过 EmployeeRuntimeBinding 与员工绑定，TaskRun 固定。
- 只对幂等安全、可重试错误重试，如 429、5xx、网络超时。
- 最多重试 2 次，使用指数退避和总超时。
- 仅允许相同模型版本、推理强度、协议和租户授权的端点或区域切换。
- 禁止自动跨模型、跨版本、跨推理强度或跨协议降级。
- 不允许因 A 租户故障自动使用 B 租户的密钥或模型。
- 流式响应中断后不自动重放非幂等工具调用；按原生错误响应和用量结算。
- 上下文超限通过压缩、拆分或暂停处理，不自动换模型。

### 3.4 模型不可用检测与上报

- 网关按模型、租户和端点统计错误、限流和健康状态。
- 连续 2 次调用失败或 5 分钟持续不可用时生成 `ModelUnavailableEvent`。
- 事件包含员工、绑定模型、受影响 Task、错误、最近成功时间、重试结果和检查点引用。
- 控制面将事件上报直接 Leader，不主动直达 Controller。
- Leader 决定继续等待同模型、调岗或调整绑定；绑定调整只影响后续任务。
- 没有直接 Leader 时 Controller 兜底，管理链无法解决才到 Boss。
- 模型下架时停止新任务，在途任务在安全点暂停或完成。

### 3.5 模型评估

具备模型评估能力的岗位通过独立 MCP 工具面调用 benchmark，使用服务或员工评估 scope。每次评估必须：

- 绑定租户、岗位、模型版本和测试套件版本。
- 单独预留预算并生成用量事件。
- 将结果写入能力矩阵缓存，禁止每开一个岗位重复全量评测。
- 数据集、密封防泄漏、回归频率、评分和质量门槛详见 [14 模型评测与回归](./14-model-evaluation-and-regression.md)。

### 3.6 自动模型档位

- 不手工维护 cheap/mid/premium 清单，档位由 CatalogSnapshot 和 `ModelTierPolicyVersion` 计算。
- 使用 models.dev 的 cost、limit、reasoning、tool_call、structured_output、modalities、knowledge 和 last_updated。
- 先过滤不支持工具调用、上下文不足、价格不完整或状态不可用的模型。
- 能力分前 20% 为 Premium；剩余模型按 `input + output × 0.25` 参考成本取最低 33% 为 Cheap；其余为 Mid。
- 数据不足标记 Unclassified；优先级为 Premium、Cheap、Mid、Unclassified。
- 档位只用于筛选、招聘和预算策略，不替代精确模型绑定。
- 目录更新只影响新任务，不自动换模型或调岗。

## 4. 用量事件与权威源

### 4.1 模型用量事件

```text
llm_usage:
  usage_event_id
  tenant_id, gateway_id
  employee_id, butler_session_id, task_id
  model_id, provider_model_id
  request_id, provider_request_id
  input_tokens, output_tokens, cached_tokens, reasoning_tokens
  catalog_snapshot_id, price_version, currency
  effective_price_refs, pricing_tier
  estimated_amount, provider_reported_amount
  started_at, completed_at, status
  idempotency_key
```

### 4.2 工具用量事件

Runner 上报沙箱、浏览器、存储等工具消耗：

```text
tool_usage:
  usage_event_id, tenant_id, runner_id, employee_id, task_id
  tool_type, duration_ms, cpu_ms, memory_mb_seconds
  storage_bytes, network_bytes
  unit_price_version, amount, currency
```

### 4.3 权威规则

| 成本类型 | 权威源 | Runner/控制面职责 |
|---|---|---|
| 模型输入/输出/缓存 token | 网关 | 只关联 task_id，不重复记账；OpenAI 与 Anthropic 按各自原生缓存字段计量 |
| 工具执行时间与资源 | Runner | 控制面按价目版本计价 |
| 平台订阅费 | 计费系统 | 与模型账本分账 |
| Provider 账单差异 | 周期性对账 | 生成更正账本条目 |

### 4.4 投递与去重

- 网关和 Runner 批量上报，至少一次投递；Gateway 先写持久投影队列，再由后台任务投递 `/internal/metering/usage`，失败指数退避。
- 控制面以 `(source, source_event_id)` 唯一约束去重；无效或跨租户员工/任务归属拒绝，不进入 Unallocated 猜测。
- 迟到事件允许入库，但按发生时间归属预算周期；控制面保留 `occurred_at`，项目支出仅在新事件首次入账时更新。
- 同一请求重试产生多个 provider 调用时，每个调用有独立 usage_event_id，不合并。
- 月度供应商账单对账差异超过 1% 或绝对金额阈值时告警。

## 5. 账本模型

### 5.1 账本条目

```text
ledger_entries:
  id, tenant_id, usage_event_id
  occurred_period, posted_period
  account_type, employee_id, position_id, project_id
  cost_center_type, cost_center_id
  amount, currency, price_version
  entry_type: charge | correction | refund
  corrects_entry_id?, created_at
```

- `account_type`：model、tool、butler、benchmark、platform。
- 员工、岗位、项目和全局视图由同一权威条目投影，不重复计费。
- 归属优先任务所属项目，其次显式项目或成本中心；无项目成本进入 CompanyOverhead，无法归属进入 Unallocated。
- 原始条目不可更新或删除；更正引用原条目并记录原因。
- 详细归属、分摊、结转、异常和原币规则见 [40 成本归属、分摊与异常](./40-cost-allocation-and-anomaly.md)。

### 5.2 结算顺序

1. 接收 usage 事件并去重。
2. 计算金额，绑定价目版本。
3. 释放对应预算预留。
4. 写入 ledger entry 和 Outbox 事件。
5. 更新预算投影和告警状态。

## 6. 预算预留与硬限制

### 6.1 预算层级

- 租户月度硬预算：默认 ¥8000。
- 预警线：默认 ¥5600，即硬预算的 70%。
- 项目、岗位和单任务预算作为预警与评审线，不自动硬终止；任务成本到 100% 时暂停并沿管理链逐级评审。
- 项目预算默认软限制，可经审批设为硬限制；预算不自动结转，只允许所有者显式批准未使用项目预算结转。
- 模型和推理强度绑定员工，成本评审不得通过临时换模型规避。
- BYOK 模型费用仍计入租户运营账本，但不进入平台收入。

### 6.2 预留金额

网关根据请求上限估算：

```text
reserved = max_input_tokens × input_price
         + max_output_tokens × output_price
         + safety_margin
```

`max_output_tokens` 不能超过模型上下文和预算可承担额度。缓存折扣和 reasoning token 按价目版本保守估算。

### 6.3 原子预留

```sql
UPDATE budget_periods
SET reserved_amount = reserved_amount + :amount
WHERE tenant_id = :tenant
  AND period = :period
  AND committed_amount + reserved_amount + :amount <= hard_limit;
```

影响行数为 0 时返回 `429 BUDGET_HARD_LIMIT`，不调用 provider。

- 预留默认有效期 10 分钟，长请求按 task lease 续期。
- 请求完成或失败后释放预留，并按实际用量增加 committed。
- 预留过期由后台任务释放，不能依赖客户端确认。
- 多个预算层级必须全部通过，任一层不足即拒绝。

### 6.4 硬限制边界

平台能保证的是“不授权超过预算预留的调用”。Provider 实际计费与估算可能因 token 计费规则、供应商延迟或账单更正产生偏差，因此：

- 单次请求设置输出上限和总超时。
- 实际超过预留时记录 `budget_overrun` 事件并立即停止新调用。
- 供应商账单差异通过更正条目处理，不修改原始用量。
- 股东收到告警，并可通过升级队列调整预算或终止任务。

### 6.5 预警与行动

| 使用率 | 动作 |
|---|---|
| 70% | 生成预警事件和管家报告 |
| 85% | 限制 benchmark、非关键 premium 调用 |
| 100% | 拒绝新模型调用，允许已授权任务做清理和报告 |
| 超过 100% | 生成 L3 告警和升级事项，冻结非必要任务 |

### 6.6 当前本地实现

- `ModelPriceBook` 以不可变价格快照报价；未定价或价格无效的模型在调用 Provider 前返回 `422 UNPRICED`，不按 0 结算。
- `ModelPricingService` 按输入、缓存输入、输出和最大重试次数保守估算金额，并应用安全边际；金额统一使用最小货币单位整数。
- `LocalBudgetService` 在单进程临界区内同时检查已提交与已预留金额，持久化预留、结算、释放、过期、只增账本、超支事件和对账案件。
- 每个 Provider HTTP 尝试通过 `AttemptObserver` 生成独立 `usage_event_id`；失败尝试也保留用量与错误，成功尝试按请求时的价格快照结算。
- 流式中断在 `finally` 中结算已报告 token；请求失败释放未使用预留，已发生费用不退回。
- 迟到用量按 `occurred_period` 归属、按 `posted_period` 入账；供应商账单差异追加更正/退款条目，不修改原始账目。
- `/admin/budget` 提供周期快照、账本、对账、更正和过期端点；生产环境必须配置 Gateway 管理凭据。
- `HttpUsageProjector` 持久化待投影事件并按 `usage_event_id` 去重；ControlPlane 幂等写成本账本、审计和项目支出，投影响应 204 后 Gateway 才删除队列项。
- 生产必须显式配置 `Gateway:ControlPlaneUrl`、`Gateway:MeteringToken` 和 ControlPlane `Metering:Token`；内部端点使用 `X-Regent-Metering-Token`。
- 当前持久化实现面向本地单进程；生产多 Worker 原子预留仍需 Postgres 条件更新，见 15/49。

## 7. 成本异常

- 单任务成本超过岗位近 30 天同类完成任务日均成本 3 倍且样本至少 5 条：L1 告警并通知直接 Leader；样本不足不触发。
- 项目实际或预测成本超过预算 120%：L2 告警。
- 租户预测超过月度硬预算 100%：L2；超过 110%：L3 并冻结非必要任务。
- 同一 Provider + Model ID 在 24 小时内有效价格变化超过 20%：停止自动路由，要求重新评估。
- 网关用量与控制面投影差异超过月度账单 1% 或配置绝对阈值：进入对账队列。
- 重复 source_event_id：去重并记录指标，不重复入账。
- Butler 同一 Job 连续 3 次失败仍计费：在报告中单列异常项。
- 异常只告警和触发既有评审，不自动修改预算、模型、岗位或绩效。
- 公共成本分摊、预算结转、原币和迟到更正详见 [40 成本归属、分摊与异常](./40-cost-allocation-and-anomaly.md)。

## 8. 验收场景

1. 两个并发请求同时竞争最后一份预算，只有一个获得预留。
2. 预算达到硬线后，新调用返回 429 且 provider 无请求记录。
3. 网关重复上报同一用量事件只生成一条账目。
4. Runner 上报模型费用不会覆盖网关权威金额。
5. 请求失败仍结算已发生的 provider 用量并释放预留。
6. 轮换期间在途请求使用旧密钥，新请求使用新密钥。
7. 删除模型后新任务无法路由，历史账本保持完整。
8. A 租户模型故障不会路由到 B 租户。
9. 管家和 benchmark 成本可在账本中单列。
10. 预算预警、硬限制、对账差异均有可验证事件和审计记录。
11. 配置 Ollama、本地模型或本地 GPU 端点时返回明确不支持错误。
12. Provider 流式中断后，已产生用量仍可结算，未完成工具调用不自动重放。
13. OpenAI 请求进入 Anthropic 端点时返回 422，不发生协议转化。
14. OpenAI 与 Anthropic 的工具调用、流式事件和错误结构分别保持原生形状。
15. 任务中禁止自动跨模型、跨版本、跨推理强度和跨协议切换；换模型必须调岗或变更绑定。
16. 运营平台可以下架 Provider 或模型，但不能读取租户密钥。
17. Provider 目录重复或乱序同步不会覆盖更新版本。
18. models.dev 每日同步失败保留最近有效快照。
19. BYOK 自动预填 models.dev 已有字段。
20. 缺失价格由用户补充并记录币种和单位。
21. models.dev 新增同字段时自动覆盖手工值。
22. 未定价字段不会按 0 结算。
23. 每日消耗绑定请求发生时的价格快照。
24. cheap/mid/premium 由目录数据自动计算。
25. 档位变化不影响在途任务。
26. 历史账目不会因目录更新而改写。
27. 同一 Usage 只生成一条钱账记录。
28. 任务费用归项目并保留发生时员工和岗位快照。
29. 无项目 Butler 和公共评测费用进入公司公共成本。
30. 无效或跨租户归属进入 Unallocated 并记录安全事件。
31. 公共成本默认不自动分摊。
32. 分摊权重不为 100% 时拒绝规则，分摊不增加总成本。
33. 项目预算默认软限制，可经审批设为硬限制。
34. 未审批不能结转预算，预留、欠款和超支不得结转。
35. 岗位样本不足 5 条时不触发任务成本异常。
36. 同类任务成本超过 3 倍时生成 L1。
37. 项目预测超过 120% 时生成 L2。
38. 租户预测超过 100% 和 110% 时分别生成 L2 和 L3。
39. 价格 24 小时变化超过 20% 时停止自动路由。
40. 账单差异超过阈值时生成对账案件。
41. 成本异常不自动改预算、换模型或惩罚员工。
42. 不同币种分别统计且不直接相加。
43. 迟到费用归发生周期，关闭周期通过当前周期更正。
44. 重复 Usage 不重复入账。
45. 预算为 0 或币种不一致时不计算偏差比例。
46. 项目预算偏差统一使用 ±10% 目标和 ±20% 预警。

## 9. 能力评估与 Provider 韧性

### 9.1 能力矩阵

```text
BenchmarkRun:
  id, tenant_id, model_id, provider_model_id
  suite_version, capability_domain
  started_at, completed_at, status
  score, cost, usage_event_ids, artifact_id
```

- 测试套件包含代码、推理、写作和成本域，每个套件有版本。
- 模型版本、量化方式、上下文和提示模板不同视为不同评估对象。
- 租户原始评测结果私有；SaaS 共享层只发布匿名聚合分数和适用条件。
- 新岗位优先复用有效评估；模型或套件版本变化时才重测。
- 评估失败或预算不足不会把模型标记为 `Ready`。

### 9.2 模型运行边界

- 当前只支持 OpenAI 与 Anthropic 云端原生端点。
- 不支持本地 GPU、Ollama、本地模型档、GPU 排队、抢占、显存调度或本地推理端点。
- 协议枚举和 Provider 插件不保留 `ollama_openai` 或本地兼容实现。
- 员工容器不挂载 GPU，Runner 不上报本地推理资源。
- 本地推理如将来重新引入，必须作为新需求评审。

### 9.3 Provider 错误与流式语义

| 情况 | 行为 |
|---|---|
| 连接失败且未开始生成 | 可重试，沿用同一幂等键 |
| 429 | 遵循 Retry-After，受总超时和重试次数限制 |
| 5xx | 指数退避，最多 2 次 |
| 流式中断 | 结算已报告用量，返回明确错误，不自动重放工具调用 |
| 工具调用参数不完整 | 不执行工具，交由 Agent 修复或终止 |
| 内容策略拒绝 | 不重试，记录安全事件 |
| Provider 持续失败 | 打开熔断器，只允许同模型重试或同版本端点切换，并上报直接 Leader |

熔断器按 provider + 租户维度统计，避免一个租户的异常影响其他租户。错误响应保持对应协议的原生形状，并通过 `X-Regent-Request-Id` 响应头提供内部关联 ID；不把 Anthropic 错误转换为 OpenAI 错误，也不反向转换。
