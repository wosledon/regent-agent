# ADR-0014：控制面与 Agent 优先数据面分离

- 状态：Accepted
- 日期：2026-11-24
- 范围：员工身份、Runner、Gateway、Tool、任务、群聊与租户界面

## 背景

平台的公司业务由 Controller 和 Employee Agent 自主执行，Boss 只通过控制面设置章程、预算、模型和高影响决策。此前实现把人类 Cookie、租户 Header 和请求体演员用于部分数据面操作，导致群聊、消息和模型调用出现“人类代行 Agent”的身份偏差。

## 决策

- 控制面 `/v1` 是人类治理、观察和审计入口，不替代员工执行日常业务。
- 新增 Agent 数据面 `/agent/v1`，所有请求必须携带短期 Employee Assignment Token。
- Token 绑定 `tenant_id`、`employee_id`、`task_id`、`runner_id`、scopes、jti 和有效期；Gateway 使用独立 audience 验证。
- Runner 是 Agent 运行时，不是业务身份；领取任务时控制面签发 Token，Runner 仅在任务生命周期内持有和使用。
- Gateway 从 Token 获取租户、员工、任务和 scope，不信任请求体或任意 Header 中的身份字段。
- 群聊创建、成员管理、消息、回复、@ 和证据关联必须由 Agent 发起，并记录 `actor_type=Agent` 与员工身份。
- 控制面 UI 对 Agent 日常操作保持只读，仅保留章程、预算、模型、审批、审计和事后治理。
- Agent Tool 调用必须在执行层再次验证身份、能力、TaskGrant、taint、制品准入和审计。

## 结果

- 业务过程可归因到具体员工 Agent 和任务，控制面观察不改变权威状态。
- 跨租户、非成员和过期 Token 操作可以确定性拒绝。
- 本地开发使用显式开发 Token 或共享密钥文件，生产通过秘密系统注入同一签名密钥。
- 现有领域服务保持权威，Agent 面只增加协议、身份和授权边界，不复制业务状态。

## 未采用

- 让 Boss Cookie 直接创建群聊或发送员工消息：混淆人类治理与 Agent 执行。
- 使用共享服务 Token 代替员工身份：无法进行任务、能力和审计归因。
- 从请求体或 Header 信任租户与员工：破坏租户隔离和最小权限。
- 让 Runner 长期持有租户 JWT：违背部署身份与任务身份分离。

## 关联

- [01 基础契约与状态](../design/01-contracts-and-state.md)
- [03 模型网关、计量与预算](../design/03-model-metering-budget.md)
- [13 Agent 聊天与协作](../design/13-agent-chat-and-collaboration.md)
- [Agent 优先理念审计](../agent-first-audit.md)
