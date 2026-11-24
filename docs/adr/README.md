# Regent-Agent 架构决策记录（ADR）

- 版本：v0.1
- 更新日期：2026-11-23
- 用途：记录已接受的后端、前端、数据、模型、部署、可靠性和治理技术决策。

## 1. 使用方式

- Q-001～Q-052 记录产品和架构讨论结论。
- ADR 记录后续实现中 Accepted 的技术决策。
- 详细设计描述 ADR 的可实施契约、状态机、算法和验收场景。
- 决策变化时新增 ADR，并标记旧 ADR 为 `Superseded`，不静默改写历史。

状态：

| 状态 | 含义 |
|---|---|
| Proposed | 待评审 |
| Accepted | 已接受并用于实现 |
| Superseded | 被新 ADR 替代 |
| Deprecated | 不再推荐，但历史保留 |
| Rejected | 已评估但未采用 |

## 2. ADR 索引

| ADR | 决策 | 状态 | 关联设计 |
|---|---|---|---|
| [0001](./0001-backend-stack.md) | 后端采用 .NET 10 与 MAF | Accepted | 后端技术文档 |
| [0002](./0002-frontend-stack.md) | 前端采用 React 19、TypeScript、MUI 与 SignalR | Accepted | 前端技术文档、48 |
| [0003](./0003-postgres-rustfs-workspace.md) | Postgres 为状态权威，RustFS 为对象权威 | Accepted | 15、49 |
| [0004](./0004-postgres-keyvault.md) | Gateway 密钥库存放于 Postgres keyvault schema | Accepted | 03、04、31 |
| [0005](./0005-cloud-native-model-runtime.md) | 模型运行仅支持 OpenAI 与 Anthropic 云端原生端点 | Accepted | 03 |
| [0006](./0006-internal-api-extension-boundary.md) | API、Tool 与 MCP 仅用于系统内部 | Accepted | 42、47 |
| [0007](./0007-github-tag-local-compose.md) | GitHub Tag 源码与本地 Compose 构建 | Accepted | 39、43 |
| [0008](./0008-outbox-inbox-saga.md) | 跨模块使用 Outbox、Inbox 与 Saga | Accepted | 02、15 |
| [0009](./0009-judge-employee-runtime.md) | Judge 作为独立员工运行时 | Accepted | 14、46 |
| [0010](./0010-internal-notifications.md) | 通知仅通过仪表盘内部投递 | Accepted | 41 |
| [0011](./0011-configurable-compliance.md) | 合规通过 ComplianceProfile 配置并 fail closed | Accepted | 45 |
| [0012](./0012-single-node-capacity.md) | 当前只承诺单租户单机聚合容量 | Accepted | 44 |
| [0013](./0013-pluggable-external-adapters.md) | 外部能力采用可插拔适配，本地模式为默认 | Accepted | 03、15、17、26、27、31、50 |

## 3. 新 ADR 模板

```markdown
# ADR-XXXX：决策标题

- 状态：Proposed
- 日期：YYYY-MM-DD
- 范围：影响的模块和能力

## 背景
描述问题、约束和触发决策的因素。

## 决策
明确写出采用什么，以及必须遵守的边界。

## 结果
列出收益、成本、风险和维护影响。

## 未采用
列出主要替代方案和否决原因。

## 关联
链接相关 Q 决策、详细设计和 ADR。
```

## 4. 评审规则

- ADR 必须说明决策边界，不能只写技术名称。
- ADR 必须列出未采用方案，避免重复讨论。
- ADR 必须链接可实施详细设计。
- 影响安全、租户隔离、密钥、审计或数据保留的 ADR 需要安全评审。
- 影响公共兼容、部署或升级的 ADR 需要运维评审。
- Superseded ADR 必须指向替代者。
