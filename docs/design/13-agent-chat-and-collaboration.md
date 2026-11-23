# 13 Agent 聊天与协作

> 状态：v0.1（待评审）  
> 对应缺口：G-30  
> 依赖：消息总线、通信路由、任务检查点、记忆召回、项目管理

## 1. 定位

聊天负责员工之间的信息共享、讨论和协作入口；任务、评审、决策和交接仍由正式状态机负责。

```text
聊天 / @ / 线程
  → 协作消息
  → Help / Review / Decision / Delegation / Handoff
  → 必要时提升为 Task / Requirement / Defect / Decision
```

聊天是弱证据，不直接修改领域状态，也不扩大权限。

## 2. 核心不变量

1. 私人会话默认仅参与者可见，群聊按项目、岗位或主题范围隔离。
2. 所有聊天经过控制面，Runner 之间不直连。
3. `@` 只产生定向通知和路由，不自动派工或绕过通信权限。
4. 委派和交接使用显式状态机，接收者可以带理由拒绝。
5. Ack 表示收到，不表示同意或完成。
6. P0/P1 才能唤醒休眠员工，普通消息进入队列或摘要。
7. 聊天召回必须权限过滤、加权排序并渐进披露。
8. 聊天是弱证据，不能覆盖正式 Decision、测试或 Artifact。
9. 重要聊天结论必须提升为正式工作项或 Decision 才能改变状态。
10. 聊天内容视为不可信数据，不能改变工具策略或系统指令。
11. 密钥、凭证和未脱敏敏感参数禁止进入聊天。
12. 消息、成员、提升、删除和越权拒绝均可审计。

## 3. 会话模型

| 类型 | 范围 | 默认成员 |
|---|---|---|
| Direct | 私人会话 | 两名参与者 |
| Project | 项目群 | 当前项目成员 |
| Position | 岗位群 | 岗位子树成员 |
| Topic | 主题群 | 围绕需求、缺陷或方案邀请的成员 |
| System | 系统通知 | 指定员工或角色 |

```text
Conversation:
  id, tenant_id, type, scope_type, scope_id
  name?, owner_employee_id?, status, created_at

ConversationMember:
  conversation_id, employee_id
  role: owner | member | reader
  joined_at, left_at, muted_until?
```

项目或岗位变化时自动重新计算成员；离开范围后立即失去新内容访问权。

## 4. 消息类型与优先级

| 类型 | 用途 |
|---|---|
| Chat | 普通交流 |
| TaskAssignment | 正式派工 |
| DelegationRequest | 委派子任务 |
| HelpRequest | 请求信息或专业支持 |
| HandoffRequest | 转移任务所有权 |
| ReviewRequest | 请求评审或验收 |
| DecisionRequest | 请求明确决策 |
| Interrupt | 高优先级打断 |
| StatusUpdate | 进度、阻塞和风险 |
| Discussion | 线程讨论 |
| Ack / Reject / Complete | 结构化回应 |

| 优先级 | 场景 | 投递 |
|---|---|---|
| P0 | 安全、红线、生产事故 | 立即唤醒，安全点打断 |
| P1 | 阻塞决策、即将到期 | 允许窗口内优先，禁用窗口按策略唤醒 |
| P2 | 普通任务、评审、协作 | 允许窗口内执行，禁用窗口排队 |
| P3 | 通知、讨论、FYI | 批量摘要，禁用窗口不唤醒 |

## 5. 消息信封与生命周期

```text
CollaborationMessage:
  id, tenant_id, conversation_id, seq
  from_employee_id, message_type, priority
  subject_type, subject_id
  task_id?, project_id?
  thread_id, correlation_id, causation_id
  deadline_at?, expected_response
  payload_schema_version, idempotency_key
  created_at
```

```text
Sent → Delivered → Acked → Processed
                  └→ Rejected
         └→ Expired / DeadLetter
```

- 至少投递一次，Inbox 去重。
- 同一会话内有序，不承诺跨会话全局有序。
- 超时未 Ack 先提醒，再沿管理链升级。
- 消息不直接改变任务或项目状态。

## 6. 委派

```text
Proposed → Accepted → InProgress → Completed
         └→ Rejected
         └→ Cancelled
```

- 委派者保留最终责任。
- 接收者必须满足能力、WIP 和权限要求。
- 拒绝原因包括能力不匹配、负载过高、上下文不足或利益冲突。
- 拒绝后改派或升级，不强派。
- 子任务权重、验收标准和证据要求随委派固定。

## 7. 交接

交接包包含：

```text
目标与验收标准
TaskCheckpoint
已完成与未完成项
Artifact 引用
Decision
风险、失败和反证
当前阻塞
下一步动作
```

```text
HandoffRequest → Accepted → 原子转移所有权
              └→ Rejected → 原负责人继续
```

- 接收者确认前不转移。
- 只传授权范围内的检查点和证据，不复制完整私有上下文。
- 交接更新负责人、租约和审计。

## 8. 求助、拒绝与打断

HelpRequest 必须包含问题、已尝试步骤、证据、期望答案和截止时间。回应可以是 `Answer`、`Decision`、`Redirect`、`NeedMoreContext` 或 `Reject`。

合理拒绝不是绩效失败；被迫接受无法完成的任务才是问题。

Interrupt 规则：

- 只允许 P0/P1。
- 在当前安全点暂停并保存 TaskCheckpoint。
- 不取消已提交的外部写操作。
- 处理完回到原任务。
- 高频打断触发注意力超载告警。

## 9. @ 与路由

- `@employee` 使用明确员工 ID。
- `@role` 按岗位标签解析，但必须经过 CommunicationProfile 授权。
- `@` 生成定向通知，不自动创建任务。
- 无直接通信权限时返回 `RouteViaLeader` 或 `Forbidden`。
- `@all` 仅管理者可用，并限制频率和范围。
- 普通群消息不默认抄送 Controller。

## 10. 加权召回

聊天是弱证据，初始来源权重：

| 来源 | 权重 |
|---|---:|
| 已提升为 Decision 的聊天 | 0.7 |
| 普通项目群聊天 | 0.4 |
| 私人聊天 | 0.3 |
| 未验证传言、单方推测 | 0.1 |

```text
Score = 相关度 × 来源权重 × 时间衰减 × 作者可信度 × 验证程度
```

加权修正：

- 关联 Task：+0.1。
- 被回复或采纳：+0.1。
- 有 Artifact 或测试佐证：+0.2。
- 提升为 Decision：按正式决策权重。
- 重复刷屏、过期或违规：降权或不召回。

同一结论被多人重复发送不自动提高真实性，必须有独立证据或正式确认。

## 11. 渐进式披露

| 级别 | 内容 | 默认行为 |
|---|---|---|
| L0 | 会话、未读、@、主题 | 始终可见 |
| L1 | 线程摘要、结论、待办、链接 | 默认召回 |
| L2 | 与当前任务相关的消息 | 按相关性召回 |
| L3 | 完整线程、附件和原始记录 | 显式展开 |

工具：

```text
chat.search
chat.expand_thread
chat.read_range
chat.open_attachment
```

- 被 @ 时披露原消息、有限线程上下文和关联工作项。
- 不自动加载整个群历史。
- 摘要带 `derived_from`，不替代原文。
- 展开和搜索时再次鉴权。
- 动态聊天放在稳定前缀之后，避免破坏缓存。

## 12. 正式提升

```text
聊天线索 → PromoteToTask / Requirement / Defect / Decision
```

- 提升时引用原消息和线程。
- 创建正式对象后，状态由对应状态机管理。
- 聊天中的“同意”不等于审批。
- 正式 Decision 与聊天冲突时，以 Decision 为准并保留争议记录。

## 13. 安全、隐私与反作弊

- 私人会话仅参与者可读，群聊按范围隔离。
- 员工离职或离开项目后失去访问权。
- 默认消息在线保留 90 天，之后归档 1 年；重要结论必须提升为 Decision 或知识库版本。
- 归档聊天仍需原权限过滤，普通 Agent 只检索 Hot。
- 密钥、Authorization 头、凭证和敏感参数禁止发送。
- 不按消息数、@ 次数或评论数奖励产量。
- 灌水、重复和无意义回复降权。
- 错误信息可标记、纠正并通知受影响任务。
- 审计成员、权限、提升、删除和越权拒绝，不默认公开正文。

## 14. Tool 与 API

| Tool | 作用 |
|---|---|
| `chat.send` | 发送聊天或协作消息 |
| `chat.mention` | 定向 @ 员工或岗位 |
| `chat.search` | 权限过滤后搜索 |
| `chat.expand_thread` | 展开线程 |
| `chat.promote` | 提升为正式工作项 |
| `collaboration.delegate` | 发起委派 |
| `collaboration.handoff` | 发起交接 |
| `collaboration.interrupt` | 发起高优先级打断 |

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/conversations` | 创建私人或群组会话 |
| GET | `/conversations/{id}/messages` | 按游标拉取消息 |
| POST | `/conversations/{id}/messages` | 发送消息 |
| POST | `/messages/{id}/ack` | 确认收到 |
| POST | `/messages/{id}/promote` | 提升为正式对象 |
| POST | `/collaboration/delegations` | 发起委派 |
| POST | `/collaboration/handoffs` | 发起交接 |

所有写 API 幂等并审计；实时推送可用 SignalR，消息表仍是权威源。

## 15. 度量

| 指标 | 定义 |
|---|---|
| 聊天召回准确率 | 召回内容相关且有权访问的比例 |
| 聊天上下文噪声率 | 注入但无助于当前任务的聊天占比 |
| Ack 延迟 | Sent 到 Acked 的时间 |
| 求助解决率 | HelpRequest 得到有效解决的比例 |
| 委派接受率 | DelegationRequest 被接受的比例 |
| 交接完整率 | 交接包证据完整的比例 |
| 打断率 | P0/P1 打断占工作消息的比例 |
| 注意力超载率 | 员工消息量超过阈值的时段占比 |
| 聊天提升率 | 聊天线索提升为正式工作项的比例 |

## 16. 验收场景

1. 两名员工可以创建私人会话，第三人不可见。
2. 项目群自动包含当前项目成员，离开后失去访问权。
3. 开发可以在项目群 @ 测试或架构师。
4. 无权限 @ 返回路由建议，不直接投递。
5. `@` 不自动创建或分配任务。
6. 普通消息不唤醒休眠员工，P0/P1 可以唤醒。
7. 委派接收者可以带理由拒绝。
8. 交接确认前任务所有权不变。
9. Ack 不表示任务同意或完成。
10. 聊天结论可以提升为 Task、Defect 或 Decision。
11. 聊天召回按来源、时间、相关度和权限加权。
12. 默认只召回摘要和相关消息，完整线程按需展开。
13. 私人聊天不进入其他员工或公共知识库。
14. 重复刷屏和灌水降权，不按消息数量奖励。
15. 聊天与正式 Decision 冲突时以 Decision 为准。
16. 密钥或凭证消息被拒绝并告警。
17. 聊天内容不能改变工具权限或领域状态。
18. 消息、成员、提升、删除和越权拒绝均可审计。
