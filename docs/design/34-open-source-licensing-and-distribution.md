# 34 开源许可与分发

- 版本：v0.1（待评审）
- 范围：Apache-2.0、LICENSE/NOTICE、SBOM、贡献者、第三方通知、商标和商业服务
- 对应需求：FR-077、BR-114、BR-115、BR-116、BR-117
- 对应缺口：G-48

## 1. 目标与边界

- 第一方产品全链路开源。
- 核心和默认企业功能不设许可证锁。
- 商业收入来自托管、支持、SLA 和运维服务。
- 第三方权利、租户产出和商标不被平台许可证覆盖。
- 本设计不替代具体司法辖区的法律意见。

## 2. 许可范围

| 内容 | 许可 |
|---|---|
| 第一方后端和前端源码 | Apache-2.0 |
| 测试、构建和部署脚本 | Apache-2.0 |
| Schema、迁移和 API 契约 | Apache-2.0 |
| 官方文档、架构图和设计 | Apache-2.0 |
| 官方示例、模板和安装包 | Apache-2.0 |
| 第三方依赖、模型和数据集 | 各自许可证 |
| 租户业务产出 | 按 Q-034，不自动适用 Apache-2.0 |
| Regent 名称和 Logo | 商标政策，不授予代码许可 |

根目录提供 `LICENSE` 和 `NOTICE`。文件可通过 SPDX 标识声明许可证，不必每行重复完整文本。

## 3. 选择 Apache-2.0 的理由

- 允许商业使用、修改、分发和再许可。
- 提供明确专利授权和专利报复条款。
- 定义贡献者和衍生作品边界。
- 要求保留许可、NOTICE 和修改声明。
- 提供明确商标边界。
- 适合企业自托管、第三方集成和多人协作。

第一版不采用 MIT、AGPL、GPL、BSL、SSPL 或双许可证。

## 4. 仓库文件

```text
LICENSE
NOTICE
CONTRIBUTING.md（贡献规则）
docs/third-party-notices.md（发布时生成或更新）
```

- `LICENSE` 使用 Apache License 2.0 官方文本。
- `NOTICE` 包含项目名称、版权和必要归属。
- 第三方通知不能替代其原始许可证文本。
- 文档和示例中的第三方片段必须注明来源。

## 5. 贡献者规则

- 贡献按 Apache-2.0 第 5 条提交。
- PR 使用 DCO `Signed-off-by`。
- 不强制转让版权。
- 贡献者必须有权提交内容。
- 企业贡献者应确认其组织授权。
- 第三方代码必须记录来源、版本、许可证和归属。
- 大额贡献可以增加 CLA，但第一版不强制。

贡献者确认记录为 `ContributionAttestation`：

```text
id
repository
commit_sha
contributor_ref
signoff_ref
authority_attested
third_party_refs[]
status
```

## 6. LicenseInventory

```text
id
artifact_digest
component_ref
version
spdx_id
license_text_ref
source_ref
attribution_required
notice_required
copyleft
compatibility_status
review_status
```

- 每个发布制品生成 LicenseInventory。
- 与 SBOM 使用相同 digest 范围。
- 未知许可证标记 `Unknown` 并阻止公开发布。
- 强 copyleft 依赖必须单独审查链接、分发和容器边界。
- 许可证冲突不得由总分、成本或功能收益抵消。

## 7. ReleaseLicenseReport

```text
id
release_id
artifact_digest
license_inventory_id
sbom_ref
license_ref
notice_ref
third_party_notices_ref
spdx_coverage
scan_status
reviewer_id
approved_at
```

公开发布前必须满足：

1. LICENSE 和 NOTICE 存在。
2. SBOM 与发布 digest 一致。
3. 第一方文件具有 SPDX 或许可声明。
4. 第三方许可证和归属完整。
5. 未知许可证为 0。
6. 强 copyleft 冲突为 0。
7. 修改声明和 NOTICE 已保留。
8. 签名和来源证明有效。

## 8. 修改与再分发

- 修改者必须保留原许可和 NOTICE。
- 必须显著标记修改。
- 可以收费分发，也可以提供商业支持。
- 不得移除第三方归属和许可证。
- 不得暗示 Regent 官方背书或认证。

## 9. 商业边界

允许：

- 托管和订阅。
- 企业支持、SLA 和培训。
- 运维、迁移和恢复服务。
- 定制开发和咨询。

当前不允许：

- 将核心功能改为闭源商业版。
- 通过许可证密钥关闭默认企业能力。
- 在核心仓库混入 source-available 代码后仍宣称全链路开源。
- 删除或替换第三方许可证义务。

未来 source-available 附加组件必须独立仓库或独立分发单元，并明确标识，不得污染 Apache-2.0 核心。

## 10. 商标

- Apache-2.0 不授予 Regent 商标使用权。
- 名称和 Logo 使用需符合单独商标政策。
- 可以如实说明“基于 Regent Agent”。
- 不得暗示官方认证、合作或背书。
- 发布页面和安装包使用原始品牌资产时需保留归属。

## 11. 安全与供应链

- 复用 SupplyChainArtifact 的 digest、签名、SBOM 和准入记录。
- LicenseInventory 与镜像、NuGet 包、前端包和安装包绑定。
- 许可证扫描进入 CI 门禁。
- 撤销、漏洞和许可风险按 digest 传播。
- 普通员工不能绕过发布许可审核。

## 12. 租户与模型边界

- 平台许可证不改变租户产出归属。
- 模型服务条款和模型权重许可证独立适用。
- 数据集和评测集保留来源许可证。
- 公共 Skill 和模板必须经过许可审核。
- Regent 官方文档开源不等于租户文档公开。

## 13. API 与事件

- `POST /releases/{id}/license-report`
- `GET /releases/{id}/license-report`
- `POST /contributions/attestations`
- `GET /artifacts/{digest}/licenses`

事件：

- `LicenseInventoryGenerated`
- `LicenseConflictDetected`
- `ContributionAttested`
- `ReleaseLicenseApproved`
- `ReleaseLicenseRejected`

## 14. 指标

| 指标 | 目标 |
|---|---:|
| 发布许可材料完整率 | 100% |
| 未知许可证发布数 | 0 |
| SPDX 声明覆盖率 | 100% |
| 第三方通知完整率 | 100% |
| 未签名贡献进入主干数 | 0 |

## 15. 测试场景

1. 发布缺少 LICENSE 时被拒绝。
2. 发布缺少 NOTICE 时被拒绝。
3. SBOM 与发布 digest 不一致时被拒绝。
4. 未知许可证阻止公开发布。
5. 强 copyleft 冲突进入阻断审核。
6. 修改版本保留原许可和 NOTICE。
7. 第三方归属进入发布包。
8. 租户产出不自动带上 Apache-2.0。
9. 模型和数据集保留各自许可证。
10. 未 Signed-off-by 的贡献无法进入受保护分支。
11. 商业托管不要求开放租户数据。
12. 安装包不包含未审核依赖。
13. 商标使用不暗示官方背书。
14. source-available 组件不能混入核心发布。

## 16. 验收场景

1. 第一方资产具有统一 Apache-2.0 声明。
2. LICENSE、NOTICE 和第三方通知完整。
3. 每次发布具有 LicenseInventory 和 ReleaseLicenseReport。
4. 未知许可证发布数为 0。
5. 贡献者确认可追溯。
6. 核心功能不因商业托管而闭源。
7. 租户产出和第三方权利保持独立。
8. 商业收费不改变代码许可证。
9. 商标使用符合独立政策。
10. 发布许可门禁无法被普通员工绕过。

## 17. 参考

- Apache License 2.0：https://www.apache.org/licenses/LICENSE-2.0
- Apache Software Foundation Licenses：https://www.apache.org/licenses/
- MIT License：https://opensource.org/licenses/MIT
