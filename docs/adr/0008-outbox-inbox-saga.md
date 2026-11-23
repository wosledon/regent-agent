# ADR-0008：跨模块使用 Outbox、Inbox 与 Saga

- 状态：Accepted
- 日期：2026-11-23
- 范围：分布式一致性和消息投递

## 背景

ControlPlane、Gateway、Runner 和 Operations 是独立进程，需要可靠投递和最终一致性，但不引入重型消息中间件。

## 决策

- 单库短事务维护即时不变量。
- 跨模块使用 Outbox、Inbox、Saga 和补偿。
- 消息和事件至少一次投递，消费端按消费者和事件 ID 去重。
- 创建类 API 使用 Idempotency-Key。
- 任务使用租约和 fencing token 防止旧 Worker 覆盖新结果。
- 不使用 XA/2PC，不引入 Kafka。

## 结果

- 进程崩溃后事件可重放，业务副作用保持幂等。
- 运维和理解成本较低。
- 需要监控 Outbox 积压、Inbox 重复和死信。

## 未采用

- XA/2PC：跨数据库和外部调用复杂度高。
- Kafka：当前吞吐量不需要。

## 关联

- [02 运行时可靠性与持久化](../design/02-runtime-reliability.md)
- [15 数据 Schema 与事务边界](../design/15-data-schema-and-transactions.md)
