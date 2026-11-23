# ADR-0011：合规通过 ComplianceProfile 配置并 fail closed

- 状态：Accepted
- 日期：2026-11-23
- 范围：企业合规策略

## 背景

不同租户有不同地区、行业和数据控制要求，但平台不宣称认证或提供法律意见。

## 决策

- 租户通过 ComplianceProfile 配置合规要求。
- 支持 baseline、strict 和自定义预设。
- 可配置 Provider/区域、数据分级、保留、审计、备份和外部共享。
- 必需控制不满足时 fail closed。
- 策略版本化，激活需审批，历史任务保留生效版本。
- 不宣称 SOC 2、ISO 27001 或等保认证。

## 结果

- 合规控制可执行、可审计。
- 技术证据不等于认证。
- 商业 SLA 和专属支持仍后置。

## 未采用

- 固定合规模式：无法适应不同租户。
- 认证声明先行：缺乏实际证据和法律依据。

## 关联

- [45 企业合规与支持边界](../design/45-enterprise-compliance-and-support-boundary.md)
