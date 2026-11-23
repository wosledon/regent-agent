# 19 记忆投毒与纠错

- 版本：v0.1（待评审）
- 范围：MemoryClaim、投毒检测、隔离、CorrectionTask、影响图、传播、申诉与指标
- 对应需求：FR-061、BR-062、BR-063、BR-064
- 对应缺口：G-32

## 1. 目标与边界

- 纠错以 claim 为单位，不静默改写整条记忆。
- 只隔离受影响内容，不关闭整个记忆系统。
- CorrectionTask 是正式任务，可认领、加权、验收。
- 原文、版本、证据和审计不可静默修改。
- 员工报告投毒不扣 KPI。

## 2. 数据模型

| 实体 | 关键字段 | 说明 |
|---|---|---|
| MemoryClaim | id、record_id、version、text、source_refs、confidence、verification、taint、status | 最小纠错单位 |
| MemoryEvidence | id、claim_id、source_ref、source_version、supports、hash | 支持或反证 |
| MemoryFinding | id、claim_id、rule_id、risk_level、evidence_refs、status | 投毒或冲突发现 |
| MemoryCorrection | id、old_claim_id、new_claim_id、reason、evidence_refs、reviewer_id、decision | 版本化纠错 |
| MemoryImpactRecord | id、claim_version_id、target_type、target_id、used_at、risk、required_action | 引用影响 |
| CorrectionTask | task_id、finding_id、claim_id、assignee_id、reviewer_id、status、weight | 正式纠错任务 |

已发布 claim 不可变；纠正和替代生成新版本。

## 3. Claim 来源

每个 claim 保存：

- 原始来源和版本。
- 证据和反证。
- 来源权威度。
- 独立来源数量。
- 验证次数和时间。
- taint 和 `derived_from`。
- 冲突 claim。
- 被引用位置。

多个来源若来自同一上游，只算一个独立来源。

## 4. 检测信号

- 来源不可信或带 taint。
- 缺少可验证证据。
- 同源重复伪装多来源。
- 与既有事实冲突。
- 短期异常灌入或引用增长。
- 摘要后置信度异常上升。
- 已知攻击模式或 canary 命中。
- 内容试图改变权限、审批或系统规则。
- 高影响 claim 没有独立复核。

检测生成 MemoryFinding，不直接删除或改写 claim。

## 5. 状态

```text
Active
Disputed
Quarantined
Corrected
Superseded
Stale
Archived
Purged
```

- Disputed：冲突，降权并复核。
- Quarantined：停止普通召回，保留证据。
- Corrected：新 claim 替代旧 claim。
- Superseded：事实更新，旧版本仅追溯。
- Purged：按删除策略彻底清除。

## 6. 发现与隔离

```text
MemoryFinding
  → 定位 claim 和版本
  → 高风险停止召回
  → 低风险降权复核
  → 创建 CorrectionTask
  → 通知直接负责人
```

- 不自动删除原文。
- 不修改历史版本。
- 不全局关闭 Memory。
- 不自动回滚全部相关任务。
- 隔离必须记录理由、范围、期限和复核人。

## 7. CorrectionTask

- 使用普通 Task、租约、WIP、权重和 DoneEvidence。
- 复核者与原始作者、来源提交者分离。
- 输入包括 claim、证据、冲突、Finding 和引用范围。
- 输出包括结论、证据、新版本和影响清单。
- 结论可以是确认、纠正、隔离、删除或证据不足。
- 高风险纠错需要独立评审和管理链确认。

纠错示例：

```text
old_claim: API 支持匿名访问
new_claim: API 必须携带租户 Token
evidence: 鉴权契约测试 + 生产审计
reviewer: 独立安全评审
result: Corrected
```

## 8. 影响分析

系统查询引用索引，生成 MemoryImpactRecord：

- 哪些员工检索过。
- 哪些 Task 和 TaskRun 使用过。
- 哪些摘要、知识库和图节点派生自它。
- 哪些 Decision、Review、发布使用过。
- 哪些外部动作已经执行。
- 使用时的 claim 版本和置信度。

无法确定影响范围时按高风险处理，不得静默忽略。

## 9. 影响处置

| 状态 | 处理 |
|---|---|
| 任务执行中 | 标记上下文失效，重新检索；高风险动作暂停 |
| 完成未验收 | 创建 Defect 或 ReviewTask |
| 已验收项目 | 创建回归任务，不自动回滚 |
| 已形成 Decision | 创建复审或 AdjudicationTask |
| 已执行外部动作 | 进入安全事件流程 |
| 跨公司或红线 | 升级 Controller 和 Boss |

只处理受影响 claim 和引用链，不牵连无关记忆。

## 10. 传播

```text
memory.correction.published
  → 停止旧 claim 召回
  → 更新向量和全文索引
  → 更新图关系
  → 清理缓存
  → 标记派生摘要
  → 重新计算任务上下文
  → 通知负责人
  → 写入审计
```

- 通过 Outbox 至少一次投递。
- 所有步骤幂等。
- 失败重试并告警。
- 传播完成前保留旧版本引用以便审计。

## 11. 申诉与复核

- 原提交者可提交反证。
- 复核者必须独立。
- 隔离必须有理由和期限。
- 证据不足不能永久封禁。
- 解封生成新事件，不删除隔离历史。
- 员工主动报告投毒不扣 KPI。
- 误隔离进入质量指标并触发规则校准。

## 12. 与 Q-019 的关系

- 不可信内容进入记忆时保留 taint。
- 摘要、组织记忆和知识库提升不能洗白。
- CorrectionTask 受 TaskGrant 和权限约束。
- 记忆内容不能授权 Tool 或审批。
- 被隔离来源不能通过换摘要或换任务绕过。

## 13. 指标

| 指标 | 目标 |
|---|---|
| 投毒检测时间 | 持续下降 |
| 可疑 claim 召回暴露 | 0 |
| 纠错任务完成时间 | 建立基线后校准 |
| 纠正传播延迟 | 持续下降 |
| 受影响任务遗漏 | 0 |
| 误隔离率 | 持续下降 |
| 重复投毒率 | 持续下降 |
| 无证据高置信 claim | 0 |

## 14. API 与 Tool

- `POST /memory/findings`：提交发现。
- `POST /memory/findings/{id}/quarantine`：隔离。
- `POST /memory/corrections`：创建纠错任务。
- `GET /memory/claims/{id}/impact`：查询影响。
- `POST /memory/corrections/{id}/approve`：批准新版本。
- `POST /memory/findings/{id}/appeal`：申诉。
- MCP：`memory.report`、`memory.correct`、`memory.impact`。

普通员工不能直接删除或改写组织记忆。

## 15. 测试场景

1. 网页投毒进入个人记忆。
2. 伪造引用被识别。
3. 多来源实际同源不提高独立性。
4. 摘要不提升高于来源的置信度。
5. 冲突 claim 同时召回时提示冲突。
6. 已发布知识被版本化纠正。
7. 派生摘要和图关系同步失效。
8. 在途任务重新检索。
9. 已完成任务创建回归而不自动回滚。
10. 隔离申诉和解封保留历史。
11. 删除和备份保留策略一致。
12. 跨租户和权限泄漏被拒绝。
13. CorrectionTask 作者不能自审。
14. 传播失败可重试且不重复副作用。
15. 员工报告投毒不影响 KPI。

## 16. 验收场景

1. 每个 claim 都能追溯到来源和版本。
2. 高风险 Finding 立即停止召回。
3. 低风险 Finding 只降权并进入复核。
4. CorrectionTask 可认领、加权和验收。
5. 纠错生成新版本，旧版本不可静默修改。
6. 影响记录覆盖任务、摘要、决策和外部动作。
7. 传播失败不影响其他正确记忆。
8. 误隔离可以申诉和解除。
9. 已完成工作不会因纠错自动回滚。
10. 所有隔离、纠正、传播和通知均审计。
