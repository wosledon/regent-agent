# 21 PII 与秘密脱敏

- 版本：v0.1（待评审）
- 范围：数据分类、秘密隔离、PII 令牌化、DLP、存储、审计、删除与指标
- 对应需求：FR-063、BR-068、BR-069、BR-070
- 对应缺口：G-34

## 1. 目标与边界

- 秘密只存在于密钥系统，业务系统只保存 `secret_ref`。
- PII 默认最小化、遮蔽、令牌化、假名化或匿名化。
- 所有输入、出站和持久化边界执行 DLP。
- 审计只保存处置元数据，不保存敏感原文。
- 删除传播到所有副本和映射，法律保留与备份保留期显式处理。
- 用户输入和既有知识产权仍归原权利人，租户产出归属见 Q-034。
- 误报不得阻断安全任务，员工不能自行关闭 DLP。

## 2. 数据分类

| 级别 | 示例 | 默认策略 |
|---|---|---|
| Public | 公开文档 | 可直接使用 |
| Internal | 一般业务内容 | 租户内使用 |
| Confidential | 源码、合同、面试记录、候选人评分 | 加密、限权、审计 |
| Restricted | 密钥、Token、证件、支付、健康数据 | 禁止普通存储和模型出站 |

PII 包括直接标识、联系方式、证件、金融、位置、健康和生物特征等。

## 3. SensitiveDataPolicy

```text
policy_id
tenant_id
version
data_classes[]
pii_rules[]
secret_rules[]
model_egress_policy
allowed_exceptions[]
effective_from
```

- 策略版本化并审计。
- 租户可提高 PII 保护，不能关闭秘密保护。
- TaskRun 和 Artifact 记录使用的策略版本。
- 策略变化只影响新任务，在途任务按原策略安全结束或暂停。

## 4. DLP 边界

```text
API 输入
聊天和消息
Tool 参数和结果
模型调用出站
记忆捕获
Artifact 上传
日志和 Trace
导出和通知
```

检测方式：

- Provider 和密钥格式规则。
- 私钥、连接串、密码、恢复码和 OAuth Token。
- 邮箱、电话、证件、地址和支付数据。
- 高熵字符串。
- 编码、拆分、拼接和跨字段泄漏。
- PDF、图片和截图 OCR。

## 5. DlpFinding

```text
finding_id
tenant_id
source_type
source_ref
policy_version
rule_id
data_class
risk_level
action
evidence_hash
status
created_at
```

- Finding 不保存敏感原文。
- 证据只保存 hash、位置和脱敏片段。
- 高置信秘密立即阻止。
- PII 默认遮蔽并允许继续安全任务。
- 复核和例外绑定范围与到期时间。

## 6. 秘密处置

- 秘密只进入网关或密钥系统。
- 业务系统只保存 `secret_ref`。
- 高置信秘密阻止发送和持久化。
- 已泄漏秘密生成安全事件并评估轮换。
- 不把秘密写入聊天、记忆、Prompt、日志、Trace、Artifact 或审计正文。
- Tool 需要第三方凭据时使用短期、最小权限凭据。

## 7. PII 处置

| 方式 | 用途 | 可逆性 |
|---|---|---|
| Masking | 界面显示 | 不可逆展示 |
| Tokenization | 任务内关联 | 映射可逆，严格限权 |
| Pseudonymization | 开发测试、分析 | 受限可逆 |
| Anonymization | 统计和公共数据 | 不可逆 |

- 令牌在同一租户和范围内稳定。
- 跨任务、跨员工默认不复用。
- 映射表独立加密保存。
- 删除 PII 时同步删除映射。
- 模型只接收完成任务必需的字段。

## 8. TokenMapping

```text
token_id
tenant_id
scope
data_type
encrypted_value_ref
policy_version
created_at
expires_at
```

- 原文与令牌分离存储。
- 只有显式角色可以解析。
- 解析操作记录审计。
- 法律保留期间禁止删除。
- 映射不得进入向量、全文或普通日志。

## 9. 模型出站

- 秘密永不发送给第三方 Provider。
- PII 默认不出租户，确需发送必须显式授权。
- 出站前执行 DLP 和字段最小化。
- Provider 请求、响应和缓存遵守同一政策。
- 错误信息和重试日志不得泄漏原文。

## 10. 存储

- Postgres Restricted 字段使用信封加密和最小权限。
- RustFS 对象静态加密，对象键不包含 PII。
- 向量、全文、图索引只存脱敏内容。
- 备份加密，密钥分离保管。
- 原始 PII 与普通业务投影分离。
- 供应链制品扫描阻止秘密进入镜像和包。

## 11. 审计

只记录：

```text
rule_id
field_type
object_id
action
timestamp
actor
hash_or_token
```

不记录：

- 秘密原文。
- 完整 PII。
- 未脱敏请求体。
- 可逆映射。

历史审计发现敏感数据时隔离记录并启动事件流程，不静默改写。

## 12. 删除传播

```text
停止使用并立即软删除
  → 移除向量/全文/图、缓存和派生摘要
  → 隔离 RustFS 和 Postgres 原文
  → 撤销密钥和令牌映射访问
  → 恢复窗口结束后物理清除
  → 更新备份保留计划
  → 输出删除证明
```

- 使用 Q-020 的引用影响图定位派生内容。
- 软删除立即停止召回，权威数据在恢复窗口内加密隔离。
- 所有步骤幂等、可重试、可审计。
- 法律保留期间禁止物理清除，但不恢复访问。
- 备份副本按保留期到期处理并重放 tombstone。
- 删除证明包含范围、时间、结果和未清除的合法保留项。

## 13. 例外与申诉

- 高置信秘密默认阻止。
- PII 误报默认遮蔽并允许继续安全任务。
- 例外绑定字段、范围、原因、负责人和到期时间。
- 员工不能自行关闭 DLP。
- 员工主动报告泄漏不处罚。
- 误报复核生成规则校准记录。

## 14. 指标

| 指标 | 目标 |
|---|---|
| 秘密泄漏次数 | 0 |
| 未脱敏 PII 进入模型 | 0 |
| DLP 覆盖率 | 100% |
| 误报率 | 持续下降 |
| 秘密轮换时长 | 持续下降 |
| 删除传播延迟 | 持续下降 |
| 历史敏感对象发现数 | 持续下降 |
| 员工主动报告率 | 持续提升 |

## 15. API 与 Tool

- `GET /sensitive-data/policies`：查看政策。
- `POST /sensitive-data/findings`：提交发现。
- `POST /sensitive-data/findings/{id}/review`：复核。
- `POST /sensitive-data/exceptions`：申请限期例外。
- `POST /sensitive-data/deletions`：发起删除传播。
- `GET /sensitive-data/deletions/{id}`：查看删除证明。
- 普通员工不能查看令牌映射或关闭 DLP。

## 16. 测试场景

1. API Key、私钥、连接串和密码被阻止。
2. 编码、拆分和跨字段秘密被识别。
3. 邮箱、电话、证件、支付和健康数据被脱敏。
4. PDF、图片和截图 OCR 被扫描。
5. 日志、Trace、错误和审计不保存原文。
6. 记忆、向量、Artifact 和导出只存脱敏内容。
7. 模型出站前拦截秘密和未授权 PII。
8. 删除传播到索引、缓存、遥测和映射。
9. 法律保留项不被提前删除。
10. 误报豁免到期后自动失效。
11. 已泄漏秘密触发轮换和通知。
12. 员工不能自行关闭 DLP。

## 17. 验收场景

1. 秘密只存在于密钥系统。
2. 业务记录只包含 secret_ref。
3. PII 在模型上下文中已令牌化或遮蔽。
4. 审计不包含秘密或完整 PII。
5. DLP 覆盖所有指定边界。
6. 误报不阻断安全任务。
7. 例外具有范围、负责人和到期时间。
8. 删除证明覆盖所有副本和映射。
9. 法律保留和备份保留期被明确记录。
10. 员工报告泄漏不影响 KPI。
