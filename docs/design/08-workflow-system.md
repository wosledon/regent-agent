# 08 可复用工作流系统

- 版本：v0.1（待评审）
- 范围：工作流资产、动态组织范围、版本、提案审查、执行、度量与改进
- 对应缺口：G-25、G-28

## 1. 定位

工作流是可版本化的组织流程资产，用于沉淀重复出现的协作模式，例如：

```text
产品定义 → 开发 → 测试 → 独立验收 → 发布决策
```

它不是岗位、Tool 或一次性提示词：

- 岗位定义谁负责。
- Tool 定义可以做什么。
- 工作流定义按什么顺序、用什么证据完成一件事。

员工可以归纳个人工作流，也可以沿组织架构向上提名；上级 Leader 决定是否纳入组织制度。

## 2. 核心不变量

1. 工作流范围只有个人和组织两种。
2. 组织范围绑定当前组织树上的 `scope_position_id`，不写死团队、部门、公司等级别。
3. 员工只能沿自己所在岗位的上级路径提名，不能跨分支扩散。
4. 个人草稿由作者维护；组织级工作流必须经过 Leader 审查。
5. 已采纳版本不可变，改进必须创建新版本。
6. WorkflowRun 固定使用一个版本，不中途切换定义。
7. 工作流只能声明所需能力和 Tool，不能提升执行者权限。
8. 自动归纳只生成候选，不自动成为组织制度。
9. 工作流定义、审查和执行均按租户隔离。
10. 跨租户共享默认关闭，SaaS 模板库必须匿名化并单独审核。
11. 工作流步骤可以创建或推进项目工作项，但不能绕过需求就绪、评审和验收门禁，详见 [11 项目管理中心](./11-project-management.md)。
12. 工作流完成必须满足通用 Done 和分类门禁，不能由执行者直接关闭。

## 3. 核心实体

```text
WorkflowDefinition:
  id, tenant_id, name, description
  scope_type: personal | organization
  owner_employee_id?, scope_position_id?
  status, current_adopted_version_id

WorkflowVersion:
  id, workflow_id, version
  definition_json, schema_version
  author_id, created_at, change_summary
  status: draft | proposed | pilot | adopted | deprecated

WorkflowProposal:
  id, workflow_id, from_version_id, to_version_id
  proposed_scope_type, scope_position_id?
  reason, evidence_run_ids, expected_benefit
  proposed_by, created_at

WorkflowReview:
  id, proposal_id, reviewer_employee_id
  decision: adopt | adopt_with_changes | pilot | request_changes | reject
  comment, decided_at

WorkflowRun:
  id, tenant_id, workflow_id, version_id
  project_id?, initiator_employee_id
  state, input_artifacts, output_artifacts
  started_at, completed_at, cost, metrics

WorkflowStepRun:
  id, run_id, step_id
  assigned_employee_id?, state
  task_ids, tool_invocation_ids, artifact_ids
  attempt, started_at, completed_at
```

## 4. 动态组织范围

### 4.1 个人范围

- `scope_type=personal`，范围属于 `owner_employee_id`。
- 作者可以创建、修改、废弃和运行。
- 其他员工默认不可见。
- 作者可以把自己的工作流作为 Proposal 提名到上级岗位范围。

### 4.2 组织范围

- `scope_type=organization`，范围绑定 `scope_position_id`。
- 可见成员由当前组织树中该岗位及其子树决定。
- 审批人由当前组织树向上查找具备 `workflow.review` 能力的岗位。
- 公司范围就是根岗位范围，不是额外的固定层级。

示例：

```text
Position A（根）
└─ Position B
   └─ Position C
      └─ Position D（作者）
```

Position D 的员工可以提名到：

```text
D 子树 → C 子树 → B 子树 → A 根范围
```

不能提名到兄弟岗位或其他分支。

### 4.3 组织变化

- 工作流始终引用稳定的 `position_id`。
- 岗位移动后，生效范围按当前组织树重新计算。
- 采纳记录保存当时的组织树版本和路径快照，用于审计。
- 范围岗位被关闭或合并时，工作流进入 `scope_orphaned`，停止新实例，直到重新指定范围或废弃。
- 不得在岗位消失时自动扩大到公司范围。

## 5. 状态与版本

### 5.1 版本状态

```text
draft → proposed → in_review → pilot → adopted
                         │  ├─ request_changes → draft
                         │  └─ reject
adopted → deprecated
```

- Draft：作者可修改。
- Proposed：提交审查，定义冻结。
- InReview：Leader 审查中。
- Pilot：限定项目或时间试点。
- Adopted：组织正式版本，不可修改。
- Deprecated：停止新实例，历史保留。

### 5.2 运行状态

```text
Pending → Running → Succeeded
              ├→ Failed
              ├→ WaitingApproval → Running
              └→ Cancelled
```

- Run 创建时固定 `version_id`。
- 步骤失败按步骤策略重试或终止。
- 高风险步骤进入升级队列，不直接执行。
- 取消不删除已产生的 Artifact 和审计。

## 6. 工作流步骤

```text
WorkflowStep:
  id, name, sequence
  required_capabilities[]
  assigned_position_id?
  input_artifacts[]
  output_artifacts[]
  allowed_tools[]
  success_criteria[]
  approval_gate?
  retry_policy, timeout
  on_failure
```

约束：

- 步骤不能直接执行数据库操作。
- `allowed_tools` 必须是 Tool Registry 中存在的 Tool。
- 执行员工仍受 Tool 可见性和 PEP 约束。
- 步骤所需能力不足时等待分派，不能自动扩权。
- 独立验收步骤必须绑定与实现者不同的员工。

## 7. 典型工作流

| 步骤 | 能力 | 输出 | 门禁 |
|---|---|---|---|
| 产品定义 | `product.define` | 产品简报、成功指标 | 指标完整 |
| 开发 | `engineering.deliver` | 代码、构建产物 | CI 通过 |
| 测试 | `quality.verify` | 测试报告、缺陷清单 | 阻断缺陷清零 |
| 独立验收 | `experience.validate` | 体验报告 | 验收门槛通过 |
| 发布决策 | `project.decide` | go/no-go 记录 | 证据齐全 |

这个流程可以由任意具备 `workflow.design` 与产品能力的员工设计，不要求固定“项目经理”岗位。

## 8. 捕获与归纳

### 8.1 捕获来源

- 成功完成的任务序列。
- Tool 调用与结果。
- Artifact 和审批记录。
- 周期、成本、失败率与人工介入。

### 8.2 捕获限制

- 不保存原始系统提示词。
- 不保存密钥、完整 Authorization 头或未脱敏参数。
- 外部内容只保存引用和摘要。
- 捕获结果标记为 `candidate`，不能直接 Adopted。

### 8.3 归纳流程

1. 员工完成一次有效工作。
2. 系统生成候选步骤、依赖和门禁。
3. 员工编辑为个人 Draft，或直接创建 Proposal。
4. 系统生成与现有版本的差异。
5. 员工选择个人使用或向上提名。

## 9. 提案与审查

### 9.1 提案内容

- 目标范围与 `scope_position_id`。
- 变更摘要和版本差异。
- 成功运行证据。
- 预期周期、成本或质量收益。
- 所需能力、Tool 和风险。
- 建议试点范围。

### 9.2 审查路由

1. 从作者当前岗位开始向上查找。
2. 找到第一个具备 `workflow.review` 且对目标范围有权限的岗位。
3. 权限不足时继续向上路由。
4. 根岗位仍无权限时通知 Controller 或 Boss，不自动采纳。

### 9.3 审查决策

| 决策 | 结果 |
|---|---|
| Adopt | 直接成为目标范围正式版本 |
| Adopt with changes | 生成修订版本后再采纳 |
| Pilot | 限定项目、时间或人数试点 |
| Request changes | 退回作者补充证据或修改 |
| Reject | 保留记录，不进入组织目录 |

### 9.4 审查标准

- 是否有成功运行证据。
- 是否改善周期、成本、失败率或人工介入。
- 是否满足独立性、审批和合规要求。
- 所需岗位和能力是否可用。
- Tool 是否仍存在且合规。
- 是否包含敏感数据或过度特定假设。
- 是否可能形成单点故障或权限集中。

## 10. 执行模型

```text
WorkflowRun
  → 解析固定版本
  → 创建步骤任务
  → 分派给具备能力的岗位
  → 员工通过授权 Tool 执行
  → 提交 Artifact 和证据
  → 门禁通过后进入下一步
  → 汇总指标和审计
```

- 控制面负责任务、状态、租约和审计。
- MAF Workflows 可作为步骤编排引擎，但不是权限或事实来源。
- 工作流不能直接调用未授权 Tool。
- 步骤重试使用幂等键，不重复产生领域副作用。
- 运行超时、成本超限或能力缺失时暂停并告警。

## 11. 度量与改进

每次 WorkflowRun 记录：

- 总周期和步骤耗时。
- 模型与工具成本。
- 成功率、重试率、失败步骤。
- 人工审批和介入次数。
- Artifact 与验收结果。
- 使用的 workflow version。

改进流程：

1. 从运行指标识别瓶颈。
2. 员工或系统创建新版本提案。
3. 附上旧版本对比数据。
4. Leader 审查并决定试点或采纳。
5. 新版本逐步替代旧版本，旧版本标记 Deprecated。

## 12. Tool 与 API

```text
workflow.capture
workflow.create
workflow.version
workflow.propose
workflow.review
workflow.adopt
workflow.reject
workflow.instantiate
workflow.cancel
workflow.report
```

API：

- `POST /workflows`
- `POST /workflows/{id}/versions`
- `POST /workflows/{id}/proposals`
- `POST /workflow-proposals/{id}/review`
- `POST /workflows/{id}/runs`
- `GET /workflow-runs/{id}`

所有写操作幂等并审计。工具可见性仍按角色过滤：

- 普通开发只能运行被分配的工作流。
- 工作流设计者可以创建 Draft 和 Proposal。
- Leader 才能采纳组织范围工作流。
- Controller 可以管理根范围目录，但不能绕过高风险审批。

## 13. 安全与隔离

- 工作流定义、提案、运行和证据必须带 `tenant_id`。
- 个人工作流不得被其他员工检索。
- 组织工作流只对当前范围成员可见。
- 工作流不能授予 Capability、OrgManagementGrant 或 Tool 权限。
- 自动捕获必须脱敏，防止把提示注入或密钥写入制度资产。
- 范围岗位缺失时停止扩散，不自动提升可见性。
- 跨租户模板共享必须匿名化、去标识并单独审核。

## 14. 验收场景

1. 员工可以创建个人 Draft 并自行运行。
2. 个人工作流只能沿作者岗位的上级路径提名。
3. 向兄弟岗位范围提名会被拒绝。
4. 无 `workflow.review` 能力的员工不能采纳组织工作流。
5. Leader 可以采纳、修改、试点、退回或拒绝。
6. Adopted 版本不可修改，改进生成新版本。
7. WorkflowRun 固定版本，不受后续版本影响。
8. 工作流步骤不能使用执行员工不可见的 Tool。
9. 工作流不能提升员工 Capability 或组织管理权限。
10. 范围岗位移动后按当前组织树重新计算可见成员。
11. 范围岗位关闭后工作流进入 `scope_orphaned`，停止新实例。
12. 自动捕获不包含密钥、原始提示词和未脱敏参数。
13. 运行指标可以驱动新版本提案和对比审查。
14. 跨租户工作流和运行记录不可见。
