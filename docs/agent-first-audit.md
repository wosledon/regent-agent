# Agent 优先理念审计

- 审计日期：2026-11-24
- 范围：PRD、技术设计、01/03/06/11/13/18/20/42/48、ADR 与当前实现
- 结论：员工 Agent 身份、Gateway 数据面、群聊协作和核心业务 Tool 面已贯穿 Agent 优先理念。

## 1. 理念基线

- Boss 是人类所有者，只通过控制面设置章程、预算、模型和高影响决策。
- Controller 与 Employee Agent 是公司业务的执行者；Runner 只是 Agent 的运行时，不是业务身份。
- 除控制面的人类观察与治理入口外，模型调用、任务执行、Tool、消息、群组、记忆和协作都应以员工 Agent 身份发起。
- Agent 身份必须绑定租户、员工、任务、能力和有效期，不能从请求体或任意 Header 伪造。
- 控制面可以观察和审计 Agent 操作，但不应替代员工成为日常群聊、消息和业务操作的发送者。

## 2. 偏差与修正矩阵

| 领域 | 原偏差 | 当前修正 | 结论 |
|---|---|---|---|
| 员工身份 | 只有人类 Cookie；Runner 使用 Header 指定租户 | `RegentAgent.AgentIdentity` 签发短期 JWT，绑定租户、员工、任务、Runner、scope、jti 和过期时间 | **已修正** |
| 任务领取 | `TaskClaim` 只有租约 | 领取任务时创建/绑定员工并返回 Assignment Token | **已修正** |
| Gateway | local 匿名，remote 共享 Token，租户取 Header | 所有 `/v1` 模型调用验证 Agent JWT 和 `infer:employee`，身份只来自 Token | **已修正** |
| 群聊创建 | `/v1/conversations` 使用人类 Cookie | `/agent/v1/conversations` 使用 Agent Token，创建者必须是成员 | **已修正** |
| 消息发送 | 人类 Cookie 发送 | `/agent/v1/conversations/{id}/messages` 使用 Agent Token，强制成员校验 | **已修正** |
| 群成员 | 控制面直接添加 | Agent 面添加，校验租户成员和操作者群成员身份 | **已修正** |
| 聊天记录 | 仅 `Sender` 字符串 | 增加 `SenderEmployeeId`、`ActorType`、创建者身份和任务审计 | **已修正** |
| 控制面 UI | 提供建群、发消息、加成员 | 改为只读观察，移除写操作 | **已修正** |
| 组织/项目/记忆 | 多数 `/v1` 写接口仍是人类 Cookie | `/agent/v1/tools` 覆盖任务、记忆、工作流、项目、组织、改进、招聘、绩效、评测、Agent 演进和任务治理 | **已修正** |
| 审计 | 部分事件记录人类 Owner | 群聊和任务事件记录 Agent、员工、任务和 actor_type | **已修正** |

## 3. 当前实现

- `/agent/v1` 提供员工身份、任务检查点/完成、运行时上下文、Tool loop、Tool 发现/调用、群聊和协作委派/交接/求助/打断 API。
- Tool 目录覆盖聊天、任务、协作、记忆、工作流、项目、组织、改进、招聘、绩效、评测、Agent 演进和任务治理。
- 委派保留委派者最终责任；交接在接收者确认前不转移所有权，确认时原子更新负责人和租约；P0/P1 打断在安全点保存检查点并使旧租约失效，恢复时轮换新租约。
- Gateway 拒绝缺失、过期、错误 audience 和错误 scope 的 Token，并按租户记录预算与用量。
- ControlPlane 与 Gateway 共享本地密钥文件；Compose 使用独立密钥卷。
- 控制面 `/v1/conversations` 仅保留 GET，Tenant Dashboard 消息与协作页面只读。
- Agent 创建的群、消息和审计事件进入组织协作可视化与追溯时间线。

## 4. 当前结论

- 员工 Agent 身份、Gateway、群聊和核心业务 Tool 面已贯穿 ADR-0014。
- 控制面保留章程、预算、模型、解雇、审批、审计和事后治理；日常业务操作由 Agent 发起。
- Tool 调用使用 Token scope 作为可信 TaskGrant，执行层再次校验能力、taint、注入、签名、digest、SBOM 和成员/租户边界。
- 所有 Tool 结果可幂等重放，调用、拒绝和失败均写入 Agent 审计。

## 5. 完成标准

- Agent Token 的签发、校验、过期、租户/员工/任务绑定有自动化测试。
- Runner 领取任务后使用 Token 调用 Gateway，Gateway 拒绝缺失或伪造身份。
- Agent 可创建群、加成员、发消息；非成员和跨租户操作被拒绝。
- 控制面只读展示 Agent 群聊和协作请求过程，审计归因到实际执行的员工 Agent。
- 核心业务操作通过 Agent Tool/API 执行，且每次执行重新鉴权。
- ADR、详细设计、OpenAPI、实现覆盖和 E2E 同步更新。
