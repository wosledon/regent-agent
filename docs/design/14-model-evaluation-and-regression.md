# 14 模型评测与回归

- 版本：v0.1（待评审）
- 范围：公司内评测复用、数据集版本、密封防泄漏、回归调度、评分与质量门槛
- 对应需求：FR-056、BR-048、BR-049
- 对应缺口：G-22

## 1. 目标与边界

模型评测为招聘、岗位绑定、模型升级、技能公共化和绩效提供可复现证据。

- 评测结果在同一公司内复用，跨租户默认不共享。
- 评测数据集、提示词、工具、运行时和评分策略全部版本化。
- 密封集与生产上下文隔离，评测用例不进入记忆、Skill 或 Prompt。
- 评测通过不自动绑定员工，失败不自动触发处罚。
- 具体密封集设计、污染检测算法、Judge 校准样本和统计显著性门槛由 Q-052 继续讨论。

## 2. 数据模型

| 实体 | 关键字段 | 说明 |
|---|---|---|
| BenchmarkSuite | id、tenant_id、name、capability_dimensions、visibility、version、status | 逻辑评测集 |
| DatasetVersion | id、suite_id、version、content_hash、source_ref、license、redaction_status、split | 不可变数据集 |
| BenchmarkCase | id、dataset_version_id、case_hash、difficulty、risk_level、expected_evidence、sealed | 单个案例 |
| ScoringRubric | id、version、dimensions、weights、hard_fail_rules、judge_prompt_ref | 评分规则 |
| EvaluationRun | id、suite_id、dataset_version_id、model_version_id、prompt_bundle_id、tool_profile_id、runtime_version、reasoning_effort、seed、status | 一次评测执行 |
| CaseResult | id、run_id、case_id、verdict、score_vector、evidence_refs、judge_ref、latency、cost | 案例结果 |
| EvaluationSummary | id、run_id、metrics、confidence_intervals、gate_results、baseline_run_id | 汇总与门槛判断 |
| EvaluationApproval | id、run_id、approver_id、decision、reason、expires_at | 审批记录 |

所有已发布对象不可静默修改；更新生成新版本并引用旧版本。

## 3. 复用键

```text
provider_model_version
+ reasoning_effort
+ prompt_bundle_version
+ tool_profile_version
+ agent_runtime_version
+ benchmark_suite_version
+ dataset_version
+ scoring_policy_version
+ seed
```

- 键完全匹配才可复用 EvaluationSummary 和 CaseResult。
- 模型别名、不同数据集或不同 Judge 不能复用。
- 公司内岗位共享核心结果，只运行岗位专属补充集。
- 运营方公共评测必须匿名、脱敏、许可明确并单独审核。

## 4. 数据集与防泄漏

1. 数据集记录来源、许可证、内容 hash、脱敏和访问范围。
2. 密封集只允许评测服务读取，不向员工、模型和普通界面暴露。
3. 原始输入、答案和 Judge 理由按最小权限保存。
4. 评测案例不进入生产记忆、Skill、Prompt 或向量索引。
5. 公开数据集执行近似重复、已知污染和 canary 检查。
6. 数据策展人与被评估员工分离。
7. 数据集发布前进行隐私、版权和安全审查。
8. 历史版本保留，可复现当时评测。

## 5. 评测类型

| 类型 | 触发 | 主要证据 |
|---|---|---|
| 模型能力 | 新模型版本 | 核心集、安全集、成本与延迟 |
| 岗位适配 | 新员工绑定或调岗 | 岗位补充集、历史交付 |
| 招聘白板 | 候选人评估 | 中立案例、产出和评分理由 |
| 技能回归 | Prompt/Skill/Tool 变更 | 受影响能力集 |
| 项目回归 | 发布候选 | 项目关键场景 |
| 事故回归 | 缺陷或安全事件 | 对应失败案例和相邻回归 |

## 6. 调度频率

| 触发 | 执行范围 |
|---|---|
| 新模型版本 | 完整核心集与安全集 |
| 新员工绑定 | 岗位相关集 |
| 每日 | 小型 canary 冒烟集 |
| 每周 | 核心回归集 |
| 每月 | 完整回归与漂移检查 |
| Prompt/Skill/Tool 变更 | 受影响能力集 |
| 生产事故 | 缺陷案例与相邻回归 |

评测任务使用独立预算和队列，不抢占生产任务的硬性资源保证。

## 7. 执行流程

```text
创建 EvaluationRun
  → 锁定模型、Prompt、Tool、Runtime、数据集和评分版本
  → 预留评测预算
  → 隔离沙箱执行
  → 确定性测试与规则评分
  → LLM Judge / 人工抽检
  → 汇总指标和门槛
  → Approved / Rejected / Quarantined
```

- 随机任务固定 seed，并至少连续运行 3 次。
- 流式中断、工具失败和环境故障与能力失败分开记录。
- 同一案例的重复执行用于识别 Flaky 结果。
- 无法稳定复现的结果不能作为唯一通过依据。

## 8. 质量门槛

| 指标 | 初始门槛 |
|---|---:|
| 安全、租户隔离、越权案例 | 100% |
| P0/P1 失败 | 0 |
| 核心案例 | 100% |
| 整体通过率 | ≥80% |
| 单能力维度 | ≥70% |
| 关键指标相对基线下降 | ≤5 个百分点 |
| 单维度相对基线下降 | ≤10 个百分点 |
| Judge 与人工一致率 | ≥85% |

- 延迟和成本必须符合岗位预算。
- 硬失败不能被总分或加权评分抵消。
- 报告通过率、样本量、波动范围和不稳定案例。
- 统计显著性细则由 Q-052 决定。

## 9. 评分与 Judge

优先级：

```text
确定性测试 → 规则评分 → LLM Judge → 人工抽检
```

- Judge 使用独立模型或服务，不让被评模型给自己打分。
- Judge 版本、Prompt、示例和评分理由全部记录。
- 主观维度进行人工校准，并监控 Judge 与人工一致率。
- 评分规则在运行前冻结，不得看到结果后修改。
- Judge 失败或不可复现时进入复核，不默认通过。

## 10. 结果使用

- Approved 只表示评测门槛通过，不自动创建员工绑定。
- Rejected 或 Quarantined 模型不得用于新绑定。
- 在途任务不因评测结果自动换模型。
- 招聘、调岗、技能公共化和模型升级必须引用 EvaluationRun。
- 样本不足或结果不稳定时不生成高置信度绩效结论。
- 所有审批、申诉和例外均审计。

## 11. API 与 Tool

- `POST /evaluations/runs`：创建评测。
- `GET /evaluations/runs/{id}`：查看状态和汇总。
- `POST /evaluations/runs/{id}/approve`：审批结果。
- `POST /benchmark-suites/{id}/versions`：发布新数据集版本。
- `POST /evaluations/runs/{id}/retest`：复测失败案例。
- MCP：`evaluation.run`、`evaluation.status`、`evaluation.approve`。
- 密封案例和原始输出不通过普通员工 Tool 暴露。

## 12. 指标

- 核心能力覆盖率。
- 评测结果复用率。
- 数据集污染事件数，目标 0。
- 回归逃逸率，初始 ≤5%。
- Judge 与人工一致率，初始 ≥85%。
- Flaky 案例率。
- 单次成功成本。
- 岗位专属重复评测率。
- 评测结果新鲜度。

## 13. 验收场景

1. 完全匹配的评测结果可以在公司内复用。
2. 模型版本变化后不能复用旧结果。
3. 密封集不会出现在生产 Prompt 或记忆中。
4. 数据集更新生成新版本并保留旧结果。
5. 新模型必须通过完整核心集和安全集。
6. 安全和租户隔离失败一票否决。
7. Prompt、Skill 或 Tool 变化触发受影响回归。
8. 连续 3 次结果波动被记录。
9. Judge 版本变化后旧评分不能直接复用。
10. 评测通过不会自动绑定员工。
11. 评测失败不会自动触发处罚。
12. 跨租户评测数据默认不可见。
13. 公共评测必须经过脱敏和许可审核。
14. 评测预算与生产预算分账。
15. 原始输出仅授权评估者和审计可见。
16. Q-052 未决细则不会被静默假定。
