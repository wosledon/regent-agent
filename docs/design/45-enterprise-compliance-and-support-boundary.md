# 45 企业合规与支持边界

- 版本：v0.1（待评审）
- 范围：源码私有部署、可配置合规策略、技术证据、支持边界和认证声明
- 对应需求：FR-089、BR-156、BR-157、BR-158、BR-159、BR-160
- 对应缺口：G-60

## 1. 当前范围

- 用户在自己的服务器、内网或 VPC 中按版本 Tag 源码部署。
- 租户可配置合规策略和技术控制。
- 保留审计、删除、DLP、供应链和安全事件证据。
- 当前仅提供公开 GitHub Issue 和文档支持。

当前不提供：

- 托管式 BYOC 或专属 VPC。
- 跨区域复制、驻留编排或区域切换。
- 企业部署团队或迁移协助。
- SOC 2、ISO 27001、等保或其他认证声明。
- 自动客户合规审计包。
- 法律意见。
- 商业 SLA、24×7 值班、专属客户经理或固定支持时限。

## 2. 私有部署与数据驻留

- 部署位置由用户选择。
- 数据存放在用户选择的 Postgres、RustFS 和本地卷。
- 用户自行选择模型 Provider 和区域。
- 发送到云端模型的 Prompt、工具参数和输出仍会离开本机。
- 不承诺“数据永不离开本机”或“完全离线 AI”。
- 不提供跨区域数据同步或自动区域切换。

## 3. ComplianceProfile

```text
ComplianceProfile:
  id, tenant_id, name
  preset: baseline | strict | custom
  status: draft | active | suspended
  version, effective_at
  jurisdictions[]
  industry_requirements[]
  allowed_providers[]
  allowed_models[]
  allowed_regions[]
  data_classification_policy_ref
  retention_policy_ref
  audit_profile: standard | worm | anchored
  backup_requirement
  external_sharing_policy_ref
  required_controls[]
  created_by, approved_by
```

- 只有 Owner 或有效治理委托人可激活策略。
- 策略必须版本化，激活前完成差异预览和影响评估。
- 旧版本保留，历史任务记录实际生效版本。
- baseline 使用平台默认技术控制。
- 租户首次读取或更新 `ComplianceProfile` 时，控制面按认证租户原子创建确定性 baseline：允许 OpenAI/Anthropic、区域 `global`、在线保留 180 天、`standard` 审计、不要求备份、不允许外部共享、版本 1、状态 `Active`；重复读取保持稳定，后续更新递增版本。
- strict 提高审计、备份、DLP 和 Provider/区域限制。
- custom 允许租户按实际要求组合控制，但不能关闭安全红线。

## 4. 可配置控制

| 控制域 | 可配置内容 |
|---|---|
| 地区与行业 | 适用地区、行业要求、生效时间 |
| Provider | 允许的 Provider、模型、端点和区域 |
| 数据分级 | Public/Internal/Confidential/Restricted 规则 |
| 保留与删除 | 在线、归档、清除和法律保留 |
| 审计 | standard/worm/anchored、检查点和验证频率 |
| 备份 | 是否必需、组件、保留和演练要求 |
| 外部共享 | 允许的接收方、动作、字段和数据类别 |
| 支持访问 | SupportGrant 范围、时长和审批要求 |
| 内容安全 | 地区规则、年龄限制和申诉策略 |

- 配置不得覆盖租户隔离、秘密保护、审批和审计不变量。
- 配置只能收紧或明确控制，不能通过文本声明绕过执行层策略。

## 5. 必需控制与 fail closed

- 每个控制标记 `required` 或 `advisory`。
- 必需控制必须有有效证据来源。
- 模型调用前检查 Provider、模型、区域、数据类别和预算。
- 任务启动前检查数据分级、Tool、网络和外部动作范围。
- 发布和外部动作前检查内容安全、权利和共享策略。
- 备份、审计或删除控制失效时，相关动作被拒绝或暂停。
- 不允许静默降级为 baseline。

状态：

```text
Compliant
  → Degraded（咨询项或证据过期）
  → NonCompliant（必需项失败）
  → Suspended（策略暂停）
```

`NonCompliant` 对受影响动作 fail closed，不影响无冲突的其他租户和无关任务。

## 6. 证据与报告

技术证据包括：

- 审计验证报告和检查点。
- 删除证明和 tombstone 状态。
- DLP 统计和例外记录。
- SupportGrant 访问日志。
- SBOM、签名、漏洞和许可证报告。
- 备份配置、作业和恢复演练结果。
- 安全事件、取证和复盘。
- ComplianceProfile 版本和违规记录。

当前不自动生成客户专属合规审计包，也不宣称证据等于认证。租户可导出其授权范围内的技术证据。

## 7. 支持访问

- 自托管默认没有外部运营人员访问。
- 托管支持必须使用 SupportGrant。
- Grant 限时、限范围、可撤销、可审计，默认只读。
- 写操作需租户明确批准，紧急访问双人审批。
- 不能读取模型密钥或绕过红线。
- SupportGrant 是技术授权，不构成商业支持或 SLA 承诺。

## 8. 商业支持边界

- 工程 SLO 继续作为内部质量目标。
- 不把 SLO 表述为合同 SLA。
- 不提供赔付、24×7 值班、专属客户经理或固定响应时限。
- 当前支持渠道为公开 GitHub Issue 和官方文档。
- 专属支持、私有培训、SLA 和迁移协助作为未来商业服务。
- 本地套餐、配额与订阅账本已实现；真实支付、生产定价、税务和退款继续延后。

## 9. 实体

- `ComplianceProfile`：租户合规策略版本。
- `ComplianceControl`：单项控制、要求、状态和证据来源。
- `ComplianceEvaluation`：动作或时间点的策略评估结果。
- `ComplianceViolation`：违规事实、影响、处置和申诉。
- `ComplianceEvidenceExport`：授权技术证据导出记录。

## 10. API

- `GET /compliance/profile`：读取当前策略；租户尚无策略时原子创建并返回确定性 baseline，不返回空 200。
- `PUT /compliance/profile`：按当前认证租户和版本更新策略；请求体中的租户标识不改变资源归属。
- `POST /compliance/profile/draft`：创建草稿。
- `POST /compliance/profile/evaluate`：预览差异和影响。
- `POST /compliance/profile/activate`：激活版本。
- `GET /compliance/evaluations`：查询评估。
- `GET /compliance/violations`：查询违规。
- `POST /compliance/violations/{id}/appeal`：申诉。
- `POST /compliance/evidence-exports`：导出授权证据。
- 普通员工不能修改 ComplianceProfile。

## 11. 指标

| 指标 | 目标 |
|---|---:|
| 合规必需控制覆盖率 | 100% |
| 合规策略违规放行数 | 0 |
| 证据过期未处理数 | 0 |
| 未审批策略激活数 | 0 |
| 越权合规证据导出数 | 0 |
| 认证或 SLA 误导声明数 | 0 |

## 12. 测试场景

1. Owner 可以创建并激活 baseline 策略。
2. strict 策略提高审计、备份和 Provider 限制。
3. custom 策略不能关闭安全红线。
4. 未审批策略不能激活。
5. Provider 不在允许列表时模型调用被拒绝。
6. 模型区域不符合驻留要求时调用被拒绝。
7. 数据类别超过共享范围时外部动作被拒绝。
8. 必需备份未启用时受控发布被拒绝。
9. 必需审计证据过期时相关动作暂停。
10. 非合规状态不会静默降级为 baseline。
11. 无关任务不受影响。
12. 历史任务保留实际策略版本。
13. 违规记录包含影响、证据和处置。
14. 租户可申诉误报。
15. 普通员工不能修改策略。
16. 证据导出经过授权和审计。
17. 自托管默认无外部 SupportGrant。
18. 平台不返回认证或商业 SLA 声明。

## 13. 验收场景

1. 合规策略可配置、版本化并经过审批。
2. 必需控制不满足时 fail closed。
3. 技术证据可追溯，但不等于认证。
4. 数据驻留边界和云端模型外发明确可见。
5. 不提供商业 SLA、专属支持或自动合规审计包。
6. 当前仅提供公开社区和文档支持。
