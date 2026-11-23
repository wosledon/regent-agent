# 20 工具与插件供应链

- 版本：v0.1（待评审）
- 范围：Tool、MCP、Skill、依赖、容器镜像、签名、SBOM、准入、更新与撤销
- 对应需求：FR-062、BR-065、BR-066、BR-067
- 对应缺口：G-33

## 1. 目标与边界

- 批准的是不可变制品，不是插件名称或可变 tag。
- 已批准制品可自主调用，不需要每次审批。
- 安装、升级、扩权或突破沙箱必须重新审核。
- TaskRun 固定 Tool、MCP、Skill 和镜像版本。
- 撤销按 digest 处理，不删除历史证据，不处罚员工。

## 2. 管理对象

- 内置 Tool 和 ToolManifest。
- MCP Server 和 Bundle。
- Skill Package。
- NuGet 和第三方依赖。
- 容器镜像。
- 安装脚本和辅助二进制。

## 3. SupplyChainArtifact

```text
artifact_id
type
publisher
name
version
digest
signature_ref
source_ref
license
sbom_ref
scan_ref
requested_capabilities
allowed_network
secret_scope
approval_status
```

- 制品发布后不可变。
- 生产禁止 `latest`。
- 每个 TaskRun 记录实际加载的 artifact digest。
- 签名、SBOM、扫描和审批引用不可静默替换。

## 4. 信任等级

| 等级 | 示例 | 默认策略 |
|---|---|---|
| Platform | 内置 Tool 和镜像 | 签名发布，直接可用 |
| Internal | 公司内部 Skill、MCP、镜像 | 审核后租户可用 |
| Verified Third Party | 外部签名制品 | 审核 + 沙箱 |
| Local/Unsigned | 本地草稿、未签名包 | 仅开发，禁止生产 |

发布者声誉不能替代制品审核。

## 5. 签名与摘要

- 容器镜像、MCP Bundle、Skill Package 和安装包必须签名。
- NuGet 依赖使用包签名和 lock file。
- 验证签名、证书链、时间戳和可信根。
- 自托管支持导入可信根和离线验证。
- 签名有效但 digest 不匹配时拒绝加载。
- 拉取镜像和制品时固定 digest。

## 6. 准入流程

```text
提交制品
  → 签名验证
  → digest 固定
  → SBOM 生成
  → 漏洞扫描
  → 许可证检查
  → 权限和网络审查
  → 隔离试运行
  → 审批发布
```

AdmissionReview 绑定：

```text
artifact_digest
version
requested_capabilities
allowed_network
allowed_filesystem
secret_scope
reviewer
expires_at
```

## 7. Tool 与 MCP

- 只能加载已注册 ToolManifest。
- Tool Schema 和权限集合随版本固定。
- MCP 升级出现新 Tool 时默认禁用，必须重新审批。
- MCP Server 不能自行注册 Tool 或扩大权限。
- MCP 进程使用独立身份、最小凭据和隔离环境。
- Tool Call 仍由执行层鉴权，插件声明不是授权。
- 在途 TaskRun 不中途升级 MCP 或 Tool。

## 8. Skill Package

- Skill 是提示词、脚本和工具的集合，按代码审核。
- 已发布版本不可变。
- Skill 不能自行授予 Capability 或 Tool。
- 默认禁止安装脚本；确需脚本时单独审批并在沙箱执行。
- 个人草稿只能本人或授权范围试用。
- 公共 Skill 必须经过评测、许可审核、试点和签名发布。
- 回滚重新激活旧 digest，不改写历史。

## 9. 容器镜像

- 使用批准的基础镜像和最小镜像。
- 按 digest 拉取，禁止可变 tag。
- 非 Root 运行，只读根文件系统。
- 生成 SBOM 和构建来源。
- 启动前再次验证 digest 和策略。
- 镜像不包含模型密钥或租户凭据。

## 10. 依赖与脚本

- 使用 lock file 和可复现构建。
- 检查依赖混淆、来源替换和异常安装脚本。
- 安装、构建和恢复脚本在隔离环境执行。
- 依赖不直接访问模型密钥。
- 第三方凭据按任务短期注入。
- 不在镜像或仓库保存秘密。

## 11. 漏洞与许可

| 风险 | 默认处理 |
|---|---|
| 签名无效 | 拒绝加载 |
| digest 不匹配 | 拒绝加载 |
| Critical 漏洞 | 阻止发布 |
| High 漏洞 | 阻止或限期例外 |
| 未知许可证 | 阻止公共发布 |
| 请求过多权限 | 拒绝或降权 |
| 可疑网络行为 | 隔离审查 |
| SBOM 缺失 | 阻止生产 |

High 漏洞例外必须有负责人、补偿控制和到期时间。

## 12. 更新与回滚

- 不自动更新生产插件。
- 新版本先 canary、回归和安全复核。
- 审批后只影响新任务。
- 回滚恢复旧 digest。
- 更新进入 ReleaseManifest 和审计。
- 在途 TaskRun 保持原版本直到安全结束。

## 13. 撤销与事件响应

```text
发现恶意或漏洞制品
  → 禁止新任务加载
  → 隔离版本
  → 按 digest 查询影响
  → 暂停相关高风险动作
  → 轮换可能暴露的凭据
  → 回滚或替换
  → 复盘和通知
```

- 撤销事件通过 Outbox 传播。
- 所有运行实例必须确认拒绝新调用。
- 不删除历史证据。
- 不自动认定使用该制品的员工有责任。
- 无法及时撤销时冻结相关 Tool Profile。

## 14. 影响分析

`ArtifactImpactRecord` 记录：

- 哪些 Tool Profile 和 Skill 引用该 digest。
- 哪些员工和任务使用过。
- 哪些在途 TaskRun 仍加载。
- 哪些审批和发布包含该制品。
- 哪些凭据可能暴露。
- 需要暂停、回滚或轮换的动作。

## 15. 离线与自托管

- 支持导入签名 Bundle 和可信根。
- 离线验证签名、digest 和 SBOM。
- 漏洞数据库可以本地更新包导入。
- 无法联网扫描时使用最近有效扫描并标记 Stale。
- 未签名开发制品不得进入生产 Profile。

## 16. 自主性

- 已批准制品可随时调用，不需要每次审批。
- 只有安装、升级、扩权或突破沙箱才审核。
- 员工可以继续使用其他已批准工具。
- 插件被撤销时不处罚员工。
- 本地未签名草稿可在开发环境试用。

## 17. 指标

| 指标 | 目标 |
|---|---|
| 签名覆盖率 | 100% |
| SBOM 覆盖率 | 100% |
| 未签名生产加载尝试 | 0 |
| 未批准版本调用 | 0 |
| Critical 漏洞存活数 | 0 |
| 撤销传播时间 | 持续下降 |
| 插件升级回归失败率 | 持续下降 |
| 供应链事件影响任务数 | 可解释、可追踪 |

## 18. API 与 Tool

- `POST /artifacts`：提交制品。
- `POST /artifacts/{id}/reviews`：创建准入审核。
- `POST /artifacts/{id}/approve`：批准 digest。
- `POST /artifacts/{id}/revoke`：撤销版本。
- `GET /artifacts/{id}/impact`：查询影响。
- `POST /artifacts/{id}/rollback`：恢复旧 digest。
- 普通员工不能自行注册生产 Tool 或修改审批。

## 19. 测试场景

1. 篡改签名内容被拒绝。
2. 同 tag 不同 digest 被拒绝。
3. MCP 升级新增 Tool 默认禁用。
4. Skill 包含恶意安装脚本被隔离。
5. 依赖混淆和来源替换被拒绝。
6. 漏洞镜像无法绕过扫描。
7. 撤销后所有实例拒绝新调用。
8. 离线签名验证失败被拒绝。
9. 在途 TaskRun 不被强制升级。
10. 已批准 Tool 调用不需要重复审批。
11. 高风险漏洞例外到期后自动阻止。
12. 影响分析可以定位任务、Profile 和凭据。

## 20. 验收场景

1. 每个生产制品都有 digest、签名、SBOM 和审批。
2. 未签名制品无法进入生产。
3. TaskRun 固定制品版本。
4. MCP 新 Tool 不会自动获得权限。
5. Skill 不能自行扩大 Capability。
6. Critical 漏洞阻止发布。
7. 已批准制品可自主调用。
8. 撤销按 digest 传播并保留审计。
9. 员工不会因使用已批准制品被处罚。
10. 离线环境可以验证签名和摘要。
