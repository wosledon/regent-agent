# 32 产出权利与来源

- 版本：v0.1（待评审）
- 范围：租户产出归属、平台许可、第三方权利、来源记录、人类贡献和发布门禁
- 对应需求：FR-074、BR-103、BR-104、BR-105
- 对应缺口：G-45

## 1. 目标与边界

- 租户任务产出归租户所有。
- Regent 不主张业务产出所有权，只保留有限运营许可。
- 第三方权利、提供商条款和版权可保护性必须单独判断。
- 本设计是产品政策，不替代具体司法辖区的法律意见。

## 2. 权利分层

| 层 | 默认政策 |
|---|---|
| 租户业务产出 | 归租户所有 |
| 用户输入和既有材料 | 归原权利人，授予租户必要使用许可 |
| 模型和服务 | 归提供商，按适用条款使用 |
| 提供商输出权利 | 按提供商条款转让给客户 |
| 第三方材料 | 保留原许可证和归属 |
| Regent 平台代码 | Apache-2.0，见 Q-037 |
| 审计和最小 tombstone | 按治理策略保留，不含可还原业务内容 |

## 3. 租户业务产出

包括：

- 代码、测试、构建脚本和发布包。
- 需求、设计、文档、报告和产品简报。
- 数据整理结果、标注和派生数据集。
- 公司志、会议结论和项目决策。
- 图像、音频、视频和多媒体内容。
- 组织记忆、知识库和已发布 Skill。

- 产出必须绑定 tenant_id 和 project_id。
- 员工离职不改变公司成果归属。
- 员工 Agent 不是法律作者，不因生成自动取得版权。

## 4. Regent 有限许可

Regent 仅为以下目的获得非独占、受限许可：

- 托管、存储和传输。
- 运行任务、模型调用和 Tool。
- 备份、恢复和安全防护。
- 客户支持和审计。
- 履行法律义务。

Regent 不获得以下默认权利：

- 将租户内容用于平台模型训练。
- 将租户内容放入公共基准。
- 将租户产出转为跨租户公共资产。
- 将租户秘密或 PII 用于营销。

上述用途需要单独 opt-in、脱敏、许可审核和审计。

## 5. 人类输入与贡献

- 用户上传和预先存在的知识产权仍归原权利人。
- 人类用户向租户授予运营公司所必需的使用许可。
- 人类对 AI 产出的创造性修改、选择、编排和审查应留下记录。
- 仅提供提示词不必然形成足够的人类创作贡献。
- 纯 AI 生成内容可能不受版权保护，产品不得承诺一定可注册版权。

## 6. 提供商条款

- 每次模型调用记录 provider、model、账户类型和适用条款版本。
- OpenAI、Anthropic 等条款只处理提供商与客户之间的权利。
- 不覆盖第三方权利，也不保证输出唯一或受版权保护。
- 提供商条款变化不得静默改写历史 Artifact 记录。

## 7. 第三方材料

以下材料保持原权利和许可：

- 开源代码、依赖、模板和示例。
- MCP、Tool 和外部 API 返回的数据。
- 抓取网页、图片、视频和数据集。
- 用户上传的受保护材料。
- 品牌、商标、肖像、专利和第三方机密。

- 生成结果与其他用户相同或实质性相似时，不主张独占。
- 租户归属声明不能清除第三方署名、许可或通知义务。
- SBOM、ThirdPartyNotice 和许可证扫描随发布包保留。

## 8. 权利状态

```text
TenantOwned
ThirdPartyRestricted
UnknownRights
PublicReleaseApproved
LegalHold
```

- `UnknownRights` 默认不能对外发布。
- `ThirdPartyRestricted` 可以内部使用，但发布必须满足原许可。
- `PublicReleaseApproved` 必须绑定具体版本、渠道和许可证。
- `LegalHold` 不改变归属判断，只限制清除。

## 9. RightsDeclaration

```text
id
tenant_id
artifact_id
owner_type
rights_status
platform_license_scope
provider_terms_ref
third_party_summary
human_contribution_summary
declared_by
declared_at
```

- 每个重要 Artifact 至少有一份有效声明。
- 声明绑定不可变 artifact digest。
- 新版本生成新声明，不覆盖历史。

## 10. ProvenanceRecord

```text
id
tenant_id
artifact_id
task_id
employee_id
input_refs
model_provider
model_version
prompt_bundle_id
tool_refs
dependency_refs
output_hash
created_at
```

- 记录生成路径，不记录秘密或完整 Prompt。
- Tool、MCP、Skill 和依赖使用不可变版本或 digest。
- 来源链必须能解释主要输入和产出。

## 11. ThirdPartyNotice

```text
id
artifact_id
source_ref
license_id
license_text_ref
attribution_required
notice_text
risk_status
reviewed_by
```

- 未知公共许可证默认阻止生产发布。
- 归属和署名要求必须进入发布包。
- 撤销或过期许可进入发布阻断和复审。

## 12. HumanAuthorshipRecord

```text
id
artifact_id
account_id
contribution_type
summary
evidence_refs
recorded_at
```

贡献类型：

- `Directed`：人类给出具体创作方向。
- `Selected`：人类选择、排列或组合。
- `Modified`：人类进行创造性修改。
- `Reviewed`：人类审查和验收。
- `Published`：人类决定对外发布。

记录用于说明创作过程，不保证法律上的作者身份。

## 13. 发布门禁

对外发布必须同时满足：

1. Owner 或显式发布权限批准。
2. RightsDeclaration 有效。
3. 权利状态不是 UnknownRights。
4. ThirdPartyNotice 和许可证完整。
5. SBOM 和依赖许可通过。
6. 秘密、PII 和内容安全通过。
7. 人类贡献记录存在。
8. 明确开源许可证或保留所有权利。

- Regent 平台许可证不自动套用到租户产出。
- 默认项目私有，不开放公共许可。
- 发布批准绑定 artifact digest 和目标渠道。

## 14. 训练与共享

- 租户内容默认不用于训练平台模型。
- 跨租户共享必须单独 opt-in。
- 公共 Skill、评测集和基准必须匿名、脱敏并审核许可证。
- 共享不转移第三方权利。
- 共享记录保留来源、范围、期限和撤销方式。

## 15. 删除与保留

- Artifact 删除遵循 Q-030 软删除和物理清除。
- 来源记录与 Artifact 同生命周期。
- 业务删除后只保留最小审计和 tombstone。
- ThirdPartyNotice 和法律义务需要保留时，记录范围和期限。
- 删除不得静默抹去许可或争议证据。

## 16. API 与事件

- `POST /artifacts/{id}/rights`。
- `POST /artifacts/{id}/provenance`。
- `POST /artifacts/{id}/third-party-notices`。
- `POST /artifacts/{id}/human-contributions`。
- `POST /artifacts/{id}/release-rights-check`。

事件：

- `RightsDeclared`
- `RightsStatusChanged`
- `ProvenanceRecorded`
- `ThirdPartyNoticeAdded`
- `HumanContributionRecorded`
- `ReleaseRightsApproved`
- `ReleaseRightsRejected`

## 17. 指标

| 指标 | 目标 |
|---|---:|
| 发布产出来源完整率 | 100% |
| 未知权利对外发布数 | 0 |
| 未授权跨租户训练数 | 0 |
| 第三方许可阻断数 | 持续监控 |
| 人类贡献记录覆盖率 | 持续提升 |

## 18. 测试场景

1. Artifact 始终绑定 tenant_id。
2. 新 Artifact 版本不覆盖旧权利声明。
3. UnknownRights 不能对外发布。
4. 第三方许可缺失时阻止发布。
5. SBOM 与许可证清单一致。
6. 提供商和模型版本可追溯。
7. Tool 和依赖版本可追溯。
8. 人类修改和审查可记录。
9. 平台默认不将租户内容用于训练。
10. 跨租户共享必须显式 opt-in。
11. 员工离职不改变公司成果归属。
12. 删除后来源记录按同一生命周期处理。
13. 审计和 tombstone 不包含业务原文。
14. 权利状态变更全部审计。

## 19. 验收场景

1. 租户任务产出明确归租户。
2. 平台只持有限运营许可。
3. 第三方权利不被归属声明覆盖。
4. 重要 Artifact 来源完整。
5. 人类创造性贡献可追溯。
6. UnknownRights 不能发布。
7. 发布包包含必要归属和许可通知。
8. 租户内容默认不用于平台训练。
9. 跨租户共享具有明确授权和许可证审核。
10. 文档明确不保证 AI 产出一定受版权保护。

## 20. 参考

- OpenAI Terms of Use：https://openai.com/policies/terms-of-use/
- Anthropic Commercial Terms：https://www.anthropic.com/legal/commercial-terms
- U.S. Copyright Office AI：https://www.copyright.gov/ai/
