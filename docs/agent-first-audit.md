# Agent 优先理念审计

- 审计日期：2026-11-24
- 范围：PRD、技术设计、01/03/06/11/13/18/20/42/48、ADR 与当前实现
- 结论：设计文档已经明确“员工 Agent 是业务执行与协作的一等操作者”，但当前实现尚未完全贯穿；主要偏差集中在身份、群聊写操作和 Gateway 数据面。

## 1. 理念基线

- Boss 是人类所有者，只通过控制面设置章程、预算、模型和高影响决策。
- Controller 与 Employee Agent 是公司业务的执行者；Runner 只是 Agent 的运行时，不是业务身份。
- 除控制面的人类观察与治理入口外，模型调用、任务执行、Tool、消息、群组、记忆和协作都应以员工 Agent 身份发起。
- Agent 身份必须绑定租户、员工、任务、能力和有效期，不能从请求体或任意 Header 伪造。
- 控制面可以观察和审计 Agent 操作，但不应替代员工成为日常群聊、消息和业务操作的发送者。

## 2. 当前偏差矩阵

| 领域 | 设计要求 | 当前实现 | 偏差 |
|---|---|---|---|
| 员工身份 | Employee assignment token，单租户/单员工/单任务 | 只有人类 Cookie；Runner 使用 Header 指定租户 | **未实现 Agent 身份** |
| 任务领取 | 领取任务时签发 assignment token | `TaskClaim` 只有租约，无员工/租户/Token | **未实现** |
| Gateway | 验证 Agent JWT 的 tenant/employee/task/scope | local 匿名，remote 使用共享服务 Token，租户取 Header | **不是 Agent 原生** |
| 群聊创建 | Agent 通过 Tool/API 建群 | `/v1/conversations` 使用人类 Cookie 和 `CurrentUser` | **人类成为操作者** |
| 消息发送 | Agent 是发送者，成员与权限过滤 | `/v1/conversations/{id}/messages` 使用人类 Cookie | **人类成为发送者** |
| 群成员 | Agent 管理自己有权管理的群成员 | 控制面 API 直接添加成员 | **缺少 Agent 身份与能力检查** |
| 聊天记录 | 记录 Agent 员工身份与审计 | 仅记录 `Sender` 字符串 | **身份不可验证** |
| 控制面 UI | 观察、治理、审计 | 提供建群、发消息、加成员写操作 | **控制面越界代行 Agent 日常操作** |
| 组织/项目/记忆 | Agent 通过 Tool Call 执行 | 多数 `/v1` 写接口仍是人类 Cookie | **部分仍是人类操作入口** |
| 审计 | 谁、哪个 Agent、哪个任务、结果 | 部分事件记录人类 Owner | **归因错误** |

## 3. 修正原则

1. 新增独立 `/agent/v1` 数据面，使用短期 Employee Assignment Token。
2. 控制面 `/v1` 保留人类治理与只读观察；日常 Agent 写操作迁入 `/agent/v1`。
3. 任务领取时签发 Token，Runner 只持有并在任务生命周期内使用。
4. Gateway 从 Token 获取租户、员工、任务和 scope，忽略请求体/Header 中的身份字段。
5. 群聊、消息、成员和证据记录 `sender_employee_id`、`actor_type=Agent` 与任务关联。
6. 控制面可视化继续展示 Agent 创建的群与消息，但不再提供代发、代建群和代加成员。
7. 本地开发模式可以保留显式测试 Token，但不得把匿名 Header 身份表述为生产 Agent 身份。

## 4. 完成标准

- Agent Token 的签发、校验、过期、租户/员工/任务绑定有自动化测试。
- Runner 领取任务后使用 Token 调用 Gateway，Gateway 拒绝缺失或伪造身份。
- Agent 可创建群、加成员、发消息；非成员和跨租户操作被拒绝。
- 控制面只读展示 Agent 群聊过程，审计归因到员工 Agent。
- ADR、详细设计、OpenAPI、实现覆盖和 E2E 同步更新。
