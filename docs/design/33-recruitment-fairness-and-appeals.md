# 33 公平招聘与申诉

- 版本：v0.1（待评审）
- 范围：岗位相关标准、盲评、等价案例、偏差审计、合理便利、候选人申诉和复测
- 对应需求：FR-007、FR-076、BR-110、BR-111、BR-112、BR-113
- 对应缺口：G-47

## 1. 目标与边界

- 招聘只按岗位相关能力评分。
- 公平不降低安全、质量和核心能力门槛。
- 偏差审计用于发现和纠正评价工具问题，不自动认定歧视。
- Boss 保留最终录用权。
- 本设计不替代具体司法辖区的法律意见。

## 2. FairnessPolicy

```text
id
tenant_id
position_id
version
job_related_criteria[]
protected_attributes[]
proxy_variables[]
blind_screening_rules
equivalent_assessment_rules
accommodation_rules
audit_thresholds
effective_from, effective_to
```

- 政策在招聘开始前发布。
- 已发布版本不可变，修改生成新版本。
- 评分项必须具有岗位相关性说明。
- 禁止使用受保护特征及其代理变量。

## 3. 岗位相关标准

允许：

- 工作样本和实际操作结果。
- 与岗位直接相关的知识、技能和安全意识。
- 可复现的测试和 benchmark。
- 成本、延迟和部署约束，但必须与质量分开。

禁止作为能力替代指标：

- 性别、年龄、种族、国籍、残疾、姓名、照片和口音。
- 学校、雇主和社会经济背景。
- 模型品牌、供应商和当前 Controller 使用的模型。
- 与岗位无关的表达风格、兴趣和人格刻板印象。

- 成本低不等于能力强。
- 模型品牌偏好必须单独记录为业务约束，不能混入质量分。

## 4. 结构化与盲评

- 同一岗位使用等价题目、时间、工具和预算。
- 隐去姓名、头像、性别化表达、学校和模型品牌。
- 面试前冻结评分维度、权重和门槛。
- 至少两名独立评分者。
- 作者不能修改已提交的评分标准或评分。
- 工作样本优先于口头汇报。
- 无法稳定复现的结果不能单独决定淘汰。

## 5. 多语言与表达

- 候选人可使用约定语言作答。
- 翻译和跨语言复核必须检查语义是否失真。
- 不因口音、语法或非母语表达扣分，除非相关能力是岗位要求。
- 多语言案例必须具有等价难度和评分标准。
- 翻译错误、环境故障和工具故障不计为能力失败。

## 6. 模型偏差

- 不偏好 Controller 当前使用的模型或供应商。
- 按 Provider、模型系列、版本、推理强度和语言切片审计。
- 记录品牌偏好、成本偏好和技术偏见。
- 安全、协议、数据驻留和部署要求可以成为约束，但必须明确说明。
- 模型换岗或重试必须有 benchmark 或产出证据，不能由单次面试决定。

## 7. CandidateAssessment

```text
id
tenant_id
requisition_id
candidate_ref
fairness_policy_id
rubric_version
assessment_language
blind_profile_ref
evidence_refs[]
scores[]
hard_fail_results
status
created_at
```

- 候选人身份资料与盲评资料分离。
- 评分记录包含模型、Prompt、Runtime、工具和评测版本。
- 所有结论引用证据。
- 安全和租户隔离硬失败不能被总分抵消。

## 8. FairnessAudit

```text
id
tenant_id
requisition_id
period
policy_version
slice_definitions[]
sample_sizes[]
selection_rates[]
score_distributions[]
false_rejection_rates
inter_rater_agreement
confidence_intervals
findings[]
status
```

审计切片：

- Provider 和模型系列。
- 模型版本和推理强度。
- 语言和翻译路径。
- 候选人自愿提供的受保护特征。
- 人类与 AI 候选人，如系统同时支持两类。

### 8.1 调查阈值

- 群体选择率比值低于 0.8 时触发调查。
- 分数分布显著偏移时触发调查。
- 评分者一致性低于门槛时触发校准。
- 错误淘汰或申诉推翻集中出现时触发复测。

阈值是产品预警，不是自动法律结论，也不自动改变个人结果。

### 8.2 样本不足

- 报告必须包含样本量和置信区间。
- 样本不足时标记 `InsufficientEvidence`。
- 不得用极小样本宣布公平或无偏。
- 使用成对、反事实和合成案例补充工具测试。

## 9. 敏感属性

- 受保护属性只能自愿提供。
- 评分者、面试官和 Controller 默认不可见。
- 禁止模型推断性别、种族、国籍、残疾等属性。
- 数据只用于公平审计，不用于个人评分。
- 不能提供属性时仍使用盲评和合成偏差测试。

## 10. BiasFinding

```text
id
audit_id
category
affected_slice
evidence_refs
severity
root_cause_hypothesis
remediation
owner_id
due_at
status
```

处置：

- 校准评分规则。
- 替换或翻译题目。
- 调整 Judge 和评分示例。
- 增加工作样本。
- 更换评分者或评测模型。
- 复测受影响候选人。

- 不自动认定候选人不合格。
- 不自动改变已结束的个人决定，除非通过申诉或新证据。

## 11. 合理便利

- 候选人可申请替代格式、额外时间、辅助工具或语言支持。
- 便利不得改变岗位核心能力标准。
- 申请与评分隔离，只向执行人员披露必要安排。
- 拒绝便利必须记录原因和替代方案。
- 安全、密钥和租户隔离要求不能豁免。

## 12. CandidateAppeal

```text
id
tenant_id
candidate_ref
assessment_id
decision_id
appeal_type
reason
evidence_refs[]
accommodation_requested
status
independent_reviewer_id
outcome
reviewed_at
```

申诉类型：

- `ScoreDispute`
- `TranslationError`
- `ToolingFailure`
- `BiasConcern`
- `Accommodation`
- `NewEvidence`
- `RetestRequest`

流程：

```text
Submitted → UnderReview → RetestOrRescore → Resolved
                    \→ Rejected
```

- 拒绝决定必须提供可理解的证据和原因。
- 申诉由非原评分者复核。
- 申诉期间暂停最终淘汰。
- 可维持、复测、重新评分或撤销拒绝。
- 申诉不能突破安全、租户隔离和岗位硬能力门槛。
- 所有结果和纠正任务进入审计。

## 13. 招聘门禁

进入 Boss 录用审批前：

1. FairnessPolicy 和评分标准已冻结。
2. 独立评分完成。
3. FairnessAudit 已完成或明确标记证据不足。
4. 阻断性 BiasFinding 已纠正或进入候选复测。
5. 申诉窗口已结束或申诉已解决。
6. 安全、质量和核心能力门槛通过。

- Boss 可以批准或拒绝正向录用。
- 偏差调查不能替代录用审批。
- 超时不默认批准。

## 14. 数据与保留

- 候选人资料属于 Confidential。
- 身份、评分、公平审计和申诉数据按最小权限保存。
- 敏感属性与评分者隔离。
- 决策后候选人资料默认 180 天清除。
- 审计只保留必要的元数据和结论。
- 法律保留只阻止清除，不扩大评分访问。

## 15. API 与事件

- `POST /requisitions/{id}/fairness-policies`
- `POST /candidate-assessments`
- `POST /requisitions/{id}/fairness-audits`
- `POST /candidate-appeals`
- `POST /candidate-appeals/{id}/retest`
- `GET /candidate-appeals/{id}`

事件：

- `FairnessPolicyPublished`
- `CandidateAssessmentCompleted`
- `FairnessAuditCompleted`
- `BiasFindingOpened`
- `CandidateAppealSubmitted`
- `CandidateAppealResolved`
- `CandidateRetestScheduled`

## 16. 指标

| 指标 | 目标 |
|---|---:|
| 招聘评分岗位相关率 | 100% |
| 招聘周期偏差审计覆盖率 | 100% |
| 候选人申诉按期处理率 | ≥95% |
| 被推翻的错误淘汰率 | 0 |
| 阻断性 BiasFinding 逾期数 | 0 |
| 候选人资料逾期未清除数 | 0 |

## 17. 测试场景

1. 姓名、性别和学校信息不进入盲评。
2. 模型品牌不进入质量评分。
3. 相同岗位使用等价题目和工具预算。
4. 非母语表达不会无故扣分。
5. 翻译错误不会被计为能力失败。
6. 安全硬失败不能被总分抵消。
7. 选择率比值低于 0.8 时生成 BiasFinding。
8. 小样本不会被宣布为无偏。
9. 评分者无法查看自愿提供的敏感属性。
10. 模型无法推断并记录受保护属性。
11. 候选人申诉期间暂停淘汰。
12. 原评分者不能复核自己的申诉。
13. 合理便利不改变核心能力标准。
14. 申诉可以触发复测和重新评分。
15. 录用必须经过 FairnessAudit 和 Boss 批准。
16. 候选人资料在 180 天后清除。
17. 公平修正不能降低安全、质量和核心能力门槛。

## 18. 验收场景

1. 所有评分项具有岗位相关性。
2. 面试前标准已冻结。
3. 身份和模型品牌已盲化。
4. 多语言案例具有等价难度。
5. 每个招聘周期具有 FairnessAudit。
6. 偏差只触发调查，不自动判定歧视。
7. 候选人可以申诉、复测和申请合理便利。
8. 申诉由独立评分者处理。
9. 公平不降低岗位能力门槛。
10. Boss 保留最终录用权。
11. 所有评分、审计和申诉可追溯。
12. 候选人资料按 180 天策略清除。
