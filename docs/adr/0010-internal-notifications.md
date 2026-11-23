# ADR-0010：通知仅通过仪表盘内部投递

- 状态：Accepted
- 日期：2026-11-23
- 范围：内部通知与外部渠道

## 背景

当前需要可靠告警和审批提醒，但不想维护多个外部通知 Provider。

## 决策

- 第一梯队只实现仪表盘内部通知中心。
- 不实现 Telegram、邮件、短信、其他 IM 或出站 Webhook。
- 通知先持久化，再通过 SignalR 实时推送。
- SignalR 失败后客户端可按游标重新拉取。
- L3/SEV1 不受普通静默时段限制。
- 通知不得包含秘密或替代正式审批。

## 结果

- 通知可靠性和权限边界简单清晰。
- 外部渠道全部后置。
- 需要监控通知死信、重复和越权访问。

## 未采用

- 多 Provider 通知平台：超出当前范围。
- 仅 SignalR 推送：连接失败会丢提示。

## 关联

- [41 内部通知中心](../design/41-internal-notification-center.md)
- [35 租户仪表盘信息架构](../design/35-tenant-dashboard-information-architecture.md)
