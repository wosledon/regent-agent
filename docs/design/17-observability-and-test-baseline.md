# 17 可观测性与测试基线

- 版本：v0.1（待评审）
- 范围：OpenTelemetry、可插拔 Profile、Elastic Stack、Kibana APM、SLO、错误预算、告警、测试与容量/混沌门禁
- 对应需求：FR-059、BR-056、BR-057、BR-058
- 对应缺口：G-21、G-22

## 1. 目标与边界

- 四个服务和两个前端统一接入 OpenTelemetry SDK，Exporter 按 Profile 启用。
- 自托管默认 `local`，不强制部署额外遥测组件。
- Elastic Stack 是可选完整后端，不是业务或审计权威源。
- Postgres 审计和账本始终是事实来源。
- 可观测性数据不得泄漏 Prompt、源码、密钥、敏感工具参数或密封评测集。
- 遥测后端故障不得阻塞核心业务。

## 2. Profile 与部署

| Profile | 组件 | 默认场景 |
|---|---|---|
| `off` | 不启用 Exporter | 极简或资源受限 |
| `local` | stdout/journald、Docker 或轮转文件日志 | 自托管默认 |
| `otlp` | 用户已有 OTLP 后端 | 已有监控平台 |
| `elastic` | Elasticsearch、Kibana、APM Server、Collector | 完整可观测性 |

```yaml
observability:
  profile: local
  signals:
    metrics: false
    logs: true
    traces: false
  sampling: 0.1
  redaction: strict
```

- 指标、日志和追踪可独立开关。
- Profile 在启动时读取，修改后重启生效，不做热切换。
- Elastic 组件放入可选 Compose Profile，默认不启动：

```bash
docker compose up -d
docker compose --profile observability-elastic up -d
```

Elastic Profile 拓扑：

```text
Regent-Agent services
  → OpenTelemetry SDK
  → OTel Collector / Elastic Agent
      ├─ Metrics → Elasticsearch
      ├─ Logs    → Elasticsearch
      └─ Traces  → APM Server → Elasticsearch
  → Kibana
```

- 仅 Kibana 可按需暴露，Elasticsearch 和 APM 不直接暴露公网。
- 启用认证、TLS、最小权限和 ILM 保留策略。
- 镜像按 digest 固定并纳入 ReleaseManifest。

## 3. 信号清单

| 域 | 指标 |
|---|---|
| API | QPS、错误率、P95/P99、限流 |
| 调度 | 排队、认领成功率、租约过期、重试 |
| 消息 | Outbox 积压、投递延迟、Inbox 重复、死信 |
| 模型 | 调用量、Token、成本、Provider 错误、熔断 |
| 预算 | 预留、结算延迟、超限拒绝、对账差异 |
| RustFS | 上传成功率、P95、PendingUpload 年龄、孤儿对象 |
| 沙箱 | 启动时间、资源超限、OOM、逃逸拦截 |
| 业务 | 阶段停留、验收通过、返工、内部升级 |
| 备份 | 成功率、大小、RPO/RTO 偏差 |

高基数字段如 `task_id`、`employee_id`、`request_id` 只进入日志和 Trace，不作为全局指标标签。租户级 SLO 通过独立聚合计算。

## 4. 日志

- 使用结构化 JSON。
- 必填：时间、级别、服务、版本、环境、trace_id、span_id、tenant_id、事件名、原因码。
- 任务相关日志可带 task_id 和 employee_id。
- 错误必须可关联到请求、任务或事件。
- 禁止记录 Authorization、API Key、密码、完整 Prompt、模型响应、源码和敏感参数。
- PII 默认遮蔽或令牌化，遥测删除请求必须传播到 Elasticsearch 和本地日志。
- local Profile 写入 stdout/journald 或轮转文件；otlp/elastic Profile 写入对应后端。

## 5. 链路追踪

- 使用 W3C Trace Context。
- ControlPlane、Gateway、Runner、Postgres 调用、Outbox、RustFS 和外部 HTTP 建立 Span。
- Provider Span 只记录模型版本、Token、耗时和状态，不记录内容。
- 正常追踪默认采样 10%。
- 错误、L2/L3、预算、越权和关键发布路径 100% 保留。
- elastic Profile 使用 Kibana APM 展示服务地图、延迟、错误和 Trace 瀑布。
- otlp Profile 是否支持 APM 取决于所选后端。
- off/local Profile 不要求 APM，但日志仍保留可用的 trace_id。

## 6. 默认 SLO

| 服务 | 默认目标 |
|---|---:|
| 控制面 API 月度可用性 | ≥99.5% |
| 普通读 API P95 | ≤300ms |
| 普通写 API P95 | ≤500ms |
| 任务领取响应 P95 | ≤2 秒 |
| 消息消费 P95 延迟 | ≤10 秒 |
| 用量入账 P95 延迟 | ≤60 秒 |
| 审批状态传播 | ≤5 秒 |
| 仪表盘实时状态延迟 | ≤5 秒 |
| Outbox 投递成功率 | ≥99.5% |
| RustFS 上传成功率 | ≥99.5% |
| RustFS 上传 P95 | ≤10 秒，50MB 以内 |
| 任务认领成功率 | ≥99% |

Provider 可用性单独统计，不混入 Regent-Agent 自身 API 可用性。SLO 和错误预算在 elastic Profile 或具备指标能力的 otlp 后端中强制执行；off/local Profile 不要求运行时 SLO。

## 7. 错误预算

- 99.5% 月度可用性对应 30 天约 3.6 小时不可用。
- 1 小时消耗 2% 预算触发 L2。
- 6 小时消耗 5% 预算触发 L2。
- 预算耗尽触发 L3 并暂停非必要发布。
- 连续 30 分钟错误率超过 5% 或任务领取完全不可用触发 L3。
- 预算恢复后仍保留事故和变更记录。
- off/local Profile 不计算运行时错误预算，不影响 Postgres 审计和业务预算控制。

## 8. 告警路由

| 级别 | 路由 | 动作 |
|---|---|---|
| L0 | 指标和日志 | 观察 |
| L1 | 日报 | 汇总轻微异常 |
| L2 | 直接 Leader / 值班负责人 | 告警、唤醒、Runbook |
| L3 | Controller + Boss | 冻结相关动作、升级、事故流程 |

- 告警去重、分组和抑制。
- Silence 必须有过期时间和审批。
- 告警包含影响范围、Runbook、最近变更和 Trace 示例。
- 必须 Ack、记录恢复时间并关联事故。
- 仪表盘通知中心始终保留内部事件，SignalR 只是实时增强。
- 通知投影或实时推送失败不能丢失内部事件，客户端可重新拉取。
- L1～L3 和业务风险进入租户行动中心，按根因去重并显示负责人、SLA、下一步和新鲜度。
- 行动中心只改变站内呈现，不替代源告警、事故或升级状态，详见 [35 租户仪表盘信息架构](./35-tenant-dashboard-information-architecture.md)。

## 9. 保留与访问

| 数据 | 初始保留 |
|---|---:|
| 指标 | 30 天，聚合后可长期保留 |
| 日志 | 14 天 |
| 追踪 | 7 天 |
| 审计 | 租户终止后 7 年，不以 ILM 替代 |

- 租户和运营访问分离。
- Kibana 空间和索引权限按角色配置。
- local Profile 使用日志轮转和磁盘上限，otlp Profile 遵守后端保留策略。
- 遥测数据不进入普通员工记忆。
- 删除请求按数据治理策略传播。

## 10. 测试分层

| 层 | 工具/方式 | 重点 |
|---|---|---|
| 单元测试 | xUnit | 状态机、权重、预算、权限、评分 |
| 组件测试 | xUnit + 测试替身 | 单模块行为 |
| 集成测试 | 真实 Postgres + RustFS | RLS、事务、Outbox、上传 |
| 契约测试 | 原生 Provider 录制 | OpenAI/Anthropic 语义 |
| E2E | 最小完整部署 | 建岗、招聘、任务、评审、验收 |
| UI | Playwright for .NET | 关键用户路径 |
| 安全 | 自动化扫描与专项用例 | 越权、SSRF、密钥、容器 |
| 许可 | CI 许可证扫描与发布报告 | SPDX、NOTICE、SBOM、未知许可证、copyleft 兼容 |
| 升级 | N-1/N 类生产环境 | Expand、混跑、回滚、恢复 |

CI 使用 GitHub Actions 执行，默认使用 GitHub 托管 Runner。CI 不依赖真实 Provider 才能通过；真实调用只作为可选冒烟测试。所有 Profile 下测试分层和安全门禁均保持有效。

## 11. 覆盖与质量门槛

- 整体行覆盖率 ≥80%。
- 安全、预算、账本、RLS、任务状态机等关键模块 ≥90%。
- 关键业务路径必须有集成测试。
- 破坏性迁移必须有升级和恢复测试。
- 不允许静默跳过失败测试。
- Flaky 测试必须登记负责人、原因和修复期限。
- 覆盖率不能替代断言质量和场景覆盖。

## 12. 容量基线

| 项目 | 目标 |
|---|---:|
| 自托管活跃租户 | 1 |
| 员工 | 20 |
| 最大并行任务 | 40 |
| 任务创建 | 100/小时 |
| 消息 | 1000/小时 |
| 模型请求 | 100/分钟 |
| 突发 | 2 倍持续 15 分钟 |
| 稳定性 | 24 小时 soak |
| 公平性测试租户 | 最多 5 个模拟租户 |

容量数字是单机部署聚合上限，不是每租户分别叠加。Provider 使用可编排模拟服务，覆盖正常、流式中断、429、5xx、高延迟和超长上下文。

队列上限：

| 队列 | 默认上限 | 超限行为 |
|---|---:|---|
| 单租户 Pending Task | 200 | 429，拒绝新任务 |
| 单员工待处理项 | 20 | 停止新派工 |
| 单租户消息积压 | 5000 | 限流并提示重试 |
| Outbox 待投递 | 10000 | 告警并限流非关键写入 |
| 模型在途请求 | 全局 40 / 单租户 10 / 单员工 2 | 排队或 429 |

验证：

- 无跨租户饥饿，单租户连续占用共享槽最多 3 次后让出。
- 其他租户普通 API P95 劣化 ≤20%，合格任务等待 ≤30 秒。
- 预算不超支，任务不重复认领。
- 消息和账本不丢失，队列满时不静默丢弃。
- RustFS 不积压无法恢复的上传。
- Elasticsearch 写入不拖慢业务请求。
- 资源使用在目标硬件内。
- 完整规则见 [44 容量与性能基线](./44-capacity-and-performance-baseline.md)。

## 13. 混沌与故障测试

至少覆盖：

- 随机杀 Runner。
- 杀 Gateway。
- ControlPlane 重启。
- Postgres 连接中断。
- RustFS 不可用。
- elastic/otlp 遥测后端写入失败。
- 网络分区和延迟。
- Outbox 重复、乱序和毒消息。
- 模型流式中断。
- 本地工作区丢失。
- 升级中途失败。

遥测后端故障不得阻塞核心业务；遥测写入使用有界缓冲和降级策略。

## 14. 安全测试

- 跨租户读写拒绝。
- RLS 绕过失败。
- Tool 发现与执行双重鉴权。
- SSRF、路径穿越、符号链接逃逸。
- 日志和 Trace 脱敏。
- 容器权限和挂载隔离。
- 密钥扫描。
- 依赖和镜像漏洞扫描。
- elastic Profile 下 Kibana/Elasticsearch 未授权访问拒绝。
- 评测密封集不可读取。

安全红线问题一票否决，不进入发布候选。

## 15. 发布门禁

所有 Profile 下发布前必须提供：

- 单元、集成、契约、E2E 和 UI 结果。
- 安全扫描结果。
- 容量测试摘要。
- 启用备份组件时的故障恢复演练结果；关闭时不作为发布门禁。
- 已知 Flaky 和例外审批。
- 对应 ReleaseManifest。

| 门禁 | off/local | otlp | elastic |
|---|---|---|---|
| 结构化日志 | 基础 | 完整 | 完整 |
| 运行时 SLO | 不要求 | 视后端能力 | 必须 |
| 错误预算 | 不要求 | 视后端能力 | 必须 |
| APM 链路 | 不要求 | 视后端能力 | 必须 |
| Kibana 样例 | 不要求 | 不要求 | 必须 |

## 16. 验收场景

1. 默认部署使用 local Profile，不启动 Elastic 组件。
2. elastic Profile 按需启动 Elasticsearch、Kibana、APM Server 和 Collector。
3. 指标、日志和追踪可以独立开关。
4. 遥测后端故障不阻塞任务认领。
5. elastic Profile 下日志、指标和追踪可在 Kibana 关联。
6. Trace 不包含 Prompt、源码或密钥。
7. 整体覆盖率低于 80% 时发布失败。
8. 关键模块覆盖率低于 90% 时发布失败。
9. CI 不调用真实 Provider 也能完成。
10. RLS 和跨租户测试使用真实 Postgres。
11. 20 员工、40 并行任务负载通过。
12. 24 小时 soak 无任务、消息或账本丢失。
13. 混沌测试后可从检查点恢复。
14. 升级测试覆盖 N/N-1 混跑和回滚边界。
15. elastic Profile 下 Elasticsearch、Kibana 和 APM 未授权访问被拒绝。
16. off/local Profile 不要求 Kibana、APM 和运行时错误预算。
17. 关闭可观测性后端不降低测试和安全门禁。
18. Profile 修改后重启生效。
19. local Profile 日志可以轮转并受磁盘上限约束。
20. otlp Profile 不启动内置遥测容器。
21. 日志和 Trace 不包含秘密或未脱敏 PII。
22. 普通读 API P95 ≤300ms，普通写 API P95 ≤500ms。
23. 队列达到上限时返回明确背压，不静默丢弃。
24. 模型在途请求按全局、租户和员工上限排队。
25. 单租户压测时其他租户 API P95 劣化不超过 20%。
26. 其他租户合格任务等待不超过 30 秒。
27. 2 倍突发 15 分钟后队列在 5 分钟内恢复。
28. 容量报告绑定版本 Tag、硬件和配置。
