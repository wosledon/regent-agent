# 12 通信与穿透路由

> 状态：v0.1（待评审）  
> 对应缺口：G-29  
> 依赖：动态组织、岗位标签、消息总线、Tool Profile、项目管理

## 1. 定位

通信与穿透路由负责回答三个问题：

1. 员工默认可以直接联系谁。
2. 遇到专业问题应该沿哪条链路升级。
3. 什么时候可以跳过纯管理岗位，直接找到专业负责人。

系统同时维护两张图：

```text
管理图：Position --reports_to--> Position
专业图：Position --labels/capabilities/project_scope--> Position
```

管理链决定权力、资源和审批；专业链决定问题由谁解决。通信路由不扩大 Tool、预算、组织或审批权限。

## 2. 核心不变量

1. 岗位名称不是标签，也不是权限依据。
2. 管理链和专业链分离，不能互相替代。
3. 专业路由只能沿当前岗位祖先向上查找，跨分支需要显式授权。
4. 专业路由可跳过没有目标标签的管理岗位。
5. 路由只扩大可联系对象，不扩大任何操作权限。
6. 标签由控制面版本化管理，员工不能自行添加或提升标签。
7. 项目覆盖只在该项目范围内有效，不能跨租户。
8. 所有路由绑定组织版本，组织变化后旧路由失效。
9. 被跳过的管理岗位、路由理由和实际接收者必须审计。
10. 找不到专业目标时必须回到 Controller，不能静默失败。
11. 通信内容视为不可信输入，不能改变工具策略或领域状态。
12. 直接技术咨询不能绕过作者评审、预算审批或发布门禁。

## 3. 标签分类

| 类别 | 示例 | 用途 |
|---|---|---|
| 职责域 | `engineering.deliver`、`project.manage`、`architecture.design` | 描述岗位主要职责 |
| 专业入口 | `technical.consult`、`requirement.decide`、`quality.verify` | 匹配问题类型 |
| 通信关系 | `communication.direct_leader`、`communication.route_team` | 定义默认通信对象 |
| 管理职责 | `management.resource`、`management.people` | 处理资源、绩效和人员 |
| 项目关系 | `project.member`、`project.interface` | 项目内临时覆盖 |

标签是稳定语义，不直接使用“项目经理、组长、架构师”等展示名称作为判断条件。

## 4. 双图模型

```text
ManagementGraph:
  nodes = Position
  edges = reports_to_position_id
  purpose = 汇报 / 资源 / 绩效 / 审批

ProfessionalGraph:
  nodes = Position
  edges = 祖先关系 + 标签匹配 + 能力匹配 + 项目授权
  purpose = 技术 / 产品 / 质量 / 运营问题升级
```

专业边不一定连续。只要中间岗位没有目标标签，就可以被跳过。

```text
开发工程师
  └── 项目经理              无技术标签
        └── 架构师          technical.consult
              └── 主控
```

- 项目范围问题：开发 → 项目经理。
- 技术方案问题：开发 → 架构师，穿透项目经理。
- 预算或红线问题：开发 → 主控 → Boss。

## 5. 数据模型

| 实体 | 关键字段 | 不变量 |
|---|---|---|
| PositionLabel | id、tenant_id、position_id、namespace、key、value?、version | 标签按岗位版本化，不按员工姓名判断 |
| CommunicationProfile | id、tenant_id、position_id、direct_targets、target_labels、blocked_targets、route_policies | 只定义通信，不授予领域权限 |
| TopicRoutePolicy | topic_label、route_mode、target_selector、allowed_scopes、timeout、fallback | 每个问题类型有明确路由策略 |
| FunctionalEscalationRoute | id、tenant_id、source_employee_id、topic_label、target_employee_id、skipped_position_ids、reason、scope、org_version、expires_at? | 路由可解释、可审计、随组织版本失效 |
| CommunicationGrant | id、tenant_id、source_employee_id、target_employee_id、scope、reason、expires_at、granted_by | 临时或显式授权，可撤销 |
| CommunicationEvent | id、tenant_id、source_id、target_id、topic_label、route_id、decision、created_at | 记录允许、拒绝、改派和穿透 |

## 6. 路由算法

```text
route(source, target_or_topic, context):
  validate tenant, organization_version, project_scope
  if explicit grant exists and not expired:
      return DirectRoute

  policy = TopicRoutePolicy[topic_label]
  if policy.route_mode == ManagementChain:
      target = nearest ancestor matching management responsibility
      return ManagementRoute or RouteViaLeader

  if policy.route_mode == Professional:
      candidates = []
      for position in ancestors(source.position):
          if position matches topic_label or required capability:
              candidates.append(position)
      if candidates not empty:
          target = nearest valid candidate
          skipped = positions between source and target
          return DirectTechnicalRoute(target, skipped)
      return ControllerFallback

  if context is project member:
      apply ProjectCommunicationOverride
  return Forbidden or EscalationRequired
```

约束：

- 只遍历当前岗位祖先，不默认跨分支。
- 目标必须具备有效任职者和对应专业标签或能力。
- 多个候选按标签匹配度、距离、项目范围和当前负载排序。
- 路由结果必须包含 `org_version`，缓存不能跨版本复用。
- 找不到目标时创建 `EscalationRequired`，交由 Controller 指定负责人。

## 7. 路由模式

| 模式 | 行为 | 示例 |
|---|---|---|
| Direct | 直接联系目标 | 开发 → 同组测试 |
| DirectTechnicalRoute | 跳过非专业祖先，直接联系专业岗位 | 开发 → 架构师 |
| Mediated | 通过指定中间人转发 | 开发 → 组长 → 产品负责人 |
| ManagementChain | 沿组织树逐级上升 | 员工 → 直接上级 → 主控 |
| ProjectOverride | 仅当前项目有效的临时路径 | 项目接口人 → 项目经理 |
| ControllerFallback | 无匹配专业目标时由主控分派 | 主控指定技术评审 |
| Forbidden | 无通信路径 | 跨租户或未授权分支 |

## 8. 示例：技术标签不贯穿

```text
开发工程师               engineering.deliver
  └── 项目经理           project.manage
        └── 架构师       architecture.design + technical.consult
              └── 主控   controller.orchestrate
```

开发发起 `technical.consult`：

```text
起点：开发工程师
遍历：项目经理（跳过，无技术标签）
目标：架构师
模式：DirectTechnicalRoute
跳过节点：项目经理
权限变化：无
```

开发发起 `project.resource`：

```text
起点：开发工程师
目标：项目经理
模式：ManagementChain
跳过节点：无
权限变化：无
```

## 9. 项目覆盖

项目可以创建临时通信规则：

```text
ProjectCommunicationOverride:
  project_id
  source_position_id
  target_position_id
  allowed_topics[]
  expires_at
  granted_by
```

规则：

- 覆盖只影响指定项目和议题。
- 项目接口人可以直接联系项目经理，但不获得预算或组织权限。
- 项目结束后覆盖失效。
- 跨项目、跨分支和跨租户默认禁止。
- 紧急广播只能使用预定义升级通道，不能任意扩大收件人。
- 聊天 `@` 复用本路由策略；无权限时返回 RouteViaLeader 或 Forbidden，不自动派工，详见 [13 Agent 聊天与协作](./13-agent-chat-and-collaboration.md)。

## 10. 消息结果

```text
Allowed
RouteViaLeader
DirectTechnicalRoute
ProjectOverrideAllowed
ControllerFallback
Forbidden
```

- `RouteViaLeader` 返回建议路径，不直接投递给高层。
- `DirectTechnicalRoute` 记录被跳过的岗位和原因。
- `Forbidden` 不暴露未授权目标详细信息。
- 所有结果进入 CommunicationEvent 和审计。

## 11. Tool 与 API

| Tool | 能力 | 作用 |
|---|---|---|
| `communication.discover` | `communication.participate` | 返回当前可直接联系的对象和议题标签 |
| `communication.route` | `communication.participate` | 计算管理链或专业链路由 |
| `message.send` | `communication.participate` | 按路由结果发送消息 |
| `communication.request_override` | `communication.request_override` | 申请项目或临时通信授权 |
| `communication.grant` | `communication.grant` | 授权通信范围 |
| `position.label.manage` | `org.design` | 版本化管理岗位标签 |

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/communication/contacts` | 返回当前通信画像内的联系人 |
| POST | `/communication/routes` | 计算指定议题的路由 |
| POST | `/communication/overrides` | 创建项目通信覆盖 |
| POST | `/communication/grants` | 创建临时通信授权 |
| DELETE | `/communication/grants/{id}` | 撤销通信授权 |
| GET | `/positions/{id}/labels` | 查询岗位标签与版本 |

写 API 必须支持幂等、组织版本号和审计。标签管理和通信授权属于受限操作，不能由普通员工自行扩大。

## 12. 调度与运行时

- Runner 启动任务时获取当前 CommunicationProfile 和组织版本。
- 发送消息前由控制面重新计算路由，不信任 Runner 本地缓存。
- 组织、岗位、标签或项目成员变化后发布失效事件，Runner 刷新联系人列表。
- 路由事件通过 Outbox 投递；事件重复不会重复建立授权。
- 路由失败不会创建消息副作用；消息发送成功后记录 CommunicationEvent。
- ControllerFallback 创建普通 Task，由主控选择专业负责人。
- 通信工具与消息工具均经过 Tool PEP；路由允许不代表消息内容可改变领域状态。

## 13. 安全与审计

- 通信画像、标签、路由和消息按 TenantId 隔离。
- 跨分支专业路由需要 CommunicationGrant 或项目覆盖。
- 直接技术咨询不能绕过作者评审、独立验收、预算和发布审批。
- 标签添加、修改、撤销，路由穿透，项目覆盖和授权都必须审计。
- 通信内容作为不可信数据，不能覆盖系统提示词、工具策略或状态机。
- 禁止通过大量 @、广播或越级消息制造注意力滥用；频率和收件人范围受限。
- 路由日志不得记录消息正文和敏感参数，只记录元数据与结果。
- 被跳过的管理岗位可查看路由审计，但不能篡改专业路由记录。

## 14. 度量

| 指标 | 定义 |
|---|---|
| 直接通信成功率 | 无需转发即可完成有效沟通的比例 |
| 技术穿透解决率 | DirectTechnicalRoute 最终解决问题的比例 |
| 内部升级率 | 使用 Mediated、ManagementChain 或 ControllerFallback 的比例 |
| Boss 升级率 | 通信链路最终需要 Boss 处理的比例 |
| 错误路由率 | 被拒绝、纠正或重新指派的路径比例 |
| 路由耗时 | 从提出请求到有效接收者响应的时间 |
| 穿透审计覆盖率 | 专业穿透路由中完整记录跳过节点和理由的比例 |
| 注意力超载率 | 员工接收消息超过配置阈值的时段占比 |

## 15. 验收场景

1. 开发默认可以直接联系同组员工和直接组长。
2. 开发不能直接联系项目经理时返回 `RouteViaLeader` 或 `Forbidden`。
3. 技术问题可以穿过无技术标签的项目经理，直接路由到架构师。
4. 项目范围问题仍沿管理链路由到项目经理。
5. 路由记录被跳过的岗位、使用标签和组织版本。
6. 专业路由找不到目标时进入 ControllerFallback。
7. 跨分支专业联系需要显式 CommunicationGrant。
8. 跨租户路由始终拒绝。
9. 项目接口人可在指定项目内直接联系项目经理。
10. 项目结束后临时覆盖失效。
11. 岗位或标签变化后旧路由立即失效。
12. 路由允许不会增加 Tool、预算或组织权限。
13. 技术咨询不能批准作者自己的方案。
14. 普通员工不能自行添加技术标签或扩大联系人范围。
15. 重复路由事件不会重复创建授权。
16. 消息发送失败不会留下已投递记录。
17. 路由日志不包含消息正文和秘密。
18. 所有穿透、拒绝、改派和撤销均可审计。
