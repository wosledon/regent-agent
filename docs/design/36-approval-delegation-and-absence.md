# 36 审批委托与缺席

- 版本：v0.1（待评审）
- 范围：缺席协商、分级审批、 ApprovalDelegation、备用审批人、SLA 和审计
- 对应需求：FR-020、FR-079、BR-121、BR-122、BR-123、BR-124、BR-125
- 对应缺口：G-50

## 1. 目标与边界

- 人类缺席时优先由 Controller 与直接下一层协商。
- 协商只能在现有授权内解决，不能创造权限。
- 不能处理时才进入委托或备用审批人流程。
- 无有效审批人时保持待定，超时默认拒绝。
- 红线、所有者专属事项和解雇的人类批准不可绕过。

## 2. 审批分级

| 等级 | 示例 | 默认处理 |
|---|---|---|
| T0 自动 | TaskGrant 内、预算内、非外部动作 | 无需人工审批 |
| T1 运营 | 内部资源调整、一般外部写 | 能力负责人 |
| T2 治理 | 超预算、高风险外部动作、解雇 | 人类 Boss/Co-Owner |
| T3 所有者 | 主所有权转让、租户删除、订阅变更 | Primary Owner |
| 禁止 | 红线、绕过审批、明显违法 | 拒绝并告警 |

- 等级由动作类型、金额、数据范围、可逆性和风险确定。
- 高风险动作可配置双人 quorum。
- 审批绑定 `action_hash`，参数变化即失效。
- Tier 不能由 Controller 自行降级。

## 3. 缺席判定

- 审批人显式设置 Away、OutOfOffice 或 Unavailable。
- 策略可配置日历和值守窗口。
- 不能仅因短时间未登录就自动视为缺席。
- 人类缺席状态与审批路由必须审计。

## 4. 缺席协商

### 4.1 参与者

- Controller。
- 与事项直接相关的一名或多名下一层员工。
- 必要时加入独立专业评审者。

下一层员工必须具有相关 Capability，不能由无关人员凑数。

### 4.2 前置检查

协商前执行确定性检查：

- 动作是否被禁止。
- 是否属于所有者专属或人类专属。
- 是否超出预算、Tool、网络或数据范围。
- 是否影响其他租户或安全边界。
- 是否需要 quorum 或 step-up。

任一硬门禁失败时跳过自主执行，直接进入对应审批或拒绝。

### 4.3 协商规则

- Controller 和下一层代表必须明确同意。
- 任何一方反对、权限不清或证据不足时视为不能处理。
- 不允许多数票覆盖红线、预算或权限。
- Controller 不能单方面把协商结果当作审批。
- 发起者不能同时成为唯一批准者。
- 利益冲突时必须重新分派参与者。

### 4.4 结果

```text
ResolvedWithinAuthority
NeedsHumanApproval
BlockedByPolicy
Deadlock
InsufficientEvidence
```

- `ResolvedWithinAuthority` 才可自主执行。
- 其他结果进入人工路由或拒绝。
- 解雇、移除订阅、所有者专属和不可逆大规模清除不能通过协商执行。

## 5. NegotiationRecord

```text
id
tenant_id
escalation_id
participants[]
positions[]
options[]
evidence_refs[]
authority_refs[]
budget_check_ref
outcome
decision_summary
created_at
completed_at
```

- 保留双方意见和反证。
- 记录使用的 Capability、预算和策略版本。
- 自主执行生成 `on_behalf_of` 关系。
- 人类事后审查可读取完整协商记录。

## 6. ApprovalDelegation

```text
id
tenant_id
grantor_id
delegate_id
action_types[]
risk_tiers[]
amount_limit?
scope_refs?
starts_at
expires_at
accepted_at?
revoked_at?
reason
```

规则：

- 只允许 Active 人类成员。
- 委托人生成，被委托人明确接受后生效。
- 默认最长 30 天，可更短。
- 可随时撤销。
- 不可转授权、循环委托或自我委托。
- 被委托人不能批准自己发起或存在利益冲突的事项。
- 解雇只能委托给人类 Boss/Co-Owner，普通成员不可代签；无人类 Boss 时保持待定。
- 委托不能突破原权限、quorum、红线或安全限制。
- 到期、撤销、成员暂停或能力变化立即失效。

## 7. 不可普通委托

- Primary Owner 转让。
- 租户删除。
- 订阅和付款变更。
- 所有者管理。
- 法律、监管或对外声明。
- 不可逆大规模数据清除。
- 绕过安全红线的动作。

这些动作必须由规定的所有者或授权人类处理，不能通过 ApprovalDelegation 代签。

## 8. 路由算法

```text
硬策略检查
  ├─ 禁止 → 拒绝并告警
  ├─ T0 → 自动执行
  └─ 需要审批
       ├─ 人类可用 → 对应审批人
       └─ 人类缺席
            ├─ 允许协商 → Controller + 直接下一层
            │    ├─ 现有授权内达成共识 → 执行并审计
            │    └─ 不能处理 → 继续
            └─ 不允许协商 → 继续

有效 ApprovalDelegation
  → 备用审批人
  → 可处理该事项的 Co-Owner
  → 保持待定并超时拒绝
```

- Primary Owner 专属事项不会降级到 Co-Owner。
- 没有有效代理人时不自动同意。
- 路由版本和每次转移都写入审计。

## 9. 默认 SLA

| 紧急程度 | 时限 |
|---|---:|
| Critical | 30 分钟 |
| High | 4 小时 |
| Normal | 24 小时 |
| Low | 72 小时 |

- 租户可按动作类型调低 SLA。
- 到期前提醒，到期后默认拒绝。
- 审批或协商超时不会自动通过。
- 紧急安全遏制按预授权立即执行，不等待本流程。

## 10. Quorum 与二次确认

- 高风险动作可要求两名不同人类批准。
- 双人 quorum 不能由同一委托链替代。
- 财务和不可逆动作需要 step-up 或二次确认。
- quorum 不足、参数变化或审批过期都不能执行。

## 11. UI

- 待审批卡片显示：
  - 当前审批人和角色。
  - 是否代表他人审批。
  - 委托范围和到期时间。
  - 缺席协商状态和结论。
  - SLA 剩余时间。
  - 动作参数和影响。
- 不提供无范围的“全部批准”。
- 委托人可查看、撤销和审计代签记录。
- 自主协商结果进入行动中心，但标记为无需人类审批。

## 12. 审计

记录：

- 分级和策略版本。
- 审批路由每次变化。
- 缺席状态。
- 协商参与者、证据和结论。
- 委托创建、接受、撤销和到期。
- `approved_by`、`on_behalf_of` 和委托 ID。
- 动作 hash、执行结果和拒绝原因。

## 13. API 与事件

- `POST /approval-delegations`
- `POST /approval-delegations/{id}/accept`
- `DELETE /approval-delegations/{id}`
- `POST /escalations/{id}/negotiate`
- `POST /escalations/{id}/reroute`

事件：

- `ApprovalDelegationCreated`
- `ApprovalDelegationAccepted`
- `ApprovalDelegationRevoked`
- `ApprovalDelegationExpired`
- `AbsenceNegotiationStarted`
- `AbsenceNegotiationResolved`
- `AbsenceNegotiationEscalated`
- `ApprovalRerouted`

## 14. 指标

| 指标 | 目标 |
|---|---:|
| 审批按时处理率 | ≥95% |
| 过期委托使用数 | 0 |
| 越权代签数 | 0 |
| 自我批准数 | 0 |
| 无记录自主协商数 | 0 |
| 超时自动批准数 | 0 |

## 15. 测试场景

1. 人类缺席时先触发协商。
2. 协商双方同意且权限充足时可自主执行。
3. 任一方反对时不能自主执行。
4. 预算不足时不能通过协商绕过。
5. 红线动作跳过协商并拒绝。
6. 解雇不能通过协商执行。
7. Primary Owner 专属事项不能降级到 Co-Owner。
8. 委托必须被接受后才生效。
9. 过期或撤销委托立即失效。
10. 委托不能转授权。
11. 被委托人不能自我批准。
12. 委托不能突破 quorum。
13. 参数变化使原审批失效。
14. 无有效代理人时超时拒绝。
15. 紧急安全遏制不等待审批。
16. 所有代签和协商均可审计。

## 16. 验收场景

1. 缺席协商优先于人工路由。
2. 协商不创造权限或突破硬门禁。
3. 不能处理时才进入委托或备用审批。
4. 分级、委托和 quorum 可验证。
5. 所有者专属事项不可普通委托。
6. SLA 到期默认拒绝。
7. 人类事后审查可重建完整决策链。
8. 无有效审批人时公司不会获得静默授权。

## 17. 关联设计

- 所有者治理委托见 [28 账户、租户与所有权](./28-account-tenancy-and-ownership.md)。
- 升级事项和安全审批见 [04 安全、治理与恢复](./04-security-governance-recovery.md)。
- 首屏和行动中心见 [35 租户仪表盘信息架构](./35-tenant-dashboard-information-architecture.md)。
