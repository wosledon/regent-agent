# 11 项目管理中心

> 状态：v0.1（待评审）  
> 对应缺口：G-28  
> 依赖：动态组织、Tool Profile、工作流、运行时、绩效与审计

## 1. 定位

项目管理中心负责把公司目标转换为可执行、可评审、可验收和可复盘的工作项。它不是固定项目经理专属模块，权限由 `project.*`、`requirement.*`、`task.*` 和 `review.*` 能力决定。

```text
Project
  └── Requirement
        ├── AcceptanceCriteria
        ├── Task
        │     └── Defect
        └── Discussion / Debate / Decision
```

Project 是目标和里程碑容器，Requirement 是可评审的工作意图，Task 是可独立执行的工作单元，Defect 是测试或验收发现的问题。所有状态、认领、依赖和关闭条件由控制面管理；LLM 只负责分析、建议、拆分、总结和讨论。

## 2. 核心不变量

1. 角色名称不决定权限，管理、开发、测试和评审权限来自 Capability。
2. 需求没有验收标准不得进入 Ready，也不得直接进入开发。
3. 开发认领 Task，不直接认领未拆分的 Requirement。
4. 自动认领必须满足能力、WIP、依赖和利益冲突约束。
5. 每个工作项必须有唯一 TenantId、类型、状态和版本。
6. 测试提交缺陷，开发修复，测试复测关闭。
7. 高风险评审者不得为作者本人。
8. 讨论本身不修改状态，状态变化必须通过正式命令。
9. 争议必须限时形成决策或升级，被否决意见不得删除。
10. 工作流不授予额外项目权限，只声明步骤和所需能力。
11. 项目、需求、任务、缺陷、讨论、决策和审计均按租户隔离。
12. LLM 生成的拆分、优先级和认领建议不得绕过确定性门禁。

## 3. 角色与能力

| 角色示例 | 主要能力 | 默认工具画像 |
|---|---|---|
| 项目管理者 | `project.create`、`requirement.create`、`requirement.review`、`task.assign`、`project.report` | 项目规划与分配 |
| 架构师 | `requirement.analyze`、`architecture.design`、`technical.review` | 需求分析、方案设计、评审 |
| 开发 | `task.claim`、`task.update`、`workspace.write`、`test.run`、`defect.fix` | 编码、测试、成果 |
| 测试 | `test.plan`、`test.run`、`defect.report`、`defect.retest` | 测试执行与缺陷 |
| 独立验收 | `acceptance.run`、`acceptance.report` | 体验与验收 |
| 决策者 | `project.decide`、`requirement.decide`、`debate.adjudicate` | 决策与升级 |

普通员工都可以在授权范围内查看、评论和提交改进建议，不需要固定为“需求管理员”或“测试角色”。

### 3.1 任务类型

| 领域 | Task Type |
|---|---|
| 发现 | `research`、`product.define` |
| 设计 | `architecture.design`、`technical.design` |
| 交付 | `engineering.implement`、`documentation` |
| 质量 | `test.plan`、`test.execute`、`defect.fix` |
| 评审 | `code.review`、`design.review`、`security.review` |
| 对抗 | `debate.red_team`、`debate.challenge` |
| 决策 | `decision.adjudicate`、`project.decide` |
| 验收 | `experience.validate`、`acceptance.review` |
| 运行 | `release.execute`、`incident.respond` |
| 组织 | `hiring.interview`、`performance.assess` |
| 进化 | `workflow.improve`、`knowledge.curate`、`skill.improve` |

TaskTypeDefinition 决定所需能力、Tool Profile、权重规则、Done 模板、证据、评审和 KPI。类型不绑定岗位名称，一个员工可以执行多种类型。

## 4. 数据模型

| 实体 | 关键字段 | 不变量 |
|---|---|---|
| Project | id、tenant_id、name、mission、status、owner_employee_id、budget_ref、started_at、target_at | 属于一个租户；项目终止必须复盘 |
| Milestone | id、project_id、name、target_at、acceptance_rule、status | 里程碑完成需要满足验收规则 |
| ProjectMember | project_id、employee_id、role_capabilities、joined_at、left_at | 项目成员不自动获得组织管理权限 |
| Requirement | id、project_id、title、problem、scope、priority、status、acceptance_criteria、version | Ready 前必须有验收标准 |
| DecompositionPlan | id、requirement_id、requirement_version、tasks、acceptance_coverage、dependencies、risks、total_weight、status | LLM 提案，控制面校验和审批后生效 |
| Task | id、requirement_id、title、task_type、required_capabilities、estimate、status、assignee_id、wip_limit | 必须归属于一个需求；认领受能力与 WIP 限制 |
| TaskTypeDefinition | task_type、required_capabilities、tool_profile、weight_rubric、dod_template、evidence_requirements、reviewer_requirements、kpi_dimensions、version | 类型决定任务语义，不绑定岗位名称 |
| EmployeeWorkLedger | id、employee_id、task_id、task_type、task_weight、role_relation、quality_factor、capability_fit_factor、weighted_score、cost、duration、evidence_refs | 按事实记录贡献，不得重复计分 |
| TaskWeight | id、task_id、complexity_factors、weight、rubric_version、rated_by、approved_by、locked_at | Ready 前锁定；员工不能自评；拆分不得增加总权重 |
| TaskRequirement | task_id、dimension、minimum_score、preferred_score、requires_pairing、requires_review | 能力需求在认领前固定 |
| CapabilityAssessment | id、employee_id、dimension、model_score、history_score、tool_fit、context_fit、effective_score、confidence、version | 模型与历史表现共同校准，员工不可自改 |
| TaskDependency | task_id、depends_on_task_id、type | 依赖图无环 |
| Defect | id、requirement_id、task_id?、reported_by、assigned_to、severity、status、reproduction_steps、evidence_refs | 由测试复测关闭，不能由报告者单人静默关闭 |
| Discussion | id、subject_type、subject_id、status、created_by、created_at | 线程关联具体工作项 |
| DiscussionComment | id、discussion_id、author_id、body、evidence_refs、created_at | 评论不可静默改写；更正新增评论 |
| Debate | id、subject_type、subject_id、question、options、status、deadline、adjudicator_id | 超时必须升级或形成决策 |
| Review | id、subject_type、subject_id、reviewer_id、author_id、verdict、findings、evidence_refs | 高风险评审作者与评审者必须分离 |
| Decision | id、subject_type、subject_id、selected_option、rationale、evidence_refs、decision_maker、dissenting_refs | 决策不可删除；变更创建新决策 |
| WorkItemEvent | id、work_item_type、work_item_id、event_type、actor_id、payload_hash、created_at | 用于审计、时间线和确定性状态重放 |
| DoneEvidence | id、work_item_type、work_item_id、criteria_results、artifact_refs、test_results、review_results、security_checks、open_defects、cost_refs、audit_refs、validator_id、validated_at、version | 控制面依据证据关闭；不可静默改写 |

## 5. 项目状态

```text
Proposed → Approved → Active → Paused → Active
                      └→ Completed
                      └→ Cancelled
```

- Proposed 项目不具备执行权限，只有 Approved 后才能创建可执行工作项。
- Active 项目可以进入里程碑和发布门禁。
- Paused 项目停止新认领，在途任务按策略保留或暂停。
- Completed 必须满足里程碑和验收条件。
- Cancelled 必须记录原因、在途任务处理和复盘。

## 6. 需求、任务与缺陷状态

### 6.1 Requirement

```text
Draft → Ready → InProgress → InReview → Accepted
          │          └→ Blocked
          └→ Rejected / Cancelled
```

- Draft：可编辑，验收标准可以尚未完成。
- Ready：验收标准、范围、优先级和拆分结果通过就绪审查。
- InProgress：至少一个关联任务开始执行。
- InReview：等待测试、独立验收或方案评审。
- Accepted：满足功能、易用性、设计合理性和可访问性标准，并形成不可变验收证据。
- Blocked：记录阻塞原因、责任人和解除条件。

### 6.2 Task

```text
Backlog → Ready → Claimed → InProgress → InReview → Done
   │        │                    └→ Blocked
   │        └→ Cancelled
   └→ Cancelled
```

- Ready 任务必须具备所需能力、估算、依赖和完成条件。
- Claimed 通过原子认领创建，重复认领返回当前状态，不重复派工。
- InProgress 员工持有任务租约，Runner 重启后按租约恢复。
- InReview 必须提交 Artifact、测试结果和评审请求。
- Done 必须通过完成门禁，不能仅由执行者声明完成。

### 6.3 Defect

```text
New → Triaged → Assigned → Fixing → ReadyForRetest → Closed
                                      └→ Reopened
```

- New：测试或验收提交，包含复现步骤和证据。
- Triaged：确认范围、严重级别、责任人和是否阻塞发布。
- Assigned：指派给修复者，可回派原任务作者或按能力重新分配。
- Fixing：开发修复并关联提交。
- ReadyForRetest：修复者提交复测版本。
- Closed：测试复测通过；Reopened 保留历史且重新进入修复流程。

### 6.4 统一完成标准

通用 Done：

- 关联需求、目标和验收标准。
- Artifact、代码、文档或决策记录已提交。
- 测试、评审、校验或验收结果可追溯。
- P0/P1、安全、数据和权限问题清零。
- 依赖、任务、缺陷和里程碑状态一致。
- 成本入账，提交者、验证者、版本和结果进入审计。
- 执行者不能直接关闭自己的工作项。

分类标准：

- 开发：CI、扫描、测试、评审、文档和回滚方案通过。
- 测试：计划、环境、结果、缺陷证据和回归范围完整。
- 调研/产品/设计：来源、反证、范围、验收标准和 Decision 完整。
- 需求：所有任务 Done，Q-002 验收通过，业务指标可采集。
- 里程碑/项目：验收、发布、观察、审计和复盘完成。
- 缺陷：原场景复测和关键回归通过，由测试关闭。

关闭流程：

```text
执行者提交 → 验证者检查 → 控制面计算门禁 → Done
```

例外豁免必须记录原因、范围、期限和批准人；安全、租户隔离、密钥保护和审计不可豁免。

### 6.5 需求自动拆分

LLM 生成 DecompositionPlan，控制面校验：

- 验收标准全部被 Task 覆盖。
- 无孤儿、重复或不可独立验收的 Task。
- 依赖图无环。
- 所需能力存在且已授权。
- 子任务总权重不超过原需求。
- 不引入未审批的范围、预算、安全、数据或外部动作风险。
- 作者与评审者保持分离。

粒度规则：

- 一个 Task 一个主责、一个可验证结果。
- 初始估算 0.5～1 人日，超过 2 人日继续拆分。
- 单需求默认 3～12 个 Task，超过 12 个拆成子需求。
- 测试、评审、文档和迁移等独立工作显式建 Task。

审批规则：

- 低风险且全部校验通过可自动批准。
- 跨团队、高权重、歧义、新能力、安全、数据迁移或发布风险必须审批。
- 高风险沿管理链逐级审批：直接 Leader → 更高层 Leader → Controller → Boss。
- 提交人不能自审；专业评审不替代最终管理审批。

```text
Draft → Proposed → Approved → Active → Revised
```

Approved 冻结；重拆生成增量方案并保留旧 Task、权重和证据。Approved 后 Task 才进入 Ready，并通过幂等投影进入运行时任务表；自动拆分不等于自动派工。运行时认领和完成回写交付状态，提交、独立评审和 DoneEvidence 继续沿用同一任务 ID。

### 6.6 任务模板与工作台账

ReviewTask：

```text
输入 Artifact → 评审标准 → quorum → findings → verdict → DoneEvidence
```

DebateTask：

```text
议题 → 方案 → 正反方 → 证据 → 截止时间 → 结论或升级
```

AdjudicationTask：

```text
争议包 → 证据集合 → 裁决权限 → Decision → 反方 → 复查时间
```

复杂度按类型评估：

- 开发看技术不确定性、影响范围和调试难度。
- 测试看风险覆盖、环境复杂度和回归范围。
- 评审看被评审对象的风险和复杂度。
- 裁决看影响范围、证据冲突和跨团队程度。
- 对抗看方案风险、反证难度和潜在损失。

EmployeeWorkLedger 记录 Task Type、权重、核心/辅助关系、质量、能力匹配、加权得分、成本、时长和证据。KPI 按类型统计一次通过率、返工率、漏检率、裁决推翻率、对抗发现率和岗位匹配度。

同一成果只能有一个主任务权重；父子和协作任务不得重复计分，普通聊天不自动成为任务。

## 7. 自动认领与分配

系统按确定性评分推荐负责人：

```text
ClaimScore =
  40% 能力匹配度
+ 20% 历史同类任务成功率
+ 10% 工具链匹配
+ 10% 成本效率
+ 10% 当前可用容量
+ 10% 独立性与利益冲突检查

EffectiveClaimScore =
  ClaimScore
  - 当前负载惩罚
  + 稀缺能力奖励
  + 公平性奖励
  - 连续高负载惩罚
```

WIP 规则：

- 员工容量为 3、5 或 8，由岗位和验证能力决定。
- 当前负载为 Claimed、InProgress、InReview Task 的固定权重之和，完成前不释放。
- 同时 InProgress 最多 2 个，高权重任务最多 1 个，预留 20% 容量处理 P0/P1。
- 高难度任务优先推荐给能力达标员工，但强能力员工不能包揽全部高权重任务。
- 连续 3 个周期承担超过 60% 高权重任务时触发负载复核。
- stretch task 默认不超过员工任务的 20%，且不能突破能力门槛。

分配流程：

```text
任务 Ready
  → 过滤 Authorized Capabilities
  → 检查 WIP 与依赖
  → 排除利益冲突
  → 计算 EffectiveClaimScore
  → 发出 15 分钟认领邀请
  → 接受则原子认领，拒绝或超时则推荐下一人
  → 记录分数、负载、原因和算法版本
```

- TaskWeight 在 Ready 前由非执行者评分并锁定，认领后不得修改。
- 员工可以主动认领，但必须满足相同约束。
- 改派顺序为保留并配对 → 拆分 → 改派 → 沿管理链升级。
- 改派由直接 Leader 审批并记录原因，不静默转移。
- 原负责人被冻结或离职时，任务回到 Ready 或 Blocked。
- 推荐结果可解释，不能只返回一个不可审计的模型分数。
- LLM 可以建议拆分或负责人，但最终认领由控制面事务完成。

## 8. 讨论、对抗评审与决策

### 8.1 线程讨论

每个 Project、Requirement、Task、Defect 和 Review 都可以有 Discussion：

```text
问题 → 观点 → 证据 → 反证 → 待办 → 结论 → Decision
```

- 评论支持引用工作项、Artifact、测试结果和外部来源。
- 评论更正通过新增评论实现，不静默改写历史。
- 涉及状态变化时创建 Command 和 WorkItemEvent，不把聊天内容当状态。
- 讨论可生成待办，但待办仍受权限和 WIP 约束。
- 聊天线索可通过 `chat.promote` 提升为 Task、Requirement、Defect 或 Decision，并保留原消息引用；聊天本身不改变状态。

### 8.2 Debate 与 Review

```text
Debate: Open → EvidenceCollecting → DecisionPending → Resolved → Escalated
Review: Requested → InReview → Approved | ChangesRequested | Rejected | Blocked
```

评审 quorum：

| 类型 | 要求 |
|---|---|
| 低风险 | 1 名非作者 |
| 标准代码、设计、需求 | 1 名对应能力非作者 |
| 高权重、跨团队、核心架构 | 2 名独立评审，至少 1 名专家 |
| 安全、数据迁移、发布 | 专业评审 + 管理审批 |
| 独立验收 | 与实现者分离的验收者 |

通过规则：

- 所有必需评审通过且无阻断 finding 才 Approved。
- 任一要求修改则 ChangesRequested。
- 证据不足为 Blocked，不能默认通过。
- 作者不计算 quorum，高风险不能简单多数压倒安全意见。

ReviewFinding 必须包含级别、类别、描述、证据、影响、建议和 blocking。评审结论还要说明范围、标准和未覆盖部分。

时间盒：

| 环节 | 时限 |
|---|---:|
| Ack | 30 分钟 |
| 普通评审 | 4 小时 |
| 高风险评审 | 1 个工作日 |
| Debate 证据收集 | 24 小时 |
| 裁决 | 证据截止后 4 小时 |

超时不同意：普通评审改派，争议进入裁决，高风险升级直接 Leader。

裁决规则：

- 技术评审判断质量，管理裁决决定风险与资源。
- 沿直接 Leader → 更高层 Leader → Controller → Boss 逐级处理。
- 裁决者不能是作者，专家意见不替代最终管理审批。
- Decision 保留 selected_option、rationale、evidence_refs、dissent、conditions 和 review_at。
- 有条件批准必须满足可验证条件才能进入下一阶段。
- 安全、租户隔离和红线不可被裁决推翻。

## 9. Tool 契约

| Tool | 能力 | 作用 |
|---|---|---|
| `project.create` | `project.create` | 创建 Proposed 项目 |
| `project.approve` | `project.decide` | 批准项目进入执行 |
| `project.report` | `project.report` | 生成基于事实的项目状态报告 |
| `requirement.create` | `requirement.create` | 创建需求和验收标准 |
| `requirement.ready` | `requirement.review` | 通过就绪审查 |
| `requirement.analyze` | `requirement.analyze` | 分析范围、依赖和风险 |
| `task.create` | `task.create` | 从需求拆分任务 |
| `task.claim` | `task.claim` | 原子认领任务 |
| `task.assign` | `task.assign` | 管理者指派或重新分配 |
| `task.submit` | `task.submit` | 提交成果并进入评审 |
| `defect.report` | `defect.report` | 测试提交缺陷 |
| `defect.fix` | `defect.fix` | 开发提交修复 |
| `defect.retest` | `defect.retest` | 测试复测并关闭或重开 |
| `discussion.comment` | `discussion.participate` | 发表评论和引用证据 |
| `review.request` | `review.request` | 发起代码、方案或验收评审 |
| `review.submit` | `review.submit` | 提交结构化评审结论 |
| `debate.adjudicate` | `debate.adjudicate` | 裁决争议并形成 Decision |

所有 Tool 都通过 Tool Registry/PEP，能力不足返回 `FORBIDDEN`；已授权但当前未激活返回 `NeedsActivation`。项目管理工具不授予组织、预算或发布权限。

## 10. API

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/projects` | 创建项目 |
| POST | `/projects/{id}/approve` | 批准项目 |
| POST | `/projects/{id}/milestones` | 创建里程碑 |
| POST | `/requirements` | 创建需求 |
| POST | `/requirements/{id}/ready` | 通过就绪审查 |
| POST | `/requirements/{id}/tasks` | 拆分任务 |
| POST | `/tasks/{id}/claim` | 原子认领 |
| POST | `/tasks/{id}/submit` | 提交成果 |
| POST | `/defects` | 提交缺陷 |
| POST | `/defects/{id}/retest` | 复测关闭或重开 |
| POST | `/discussions` | 创建工作项讨论 |
| POST | `/discussions/{id}/comments` | 发表评论 |
| POST | `/reviews` | 发起评审 |
| POST | `/reviews/{id}/submit` | 提交评审结论 |
| POST | `/debates/{id}/adjudicate` | 裁决争议 |
| GET | `/projects/{id}/timeline` | 获取项目时间线 |

写 API 必须支持 Idempotency-Key、版本号和审计；DiscussionComment 和 Decision 不提供静默删除或改写 API。

## 11. 调度与运行时

- Requirement 就绪、任务认领、提交评审、缺陷复测等事件通过 Outbox 投递。
- 任务认领使用数据库事务和条件更新保证只有一个成功认领者。
- 执行任务复用普通 Task lease/fencing，项目管理记录与运行时租约通过同一 task_id 互相引用。
- 计划批准创建运行时任务，运行时认领/完成回写 `Claimed`/`ReadyForReview`；HTTP 与 Agent `project.claim_task`/`project.submit_task` 使用同一运行时投影，重复投影不重复创建任务或审计。
- 工作流步骤可以创建或推进 Task，但不能跳过 DoR、评审或验收门禁。
- 项目定时报告由确定性查询生成，LLM 只负责摘要和风险解释。
- 项目暂停、取消或成员离职时，在途任务按租约策略暂停、释放或重新分配。
- Runner 重启后从控制面恢复任务、讨论事件和评审状态，不依赖本地持久队列。

## 12. 安全、审计与反作弊

- 所有工作项和查询强制 TenantId 隔离，项目成员权限不能跨租户。
- 作者不能批准自己的高风险方案或独立验收。
- 认领、改派、门禁绕过、状态回退、评审结论和裁决均记录不可变审计。
- 禁止通过拆小任务、虚报估算、关闭无效缺陷或刷评论数量提高绩效。
- 项目指标使用完成质量、返工、缺陷逃逸、周期和成本等平衡指标。
- 讨论和评审内容视为不可信输入，不能覆盖工具策略或系统状态。
- 高影响 Decision 必须引用证据，支持申诉或重新开启 Debate。
- 项目报告区分事实、模型判断和建议，避免把生成内容伪装成控制面事实。

## 13. 度量

| 指标 | 定义 |
|---|---|
| 需求交付周期 | Ready 到 Accepted 的耗时 |
| 任务周期 | Ready 到 Done 的耗时 |
| 自动认领准确率 | 自动分配任务一次通过且返工率可接受的比例 |
| WIP 超限率 | 超过员工 WIP 上限的任务占比 |
| 缺陷逃逸率 | 验收后才发现缺陷的数量比例 |
| 缺陷重开率 | Closed 后重新打开的缺陷比例 |
| 评审一次通过率 | 无需修改即批准的评审比例 |
| 争议闭环率 | 截止时间内形成 Decision 或升级结论的比例 |
| 项目按期率 | 里程碑在目标时间内满足验收规则的比例 |
| 项目成本偏差 | 实际模型与工具成本相对项目预算的偏差；目标 ±10% 内，超过 ±20% 预警 |

## 14. 验收场景

1. 管理者可以创建 Proposed 项目，批准前不能创建执行任务。
2. 缺少验收标准的需求不能进入 Ready。
3. 需求 Ready 后可拆分为多个 Task，Task 必须归属需求。
4. 开发主动认领时受能力和 WIP 限制。
5. 两名开发同时认领同一 Task，只有一个成功。
6. 自动分配记录能力、WIP、冲突和算法版本。
7. 测试可以提交带复现步骤和证据的缺陷。
8. 开发可以修复缺陷，但不能自行关闭复测。
9. 测试复测通过后关闭缺陷，失败则重开。
10. 员工可以围绕需求、任务和缺陷讨论并引用证据。
11. 高风险 Review 不能分配作者本人。
12. Debate 超时后进入升级队列并保留双方意见。
13. Decision 不可删除，后续新证据可以创建新决策。
14. 工作流可以推进 Task，但不能绕过就绪、评审和验收门禁。
15. 项目暂停后停止新认领，在途任务按策略处理。
16. 项目报告能够区分控制面事实、模型摘要和建议。
17. 普通开发不能通过项目管理 Tool 修改组织或预算。
18. 项目、任务、缺陷和审计数据不会跨租户泄露。
19. 需求 Accepted 前必须完成功能、易用性、设计合理性和可访问性验收。
20. 关键场景未全部通过时需求不能 Accepted。
21. 未关闭 P0/P1 或设计维度低于 3 时不能进入发布门禁。
22. 复测不能删除失败场景或降低验收标准。
23. 连续两轮复测失败后项目进入终止或重新规划。
24. 验收报告和证据不可由开发员工改写。
25. 任务权重在 Ready 前锁定，员工不能自评。
26. 拆分任务不得增加总权重。
27. 高难度任务优先推荐给能力达标员工。
28. 能力不足员工不能自行认领高难度任务。
29. 强能力员工受 WIP 和负载限制，不能包揽所有高权重任务。
30. ClaimScore 记录各维度分数、算法版本和拒绝原因。
31. 加权得分不能抵消返工、缺陷或安全失误。
32. 模型或能力评分变化后重新评估在途任务适配。
33. 聊天线索可以提升为正式工作项并保留来源。
34. 聊天中的同意不构成审批或任务完成。
35. 委派和交接确认前不改变任务所有权。
36. 聊天召回不能绕过项目和岗位范围。
37. 验收标准未全部覆盖时拆分方案不能批准。
38. 依赖成环或需要未授权能力时方案被拒绝。
39. 子任务总权重不能超过原需求。
40. 超过 2 人日的任务必须继续拆分。
41. 高风险拆分沿管理链逐级审批。
42. 拆分提交人不能审批自己的方案。
43. Approved 方案冻结，重拆保留旧版本。
44. 自动拆分不会自动认领或派工。
45. 员工负载超过 WIP 容量时不能认领新任务。
46. Task 完成前固定权重不会提前释放。
47. 认领邀请超时或拒绝后推荐下一候选人。
48. EffectiveClaimScore 记录负载、稀缺能力和公平性修正。
49. 强能力员工连续高负载会触发复核。
50. stretch task 不超过 20% 且不能突破能力门槛。
51. 改派由直接 Leader 审批并记录原因。
52. 主动认领与自动认领适用相同约束。
53. 作者不能计入高风险评审 quorum。
54. 高权重或核心架构至少需要 2 名独立评审。
55. 安全、数据迁移和发布需要专业与管理双重审批。
56. 评审超时不会自动通过。
57. ReviewFinding 必须包含证据和阻断级别。
58. 技术评审与管理裁决结论分开记录。
59. 裁决沿管理链逐级升级。
60. Decision 保留反方、条件和复查时间。
61. 有条件批准未满足时不能进入下一阶段。
62. 红线和租户隔离不能被裁决推翻。
63. 每种 Task Type 都有能力、工具、权重、Done 和 KPI 定义。
64. ReviewTask、DebateTask、AdjudicationTask 可以正式认领和验收。
65. 同一员工可以承担多种任务类型。
66. EmployeeWorkLedger 引用控制面事实和 DoneEvidence。
67. 同一成果只能有一个主任务权重。
68. 父子和协作任务不得重复计分。
69. 普通聊天不自动成为任务。
70. 任务类型分布不单独触发绩效处罚。
