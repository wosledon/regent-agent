# 06 主控与动态组织

- 版本：v0.1（待评审）
- 范围：主控运行时、自建岗位、能力授权、组织规划、继任与治理
- 对应缺口：G-13、G-23，并替代固定角色谱系假设

## 1. 核心决策

1. 平台不内置固定公司角色，CEO、HRBP、CTO、产品经理等只是可选称呼，不是必需岗位。
2. 每个公司有一个持续存在的 **主控（Controller）**，它是编排运行时，不是预设岗位。
3. 公司启动后，主控根据章程为自己创建一个岗位，再以该岗位身份参与经营。
4. 岗位名称、职责、能力、模型档位、预算和汇报关系都是版本化数据。
5. 授权和调度基于能力，不基于岗位名称。
6. 主控可以合并、拆分、调整和关闭岗位，但必须遵守编制、预算、层级、独立性和升级审批规则。
7. Boss 是人类所有者，不是 Agent，不占编制；展示后缀不参与权限判断。
8. 员工 Agent 是独立运行时，不是主控的内部函数；业务通信经过控制面。
9. Sentinel 是纯程序算法，不调用 LLM、不是 Agent、不占编制、不做开放式经营决策。

## 2. 概念边界

| 概念 | 含义 | 是否占编制 |
|---|---|---|
| Boss / Owner | 人类租户所有者，可配置展示后缀 | 否 |
| Controller | 公司唯一的编排 Agent 运行时，负责读状态、规划、决策和推进闭环 | 否 |
| Position | 组织树节点与编制槽，可由主控或授权员工创建 | 岗位存在时占用一个槽，关闭后释放 |
| PositionAssignment | 员工与岗位的任职关系 | 不额外占编制 |
| OrganizationTree | 由岗位汇报关系生成的组织树 | 不单独占编制 |
| OrgManagementGrant | 主控委托给员工的组织管理权限 | 不额外占编制 |
| Employee Agent | 通过任职关系绑定岗位的独立 Agent 运行时 | 是 |
| Capability | 可授权的能力，如调研、招聘、技术评审、项目决策 | 不单独占编制 |
| Sentinel | 纯程序算法，只监控和触发预定义动作 | 否 |
| Role Title | 展示和沟通用途的名称，如 CEO、负责人、工程师 | 不参与权限判断 |

一个员工在同一时间只有一个主岗位任职关系，一个岗位最多一个主任职者；岗位可以空缺。一个岗位可以捆绑多个能力，需要“市场 + 产品”时，可以设一个综合岗位，不必强制拆成两个固定角色。

```text
Boss（人类所有者）
  │ Charter / 升级决策
  ▼
Controller（唯一主控 Agent）
  │ 组织规划 / 岗位与任务分配
  ▼
Employee Agents（独立员工 Agent）

Sentinel（纯算法）──事件──▶ Controller / Butler / Boss 通知
```

## 3. 核心实体

```text
OwnerProfile:
  tenant_id, user_id, display_name, title_suffix

SentinelEvent:
  id, tenant_id, rule_id, severity, metric_snapshot
  detected_at, action_taken, correlation_id

Controller:
  id, tenant_id, company_id
  state, active_position_id, active_employee_id
  cadence_policy_version, created_at

Position:
  id, tenant_id, company_id
  title, instructions, capabilities[]
  reports_to_position_id
  model_tier, budget, max_heavy_tasks
  slot_state, version, status

Employee:
  id, tenant_id
  runtime_kind: controller | worker
  controller_id?, state, assigned_runner

PositionAssignment:
  id, tenant_id, employee_id, position_id
  assignment_type: primary
  started_at, ended_at, assigned_by

OrgManagementGrant:
  id, tenant_id, employee_id, scope_position_id
  allowed_actions[], max_budget_change
  granted_by, granted_at, expires_at, revoked_at

CapabilityGrant:
  id, tenant_id, employee_id, capability
  scope, granted_by, granted_at, revoked_at

OrgChange:
  id, tenant_id, company_id
  change_type, before_snapshot, after_snapshot
  reason, evidence_ids, actor_id, created_at

Succession:
  id, tenant_id, company_id
  from_controller_id, to_employee_id
  state, reason, effective_at
```

## 4. 启动流程

### 4.1 主控自建岗位

1. 控制面创建公司和一个 `Controller(state=Unassigned)`。
2. 主控读取 Charter、平台策略和当前资源状态。
3. 主控使用 bootstrap scope 创建自己的 `Position`。
4. 主控选择岗位名称、职责、能力、模型档位、预算和汇报关系；`reports_to_position_id` 可为空。
5. 控制面校验编制、预算、层级、能力完整性和禁止权限。
6. 创建绑定主控的 `Employee(runtime_kind=controller)` 和 primary `PositionAssignment`，主控进入 `Active`。
7. 主控开始根据当前目标规划其他岗位，而不是按固定名单招聘。

主控可以把岗位命名为 CEO，也可以命名为总经理、总协调人或任何符合章程的名称。平台不读取该名称来决定权限。

### 4.2 初始组织规划

主控基于以下输入生成组织计划：

- Charter 的使命、能力边界、预算和治理规则。
- 当前项目阶段和待完成工作。
- 现有员工能力、模型成本和 Runner 容量。
- 市场调研、产品简报和验收证据。

输出可以是一个综合岗位、多个专业岗位或分阶段扩编。平台只要求关键阶段具备所需能力，不要求预先存在固定角色。

### 4.3 阶段能力覆盖

| 阶段 | 必须具备的能力 | 可以由谁承担 |
|---|---|---|
| 调研 | `research.execute`、`evidence.verify` | 任意被授予能力的岗位 |
| 产品定义 | `product.define`、`metrics.design` | 任意被授予能力的岗位 |
| 开发 | `engineering.deliver`、`quality.verify` | 任意被授予能力的岗位 |
| 独立验收 | `experience.validate` | 与实现者独立的岗位 |
| 发布运行 | `release.execute`、`runtime.operate` | 任意被授予能力的岗位 |
| 组织调整 | `org.design`、`position.create`、`employee.hire` | 主控或授权岗位 |

能力缺口必须通过新岗位、能力授权或任务转派解决，不能靠虚构岗位名称解决。

## 5. 能力与权限

### 5.1 能力分类

| 能力域 | 示例能力 |
|---|---|
| 经营 | `project.decide`、`priority.set`、`budget.propose` |
| 组织 | `org.design`、`position.create`、`employee.hire`、`employee.assess` |
| 流程 | `workflow.design`、`workflow.review`、`workflow.run` |
| 绩效 | `employee.assess`、`employee.terminate`、`performance.review`、`improvement.propose` |
| 调研 | `research.execute`、`evidence.verify` |
| 产品 | `product.define`、`metrics.design` |
| 工程 | `engineering.deliver`、`quality.verify`、`technical.review` |
| 项目 | `project.create`、`project.decide`、`requirement.create`、`requirement.review`、`task.assign`、`task.claim`、`defect.report`、`defect.retest`、`debate.adjudicate` |
| 通信 | `communication.participate`、`communication.request_override`、`communication.grant`、`position.label.manage` |
| 验收 | `experience.validate`、`release.approve_internal` |
| 运行 | `release.execute`、`runtime.operate`、`incident.respond` |
| 知识 | `memory.curate`、`journal.publish` |

### 5.2 授权规则

- 岗位 charter 声明所需能力，实际授权通过 `CapabilityGrant` 生效。
- `CapabilityGrant` 表示权限，`CapabilityAssessment` 表示能力水平，两者不得混用。
- 工具权限由能力映射，不能由模型自行在提示词中扩大。
- 主控的编排权限来自 `Controller` 身份，不来自“CEO”标题。
- 股东、升级审批和红线策略高于主控与员工权限。
- 能力撤销立即阻止新任务；在途任务在安全点结束或取消。

### 5.3 独立性约束

- 实现者不能成为自己变更的唯一批准者。
- 独立验收岗位不得与实现岗位共享同一 `employee_id`。
- 主控不能批准自己发起的高风险外部动作。
- 技术评审必须由具备 `technical.review` 的非作者执行，不要求岗位名为 CTO。
- 招聘评分者不能修改自己已提交的评分标准。

### 5.4 Tool 可见性与执行授权

员工工具分两层计算：

```text
authorized_tools = ToolRegistry.authorize(
  tenant_id,
  employee_id,
  position_id,
  capabilities,
  OrgManagementGrant[]
)

tool_profile = ToolRegistry.profile(
  authorized_tools,
  task_context,
  workflow_step
)
```

| 角色 | Primary 示例 | Auxiliary 示例 | 不可见/禁止 |
|---|---|---|---|
| Controller | 组织规划、建岗、任职、任务分配、项目决策 | 业务查询、报告 | Owner 管理、密钥读取 |
| 被委托组织管理者 | 授权子树内的组织工具 | 只读统计 | 其他子树、主控岗位、全局预算 |
| 架构师 | 架构设计、技术选型、技术评审 | 编码、测试、成果提交 | 组织管理、预算修改 |
| 普通开发 | 工作区、任务、消息、测试、成果 | 只读架构文档 | 组织管理、预算修改、升级审批 |
| 独立验收 | 浏览器、体验记录、验收报告 | 只读测试结果 | 组织管理、代码合并 |
| Butler | 只读状态、账本、公司志、升级列表 | 无 | 所有写工具 |

规则：

- Authorized Tools 是权限上限；Tool Profile 只决定当前默认披露什么。
- Primary 返回完整 Schema；Auxiliary 默认返回名称和简介，可展开或激活。
- Restricted 需额外授权或审批；Forbidden 不披露且拒绝执行。
- 每次 Tool Call 仍由 PEP 重新鉴权，不能只依赖发现阶段过滤。
- 高级岗位可以显式继承下级能力，例如架构师拥有编码能力，但默认主要披露架构设计和技术选型工具。
- 主控通过 `tool.capability.grant` 申请授权，控制面校验后更新 CapabilityGrant 或 OrgManagementGrant。
- Runner 收到授权、画像或撤销事件后刷新 Tool 列表。
- LLM 不得直接访问数据库、任意 HTTP 或未注册函数。

### 5.5 工作流治理

- 员工具备 `workflow.design` 才能创建 Draft 和 Proposal。
- 员工具备 `workflow.run` 才能运行被分配的工作流。
- Leader 必须同时具备 `workflow.review` 和对目标岗位子树的权限，才能采纳组织工作流。
- 个人工作流由作者维护；组织工作流沿作者岗位向上审查。
- 工作流不授予 Capability 或 Tool，只声明执行所需能力。
- 已采纳版本不可变，改进必须新版本。

### 5.6 自驱动与绩效权限

- 所有员工均可提交 `improvement.propose`。
- `employee.assess` 可生成绩效结论和辅导、冻结等动作。
- `org.design` 可执行调岗、降职等组织变更。
- 主控可以决定不录用候选人，但录用、再入职和移除订阅必须由 Boss 最终批准。
- `employee.terminate` 可在 Boss 批准后执行解雇，但必须经过独立复核和申诉窗口。
- 主控 Terminate 必须先完成继任。
- 员工不能修改 KPI 原始快照，也不能评估自己并执行动作。
- HRBP 只是可选岗位名称，实际权限来自能力。

### 5.7 项目管理权限

- 项目、需求、任务、缺陷和评审权限由相应 Capability 决定，不要求固定项目经理、开发或测试岗位。
- 管理角色创建项目、需求和任务；开发按能力与 WIP 认领任务；测试提交缺陷并复测关闭。
- 高风险评审作者与评审者必须分离，争议由具备 `debate.adjudicate` 的角色裁决。
- 项目成员身份不授予组织、预算或发布权限；相关工作必须使用对应授权工具。
- 高风险需求拆分沿管理链逐级审批，提交人不能自审，专业评审不替代最终管理审批。
- 技术评审与管理裁决分离；争议裁决沿管理链逐级处理，红线和租户隔离不可推翻。
- TaskTypeDefinition 不绑定岗位名称；员工可承担开发、测试、评审、对抗和裁决等多种任务，工作台账记录核心与辅助占比。
- 员工运行状态由算法维护；模型不可用上报直接 Leader，可靠性 KPI 必须区分员工、平台/模型和管理原因。
- BYOK 从 models.dev 预填，缺失价格由用户补充；cheap/mid/premium 由目录数据自动计算。

### 5.8 通信标签与穿透路由

- 岗位标签描述职责、专业入口和通信关系，不按岗位名称判断。
- 管理链负责资源、绩效和审批；专业链负责技术和领域问题升级。
- 专业路由沿祖先查找目标标签，可跳过纯管理岗位直接联系最近匹配者。
- 跨分支通信需要 CommunicationGrant 或项目覆盖，跨租户始终禁止。
- 标签和路由由具备 `org.design` 或 `position.label.manage` 能力的角色管理。
- 通信只扩大可联系对象，不扩大 Tool、预算、组织或审批权限。
- 任务权重在 Ready 前由非执行者锁定；高能力员工优先高难度任务，但仍受 WIP 和负载约束。
- WIP 容量为 3/5/8，按固定权重计量；改派由直接 Leader 审批，强能力员工不得垄断高权重任务。

### 5.9 提示词、技能与入职配置

- SystemPromptTemplate、PositionCharterVersion 和 SkillPackageVersion 分别版本化，已发布版本不可变。
- TaskRun 固定 PromptBundle；回滚重新激活旧版本并保留审计。
- 招聘使用中立白板，不安装生产岗位 Charter 和高级技能。
- 聘用自动安装平台基线、岗位 Charter 和 Starter Skills。
- 主控或岗位负责人按能力和任务渐进分配 SkillAssignment。
- 员工可创建个人技能草稿并提交改进，但公共化必须经过评测、审查和试点。
- 技能只声明所需能力，不自动授予 Capability 或 Tool。
- 模型和推理强度通过 EmployeeRuntimeBinding 绑定；任务成本预警沿管理链逐级评审，不主动直达 Controller。

## 6. 组织调整

### 6.1 调整类型

- 创建岗位、修改岗位、冻结岗位、关闭岗位。
- 合并岗位、拆分岗位、调整汇报关系。
- 授予或撤销能力、调整模型档位和预算。
- 创建、调岗和结束任职关系。
- 授予、修改和撤销组织管理授权。
- 招聘、调岗、冻结、开除和继任。

### 6.2 调整流程

1. 主控或授权员工提交 `OrgChange`。
2. 附理由、证据和前后快照。
3. 控制面校验编制、层级、组织树、预算、能力覆盖、管理授权范围和独立性。
4. 涉及高风险资源或预算时进入升级队列。
5. 通过后在一个事务中更新岗位、任职关系、能力授权、管理授权和审计。
6. 调度器处理在途任务和 Runner 分配。

### 6.3 约束

- 初期总编制不超过 20，层级不超过 4。
- 组织树只有一个根岗位、无环；移动岗位时重新计算子树深度。
- 岗位可以空缺，空缺和冻结岗位仍保留在组织树中。
- 一个员工最多一个 primary assignment，一个岗位最多一个主任职者。
- 关闭岗位前必须转移在途任务、结束任职、归档 JD 并释放槽位。
- 合并岗位不得绕过独立性约束。
- 主控修改自己的岗位与修改其他岗位使用同一审计和预算规则。
- 组织调整不得移除当前阶段最后一项关键能力，除非同时提供替代方案。

### 6.4 组织管理委托

主控可以创建 `OrgManagementGrant`，把指定岗位子树的管理权委托给员工 Agent。

授权字段：

- `scope_position_id`：可管理的子树根。
- `allowed_actions[]`：创建、任职、移动、冻结、关闭等动作。
- `max_budget_change`：单次或累计预算调整上限。
- `expires_at`：到期自动失效。
- `granted_by`：主控或更高权限管理者。

约束：

- 受委托员工只能操作授权子树，不能越界修改其他岗位。
- 不能修改 Controller 自建岗位、Owner 权限或全局编制/预算。
- 不能批准自己发起的高风险动作。
- 使用与主控相同的 `OrgChange` API 和审计。
- 撤销后立即拒绝新动作；在途变更按 row_version 和事务结果处理。
- 主控保留组织规划的最终责任。

## 7. 主控自建岗位的边界

主控可以：

- 选择自己的岗位名称和职责。
- 给自己授予政策允许的能力。
- 选择预算内模型档位。
- 调整自己的汇报关系和组织位置。

主控不可以：

- 通过自建岗位获得股东权限。
- 绕过编制、预算、升级审批或红线策略。
- 授予自己未定义的工具能力。
- 同时成为自己外部动作的审批者。
- 在没有继任者的情况下让公司失去主控。

## 8. 继任与主控状态

```text
Unassigned -> Active -> SuccessionPending -> Active
                  \-> Dormant
SuccessionPending -> Unassigned / Dormant
```

- `Unassigned`：主控存在，但没有有效自建岗位，只能执行 bootstrap 和组织修复任务。
- `Active`：主控绑定有效岗位，可正常编排。
- `SuccessionPending`：正在把主控身份转移给另一员工。
- `Dormant`：没有可用主控或继任者，公司暂停自主推进并通知股东。

继任步骤：

1. 主控提出继任计划和理由。
2. 目标员工具备 `project.decide` 与 `org.design` 能力。
3. 创建或调整目标员工岗位。
4. 在同一事务中切换 `active_employee_id` 和运行时绑定。
5. 旧主控转为普通员工、进入 `Unassigned` 或终止。
6. 写入审计和公司志。

## 9. 运行时与调度

- `ControllerRuntime` 是公司级循环，读取状态、生成组织计划、创建任务并跟踪结果。
- 自建岗位激活后，主控使用普通 `EmployeeRuntime` 执行岗位任务。
- 主控循环节奏由 cadence policy 管理，默认仍避免空转和高成本频繁唤醒。
- 主控任务与普通员工任务使用同一租约、幂等、预算和审计机制。
- 员工 Agent 拥有独立上下文和运行时，不是主控的内部函数；协作通过控制面任务和消息。
- Sentinel 只读取指标并生成事件，不调用 LLM；预定义安全动作由策略执行，开放式决策仍交给主控或 Boss。
- 主控进程崩溃后由控制面重新调度，不依赖本地状态。

## 10. 仪表盘与审计

仪表盘应分别显示：

- Boss 的 `display_name + title_suffix`，例如 `张三老板`。
- 主控运行时节点、状态和 cadence。
- 主控当前自建岗位及能力。
- 动态组织树、岗位版本、空缺/冻结状态和能力覆盖。
- 当前任职关系和历史调岗记录。
- 组织管理授权范围、到期时间和撤销状态。
- 最近组织变更、原因和证据。
- 关键能力缺口和继任状态。

岗位名称可以改变，但 `position_id`、`employee_id`、`controller_id` 和审计关联保持不变。

## 11. 招聘与考核

- 任意具备 `employee.hire` 能力的岗位都可以发起招聘。
- JD、题库和评分标准由被授权者创建；技术项由具备 `technical.review` 能力者评审。
- 录用、降配、重试和淘汰规则不依赖 HRBP 岗位是否存在。
- 考核由具备 `employee.assess` 能力的岗位执行，只认交付物和成本证据。
- 主控可以亲自承担多项职能，但不能绕过独立评审和验收。

### 11.1 Sentinel 边界

- Sentinel 由确定性规则、阈值和算法组成，不调用 LLM。
- 它不持有岗位、不占编制、不参与组织规划。
- 它可以记录指标、生成事件、告警、熔断和执行预定义安全动作。
- 它可以唤醒主控或通知管家/Boss，但不直接做主控的经营决策。
- 涉及开放式判断时，Sentinel 只提供结构化事件和证据。

## 12. 验收场景

1. 公司启动后不出现预设 CEO 岗位，主控先处于 `Unassigned`。
2. 主控可以给自己创建任意合规名称的岗位并进入 `Active`。
3. 岗位名称改为“总经理”后，权限和调度不发生变化。
4. 主控可以把市场和产品能力合并到一个岗位。
5. 缺少独立验收能力时，项目无法进入发布门禁。
6. 主控无法通过自建岗位获得股东审批权。
7. 关闭主控岗位前必须完成继任，否则公司进入 `Dormant` 并通知股东。
8. 组织调整超编、超预算或破坏层级时被拒绝。
9. 所有岗位和能力变更都有版本、理由、证据和审计。
10. Runner 崩溃后主控和员工均可从控制面恢复。
11. Boss 设置后缀后界面显示 `[用户名][后缀]`，权限和审批身份不变。
12. 员工 Agent 的上下文和任务独立于主控，不共享完整内部状态。
13. Sentinel 不调用模型、不占编制，不能创建岗位或批准升级。
14. Sentinel 触发的预定义安全动作有规则、证据和审计记录。
15. 岗位可以空缺并保留在组织树中，员工离职不删除岗位历史。
- 员工拥有全局唯一、不可变、永不复用的 `employee_no`；调岗和再入职保持身份连续。
16. 组织树出现环、多个根或超过 4 层时变更被拒绝。
17. 同一员工不能同时拥有两个 primary assignment，同一岗位不能有两个主任职者。
18. 调岗在同一事务中结束旧任职并创建新任职。
19. 受委托员工只能管理授权子树，不能修改主控岗位或突破预算。
20. 管理授权到期或撤销后，新组织变更立即被拒绝。
21. 普通开发的可见 Tool 列表不包含任何组织管理 Tool。
22. 主控授予员工组织管理 Tool 后，员工只能看到授权子树对应的 ToolManifest。
23. Tool 授权撤销后，员工发现列表和新的 Tool Call 都立即拒绝。
24. LLM 无法通过伪造 Tool 名称或参数绕过 PEP 和领域规则。
25. 员工只能沿自己岗位的上级路径提名工作流。
26. 无 `workflow.review` 能力的员工不能采纳组织工作流。
27. 工作流不能提升执行者的 Capability、Tool 或组织管理权限。
28. 组织范围岗位变化后，工作流可见成员按当前组织树重新计算。
29. 普通员工可以提交改进提案，但不能自行修改组织或权限。
30. 无 `employee.assess` 能力的员工不能生成绩效结论。
31. 解雇动作需要 `employee.terminate`、独立复核和审计。
32. 主控无继任者时不能被执行 Terminate。
33. 员工不能修改 KPI 原始快照或评估自己。
34. 绩效动作申诉期间暂停执行，由非原评估者复核。
35. 员工工号全局唯一且永不复用。
36. 调岗保留工号和员工运行时。
37. 再入职复用工号但创建新任职周期和凭据。
38. 主控不能自行录用、再入职或移除订阅。
39. 待驱逐队列不自动执行处罚。
40. Boss 缺席时录用和移除决定保持待定。
35. Tool Profile 不能扩大员工的 Authorized Tools。
36. 架构师默认披露架构工具，编码工具按需展开或激活。
37. 普通开发不能通过激活获得组织管理和预算工具。
38. 工作流步骤切换画像不改变权限上限。
39. Auxiliary 和 Restricted 激活均记录原因、审批和审计。
40. 高级岗位的权限继承必须显式声明，不按组织层级自动扩散。
41. 项目管理者按能力创建项目和需求，不依赖固定岗位名称。
42. 开发认领任务必须满足能力和 WIP 约束。
43. 测试提交缺陷并复测关闭，开发不能自行关闭。
44. 高风险评审作者与评审者必须分离。
45. 项目成员不自动获得组织或预算权限。
46. 争议裁决保留反方意见和证据。
47. 工作流不得绕过项目就绪和验收门禁。
48. 项目管理工具只能操作授权项目范围内的工作项。
49. 专业路由可跳过无技术标签的管理岗位。
50. 技术问题可以直达最近匹配的祖先专业岗位。
51. 项目范围问题仍沿管理链处理。
52. 跨分支通信需要显式授权。
53. 标签变化后旧路由失效。
54. 通信授权不扩大 Tool 或审批权限。
55. 普通员工不能自行添加标签或扩大联系人范围。
56. 路由穿透和撤销必须审计。
57. 权限 Grant 与能力评分 Assessment 分开管理。
58. 任务权重在认领前锁定，执行者不能自评。
59. 高能力员工优先高难度任务，但仍受负载限制。
60. 能力不足员工不能自行认领高难度任务。
61. 加权绩效不能覆盖安全和质量失误。
62. 能力评分变化后重新评估任务适配。
63. 招聘白板不安装生产岗位 Charter 和高级技能。
64. 聘用自动安装平台基线、岗位 Charter 和 Starter Skills。
65. 进阶技能由负责人按能力和任务渐进分配。
66. TaskRun 固定 PromptBundle，不中途切换版本。
67. 个人技能公共化必须经过评测、审查和试点。
68. 安装技能不自动授予权限。
69. 提示词和技能回滚保留旧版本与审计。
70. 员工不能修改平台 SystemPromptTemplate。
71. 模型和推理强度绑定员工，任务中不可临时更换。
72. 成本预警先上报直接 Leader，不主动直达 Controller。
73. 上游合理放行后可继续使用原员工和原模型。
74. 上游判断不匹配时可以调岗或拆分。
75. 管理链无法解决时才上报 Controller 或 Boss。
76. 成本预警评审不得绕过全局硬预算。
77. 高风险需求拆分先审批直接 Leader。
78. 拆分提交人不能审批自己的方案。
79. 专业评审不替代最终管理审批。
80. 管理链无法决定时才上报 Controller 或 Boss。
81. 自动拆分不自动授予能力或派工。
82. 拆分权重膨胀会被控制面拒绝。
83. WIP 按固定任务权重计量。
84. 员工超容量时不能认领新任务。
85. 强能力员工不能持续垄断高权重任务。
86. 认领邀请超时后不强派。
87. 改派由直接 Leader 审批。
88. stretch task 不突破能力门槛。
89. 高风险 quorum 不足不能批准。
90. 评审超时不自动通过。
91. 技术评审不替代最终管理裁决。
92. 争议沿管理链逐级裁决。
93. 裁决者不能是作者。
94. 红线和租户隔离不可被裁决推翻。
95. 任务类型不绑定岗位名称。
96. 员工可以承担多种任务类型。
97. 工作台账记录核心与辅助工作占比。
98. 同一成果只能有一个主任务权重。
99. 协作任务不得重复计分。
100. 任务类型分布不单独触发处罚。
101. 模型不可用由算法上报直接 Leader。
102. 员工不能自行修改运行状态。
103. 界面拟人状态保留底层原因码。
104. 平台或模型故障不扣员工 KPI。
105. 管理过载计入管理责任。
106. 反复异常先复核 WIP、工具和模型绑定。
107. 状态 KPI 不自动触发处罚。
108. 模型切换必须通过调岗或绑定变更。
109. BYOK 从 models.dev 预填已有字段。
110. 缺失价格由用户补充并记录来源。
111. models.dev 新增价格字段自动覆盖手工值。
112. 模型档位由目录数据自动计算。
113. 档位变化不自动改变员工绑定。
114. 未定价模型不会按 0 结算。
