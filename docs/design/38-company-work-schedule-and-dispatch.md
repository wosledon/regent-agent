# 38 公司作息与调度

- 版本：v0.1（待评审）
- 范围：全天可配置窗口、低价窗口、成本感知调度、排班检查点、节假日和紧急唤醒
- 对应需求：FR-081、BR-130、BR-131、BR-132、BR-133
- 对应缺口：G-53

## 1. 目标与边界

- 公司全天 24 小时可配置，不默认禁止夜间。
- 用户可设置工作、静默、禁用和低价窗口。
- 可延期任务按截止时间和成本选择执行窗口。
- 到达用户禁用窗口或排班结束时在安全点暂停。
- 紧急安全事件可随时唤醒。
- 调度不扩大 Tool、预算、网络或数据权限。

## 2. WorkSchedule

```text
id
tenant_id
timezone
windows[]
blocked_windows[]
quiet_windows[]
effective_from
effective_to
version
```

- 默认全天 24 小时可运行，不设置静默或禁用窗口。
- 时区使用 IANA 标识。
- 支持多窗口和跨午夜窗口，例如 22:00–06:00。
- 用户可分别设置工作、静默和禁用窗口。
- 岗位可覆盖公司作息，员工个人不拥有无限制的排班权。
- 策略版本绑定调度决策，可审计。

### 2.1 CostWindow

```text
id
tenant_id
provider_id?
model_id?
timezone
starts_at
ends_at
discount_type
discount_value
source_ref
confidence
effective_from
effective_to
version
```

- 用户可标记夜间或其他低价窗口。
- 折扣必须记录来源、适用范围和有效期。
- 折扣未知时不假设夜间免费或打折。
- 价格按请求时有效快照记账，历史不追溯改写。
- 调度只使用已确认的 CostWindow，未验证折扣不参与优化。

## 3. 调度状态

```text
Pending
Scheduled
Assigned
Running
DeferredUntil
Sleeping
Cancelled
Completed
Failed
```

- `Scheduled` 表示已计算下一个可执行时间。
- `DeferredUntil` 表示任务因工作窗口暂停。
- `Sleeping` 是员工状态，不是任务完成。
- 延期不改变任务权重、截止时间或历史证据。

## 4. 工作窗口判定

调度器按以下顺序检查：

1. 租户和岗位 WorkSchedule。
2. IANA 时区中的本地时间和跨午夜窗口。
3. HolidayCalendar。
4. OnCallOverride。
5. ScheduleOverride。
6. CostWindow 和截止时间。
7. 任务优先级和紧急唤醒策略。

- 不存在的工作时间或夏令时重复按 Q-040 的确定性规则处理。
- 调度决策保存 `schedule_version` 和本地时间解释。

## 5. 普通任务与成本窗口

- 用户允许的窗口内正常认领和执行，夜间与白天同等对待。
- 用户禁用窗口内的普通任务进入 `Scheduled`。
- 可延期 P2/P3 任务在截止时间允许时优先选择 CostWindow。
- P0/P1 不等待低价窗口。
- 到实际执行时重新检查能力、WIP、预算、权限和有效价格。
- 截止时间或 CostWindow 变化重新计算 `scheduled_for`。
- 禁用窗口内不启动普通模型调用，紧急任务按 WakePolicy 处理。

## 6. 下班与在途任务

```text
接近用户禁用窗口或排班结束
  → 停止认领新任务
  → 当前任务到安全点
  → 保存 TaskCheckpoint
  → 提交已确认成果和账本
  → 释放任务租约
  → 标记 DeferredUntil
  → 员工 Sleeping
```

- 普通任务不得为了完成而无限运行。
- 当前 Tool 调用按安全策略完成、取消或回滚。
- 工作区生成快照，RustFS 保存引用。
- 团队工位容器组内全员休眠后才停止。
- 下次唤醒从检查点恢复，不重复已完成副作用。

## 7. HolidayCalendar

```text
id
tenant_id
name
region?
dates[]
recurrence_rule?
schedule_mode
version
```

- 节假日由用户配置为休息、正常工作或自定义窗口。
- 值班岗位可通过 OnCallOverride 工作。
- 假期结束不自动补跑全部历史任务。
- 只恢复待办、到期和明确安排的工作。
- 日历变化记录原因和审批。

## 8. 紧急唤醒

| 事件 | 默认行为 |
|---|---|
| P0 安全、红线、生产事故 | 立即唤醒相关员工 |
| L2/L3 告警 | 按值班策略唤醒 |
| 硬预算、密钥风险 | 立即处置 |
| P1 临近截止 | 允许窗口内优先执行，禁用窗口按策略唤醒 |
| P2/P3 普通任务和消息 | 允许窗口内执行，禁用窗口排队 |

- 紧急唤醒不扩大权限。
- 安全遏制按预授权执行。
- 非安全加班仍受预算和审批约束。
- 唤醒结束后员工返回 Sleeping 或正常工作窗口。

## 9. WakePolicy

```text
id
tenant_id
event_type
priority
eligible_roles[]
allowed_hours
budget_limit
max_duration
requires_on_call
version
```

- 每种唤醒事件必须有明确策略。
- 不允许用 `*` 角色无限唤醒所有员工。
- 唤醒范围限制到相关员工和任务。
- 策略变化只影响新事件，历史记录保留原版本。

## 10. ScheduleOverride

```text
id
tenant_id
employee_id?
position_id?
reason
starts_at
expires_at
budget_limit
approved_by?
status
```

- Controller 可在现有权限和预算内创建临时加班。
- 超预算、跨红线或影响其他租户仍走审批。
- 到期自动失效，不形成长期隐性作息。
- 全天运行是合法配置，但必须由用户显式设置并受预算约束。

## 11. Butler 与 Controller

- Butler 定时任务使用 WorkSchedule、CostWindow 和租户时区。
- 默认每日报告安排在用户设置的报告时间；未设置时默认 09:00。
- Controller cadence 在允许窗口内运行，可配置全天或夜间。
- cadence 仍避免无意义频繁唤醒，成本窗口可影响低优先级周期任务。
- 事件触发的安全任务不受工作窗口限制。

## 12. 消息

- P0/P1 按 WakePolicy 处理。
- P2/P3 和普通聊天在允许窗口投递，禁用窗口进入队列或摘要。
- `@` 不自动绕过用户设置的禁用窗口。
- 消息唤醒不自动创建任务或扩大权限。

## 13. KPI

- 用户禁用窗口、休息和假期不计入员工可用率分母。
- 允许的夜间工作时间与白天同等计入。
- 不因按用户设置休眠扣绩效。
- 状态事件记录 `schedule`、`emergency`、`platform` 等归因。
- 管理排班不当与员工故障分开。
- 紧急唤醒次数用于治理，不自动处罚员工。

## 14. 仪表盘

显示：

- 当前是否在工作窗口。
- 下一个工作窗口。
- 值班人员。
- 延期任务数和等待时间。
- 紧急唤醒次数和原因。
- 节假日和临时加班安排。

## 15. API 与事件

- `GET /work-schedules`
- `PUT /work-schedules/{id}`
- `POST /holiday-calendars`
- `POST /schedule-overrides`
- `POST /wake-policies`

事件：

- `WorkWindowOpened`
- `WorkWindowClosed`
- `TaskDeferredUntil`
- `EmployeeSleeping`
- `EmployeeWoken`
- `EmergencyWakeRequested`
- `ScheduleOverrideExpired`

## 16. 指标

| 指标 | 目标 |
|---|---:|
| 非计划窗口普通任务启动数 | 0 |
| 紧急唤醒审计覆盖率 | 100% |
| 排班检查点完整率 | 100% |
| 因禁用窗口延期任务按时恢复率 | ≥95% |
| 低价窗口任务占比 | 持续观察 |

## 17. 测试场景

1. 默认全天配置下普通任务可在夜间执行。
2. 允许窗口开始后重新检查权限和预算。
3. 禁用窗口或排班结束时在途任务保存检查点并释放租约。
4. 延期任务不重复已完成副作用。
5. 用户配置节假日休息时不启动普通任务。
6. OnCallOverride 可在假期工作。
7. P0 事件立即唤醒相关员工。
8. P1 在允许窗口优先执行，禁用窗口按策略处理。
9. P2/P3 消息在允许窗口投递，禁用窗口不唤醒员工。
10. 紧急唤醒不扩大 Tool 权限。
11. ScheduleOverride 到期自动失效。
12. 超预算加班进入审批。
13. 时区变化重新计算下一工作窗口。
14. 夏令时不存在或重复时间按规则处理。
15. 休息时间不计入可用率分母。
16. 组内全员休眠后才停止工位容器。
17. Butler 报告按用户设置时间执行，未设置时默认 09:00。
18. 全天运行配置在预算内可正常工作。
19. 可延期任务优先进入已配置 CostWindow。
20. 折扣未知时按普通有效价格记账。
21. P0/P1 不等待低价窗口。

## 18. 验收场景

1. 公司全天 24 小时可配置，不默认禁止夜间。
2. 用户禁用窗口内不产生普通模型调用。
3. 在途任务可安全暂停和恢复。
4. 可延期任务可利用已配置低价窗口。
5. 紧急唤醒范围明确且完整审计。
6. 临时加班限时、限范围并在预算内。
7. 用户禁用窗口和休息不导致绩效处罚。
8. 调度不扩大权限或绕过安全门禁。
9. 仪表盘可解释延期、值班、低价窗口和紧急唤醒。

## 19. 关联设计

- 时区和本地化见 [37 国际化与本地化](./37-internationalization-and-localization.md)。
- 任务租约和恢复见 [02 运行时可靠性与持久化](./02-runtime-reliability.md)。
- 消息优先级见 [13 Agent 聊天与协作](./13-agent-chat-and-collaboration.md)。
