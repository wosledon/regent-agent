# ADR-0009：Judge 作为独立员工运行时

- 状态：Accepted
- 日期：2026-11-23
- 范围：模型评测与 Judge 隔离

## 背景

Judge 需要正式任务、预算和审计，但不能与候选员工共享上下文或自评。

## 决策

- Judge 是具备 `evaluation.judge` 能力的正式员工。
- 使用独立 EmployeeRuntime 和评测上下文。
- 优先绑定与候选员工不同的 Provider、模型或版本。
- 只配置一个模型时，允许同模型不同运行时。
- 禁止 Judge 评审自己的产出、读取候选员工记忆/聊天/工作区或读取密封题。
- 同模型 Judge 标记 `SameModelJudge`，高影响场景增加人工复核。

## 结果

- Judge 消耗进入正式成本和审计。
- 模型可相同，但运行时和上下文必须独立。
- 需要 JudgeCalibration 和污染检测保证可信度。

## 未采用

- 主控直接充当 Judge：存在角色冲突和上下文污染。
- 被评模型自评：无法保证独立性。

## 关联

- [14 模型评测与回归](../design/14-model-evaluation-and-regression.md)
- [46 模型评测 QA 实施细则](../design/46-evaluation-qa-implementation.md)
