# 跨模块预设场景试用矩阵

- 更新日期：2026-11-24
- 执行入口：`python3 scripts/scenario-trial.py`
- 默认范围：本地 ControlPlane、Gateway、Operations；不启动容器、不连接真实远程组件
- 关联模型：[跨功能关联关系](./cross-feature-relationship-map.md)

## 1. 场景列表

| ID | 场景 | 覆盖关联 | 核心断言 |
|---|---|---|---|
| S-01 | 交付计划到运行时任务 | 项目 → 调度 → 工作记录 → 审计 | 批准计划生成运行时任务；可认领；完成产生工作记录、工件和审计；交付状态回写 |
| S-02 | 招聘录用到套餐配额 | 组织 → 招聘 → 审批 → 计费 → Runner 自动建员工 | 员工/任职/岗位一致；Starter 第 5 名员工和未知 assignee 自动建员工均被拒；升级 Pro 后可继续录用和认领 |
| S-03 | 在途任务员工离职 | 组织 → 租约 → 协作 → 私有记忆 | 离职使旧租约失效、任务暂停；terminated 员工不能被重新认领；私有记忆隔离 |
| S-04 | 记忆投毒与独立纠错 | 记忆 → 安全 → 上下文 → Outbox/Inbox → 通知 | 隔离期不可召回；纠错后恢复；事件、通知和审计完整 |
| S-05 | 模型调用到金额结算 | Agent 身份 → Gateway → 预算 → 账本 → 对账 | 预留头存在；每次尝试独立用量；结算后释放；账单差异只追加更正 |
| S-06 | 租户软删除与恢复 | 身份 → 数据生命周期 → 访问控制 | 软删除后普通 API 与 Agent API 拒绝；恢复后原 Owner 可重新访问 |
| S-07 | 套餐配额与本地支付 | 计费 → 项目创建 → 发票/账本 | Starter 项目上限生效；本地支付幂等；Pro 升级后配额立即生效 |
| S-08 | required SSO 与紧急恢复 | OIDC → 会话 → 紧急凭据 → 权限 | 密码/GitHub 被拒；紧急会话 15 分钟且只允许恢复动作 |
| S-09 | 遥测告警到行动中心 | 前端遥测 → SLO → Ops 告警 → 租户行动中心 | 告警去重、确认/恢复；行动中心只读投影同步变化 |
| S-10 | 备份作业到双人恢复 | BackupProfile → 作业 → manifest → 审批 → RPO/RTO | 组件作业成功；自审批被拒；双人批准后 RTO 变为 measured |

## 2. 执行约定

- 每个场景使用独立租户或唯一标记，避免把上一步状态当成下一步前置。
- 所有写请求必须符合全局契约：`Idempotency-Key`；更新/删除附合版本条件。
- 人类操作使用 Cookie；Agent 操作使用员工 Assignment Token；Ops 操作使用独立 Cookie。
- 断言同时检查权威状态和下游投影，不只看 HTTP 200。
- 失败时输出场景 ID、步骤、HTTP 状态和错误码，不输出 Token、密码或完整敏感载荷。
- 脚本输出 JSON 报告到 `data/scenario-trial-report.json`，控制台只打印摘要。

## 3. 最终执行证据

- 执行日期：2026-11-24
- 执行命令：`python3 scripts/scenario-trial.py`
- 最终结果：**10/10 通过**；报告位于本地 `data/scenario-trial-report.json`。
- 首轮仅 S-07 通过，随后修复跨模块断链并修正脚本对 204、`claimId`、`sessionKind`、唯一备份 profile 和异步投影的预期。

| ID | 最终结果 | 关键证据 |
|---|---|---|
| S-01 | 通过 | 批准计划生成 3 个运行时任务；Runner 认领/完成回写交付状态；Agent `project.submit_task` 将运行时状态投影为 `InReview`；产生工作记录、工件、DoneEvidence 和有效审计链 |
| S-02 | 通过 | Starter 第 5 名员工返回 409；未知 assignee 自动建员工也返回 409；Pro 支付后复用原审批录用并由 Runner 自动创建第 7 名员工；账本仅一条 |
| S-03 | 通过 | 离职任务转 `Interrupted`；旧租约提交被拒；terminated Agent 返回 403；任务不可再认领；私有记忆不可见 |
| S-04 | 通过 | 投毒 claim 隔离且不可召回；独立纠错后按原作者 ACL 恢复召回；Outbox/Inbox 与通知完整 |
| S-05 | 通过 | 模型调用有预留头；Gateway 结算并释放预留；对账匹配；ControlPlane 轮询后出现同 request_id 的成本条目 |
| S-06 | 通过 | 小写 GUID 软删除成功；人类和 Agent 均 fail closed；恢复后仪表盘可访问 |
| S-07 | 通过 | Starter 第 4 个项目返回 409；本地支付重放不重复账；Pro 升级后可创建项目 |
| S-08 | 通过 | required SSO 拒绝密码/GitHub；紧急凭据轮换；响应 `sessionKind=Emergency`；普通 API 403、恢复 API 200 |
| S-09 | 通过 | 唯一 SLO 产生告警；行动中心投影出现；确认持久化；恢复后投影消失 |
| S-10 | 通过 | 唯一 BackupProfile 作业完成；有效 RPO 且 RTO 初始 unverified；自审批 409；双人批准后 RTO measured |

## 4. 自动化回归

- `dotnet build RegentAgent.slnx --no-restore`：0 警告、0 错误。
- `DOTNET_ROLL_FORWARD=Major dotnet test RegentAgent.slnx --no-restore`：**131 项通过**（Contracts 1、Gateway 19、ControlPlane 111）。
- 新增回归覆盖：交付投影幂等、HTTP/Agent Tool 运行时状态同步、应用层配额（含 Runner 自动建员工）、离职任务/租约、terminated 认领排除、个人纠错 ACL、软删除 GUID、紧急会话响应、成本事件幂等和用量投影重试。
- 前端 `typecheck`、`lint`、9 项 Vitest 和两个 Vite 构建全部通过。
- Playwright 全量回归 14 项通过；文档、YAML、OpenAPI、供应链、迁移/升级演练、离线缓存、容量基线和本地备份恢复门禁均通过。
