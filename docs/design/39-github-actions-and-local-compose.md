# 39 GitHub CI/CD 与本地部署

- 版本：v0.2（供应链 CI 与不可变 Action 固定已实现）
- 范围：GitHub 版本 Tag、GitHub Actions 检查、本地 Docker Compose 构建与回滚
- 对应需求：FR-082、BR-134、BR-135、BR-136
- 对应缺口：G-54

## 1. 范围

- 源码、Issue、PR 和版本只使用 GitHub。
- GitHub 只打 `v*` 版本 Tag。
- GitHub Actions 只负责编译、测试、扫描和迁移检查。
- 本地从版本 Tag 检出源码，用 Docker Compose + systemd 构建和运行。
- 默认使用 GitHub 托管 Runner。

不引入：

- GHCR 或其他镜像仓库。
- 镜像发布、远程制品晋升或签名分发。
- Gitea、Forgejo 或 GitForge 适配层。
- Kubernetes 或其他编排器。
- 独立 Staging/Production 环境。
- 多云、多区域部署和 GitOps。

## 2. 分支与 PR

- 主分支保护。
- 员工使用 `agent/{employee_id}/{task_id}` 分支和独立 worktree。
- 提交包含 Conventional Commit、`task_id` 和 DCO `Signed-off-by`。
- 作者不能单独批准自己的变更。
- CI 未通过禁止合并。
- squash merge 后生成不可变主分支 commit。

## 3. GitHub Actions

### 3.1 pull_request

执行：

- 还原依赖和编译。
- 单元、组件和集成测试。
- 真实 Postgres/RustFS 集成测试。
- Provider 契约测试。
- E2E 和 Playwright。
- 安全、秘密、依赖和容器扫描。
- 许可证、NOTICE 和 SBOM 检查。
- 数据库迁移 dry-run。
- Compose 配置校验。

规则：

- 不构建发布镜像。
- 不推送任何镜像或包。
- 不访问生产秘密。
- Fork 和外部 PR 不获得 Secrets。
- 不使用 `pull_request_target` 执行不可信代码。

### 3.2 main 与版本 Tag

- main 推送运行完整检查。
- `v*` Tag 再次运行发布检查。
- Tag 是唯一发布身份。
- Actions 不发布镜像、安装包或二进制制品。
- GitHub Release 页面最多记录版本说明和检查结果，不承担制品分发。

## 4. 本地构建

前置条件：

- Docker Engine 和 Docker Compose。
- systemd。
- Git 和对应 Tag 的源码。
- 本地依赖缓存或网络访问。
- 本地 secret 文件或密钥系统引用。

流程：

```text
git fetch --tags
git checkout vX.Y.Z
校验 commit 和 Tag
docker compose build
生成本地 SBOM 和构建记录
执行既有 Drain / Backup / Expand / Deploy / Smoke / Resume / Contract
```

- 不在 CI 中构建发布镜像。
- 本地 Compose 使用版本 Tag 对应的 Dockerfile 和 lock file。
- 基础镜像尽量按 digest 固定。
- 构建不得读取 CI Secrets。
- 生产秘密只在运行阶段注入容器。

## 5. LocalBuild

```text
id
host_ref
version_tag
source_commit
compose_file_hash
lockfile_hashes[]
base_image_digests[]
built_image_ids[]
sbom_ref
started_at
completed_at
status
operator_id
```

- 记录本地实际构建结果，不假定不同主机位级一致。
- 构建失败不切换当前运行版本。
- 构建记录保留当前和上一版本镜像 ID。
- 本地镜像不得推送到任何远程仓库。

## 6. 配置与密钥

- 非秘密配置版本化并随源码提供模板。
- 生产秘密保存在本地 secret 文件或密钥系统，不进入 Git。
- 模型密钥仍只存在于 Gateway。
- CI 不需要生产模型密钥。
- GitHub Secrets 仅保存检查和测试所需最小凭据。
- 日志、缓存和构建产物必须扫描秘密。
- 密钥轮换不改变版本 Tag。

## 7. 本地部署

- 使用 Compose + systemd 启动和监管四部分、两个前端、Postgres 和 RustFS。
- 部署记录保存 Tag、commit、镜像 ID、配置版本、操作者和结果。
- 不在目标机使用 `latest` 作为发布版本。
- 应用启动时不自动执行数据库迁移。
- 升级复用既有 Drain、迁移、Smoke 和回滚流程。
- 仓库可提供 install/upgrade/validate/rollback/status 脚本，但不提供二进制安装包或自动更新，详见 [43 自托管安装与离线升级](./43-self-hosted-install-and-offline-upgrade.md)。

## 8. 回滚

- 优先重新启动上一版本已构建的本地镜像 ID。
- 旧镜像不存在时，检出上一个版本 Tag 并重新构建。
- 数据库按 Expand/Deploy/Contract 边界决定回滚或前滚。
- 回滚保留原 Tag、镜像 ID、失败原因和操作记录。
- 备份按 BackupProfile 可选启用，关闭时记录风险接受。

## 9. 发布审批

- PR 合并由代码评审和 CI 门禁控制。
- 本地构建和部署由 `release.execute` 执行。
- 对外发布、超预算、生产数据删除和红线动作仍走 Q-039 分级审批。
- 高风险迁移可要求双人 quorum 或 step-up。
- 审批绑定版本 Tag、配置版本和 `action_hash`。
- Tag 或配置变化使原审批失效。

## 10. 审计

记录：

- 源码 commit、PR 和版本 Tag。
- Actions workflow 版本和检查结果。
- 本地构建的 Tag、commit、基础镜像和输出镜像 ID。
- 本地部署和回滚。
- 审批与操作者。
- 失败、跳过和风险接受。

## 11. 实体

- `CiRun`：commit、workflow、状态、测试和扫描结果。
- `LocalBuild`：Tag、commit、lock file、基础镜像、输出镜像 ID 和 SBOM。
- `LocalDeployment`：主机、Tag、镜像 ID、配置版本、操作者和结果。

本地构建和部署可使用简单脚本，不要求额外控制面服务。

### 11.1 当前自动门禁

- CI supply-chain job 固定所有第三方 Action commit，执行 LicenseInventory/SBOM 一致性、NuGet/pnpm 漏洞、Expand/Contract dry-run、N/N-1 升级演练和 ReleaseManifest 校验。
- CI 不构建或推送镜像；真实容器构建、本地镜像 ID 和 Compose 联调仍按用户约束未执行。

## 12. 指标

| 指标 | 目标 |
|---|---:|
| 部署版本 Tag 一致率 | 100% |
| 公开发布镜像数 | 0 |
| CI 秘密泄漏数 | 0 |
| 本地构建记录完整率 | 100% |
| 未审批生产部署数 | 0 |

## 13. 测试场景

1. PR 未通过检查时无法合并。
2. Fork PR 无法获得 Secrets。
3. 第三方 Action 未固定 commit 时被拒绝。
4. Actions 不包含镜像推送步骤。
5. 本地构建 Tag 与批准版本不一致时拒绝部署。
6. Compose 使用浮动分支或未固定发布版本时拒绝部署。
7. 本地构建失败时保留旧运行版本。
8. 构建记录包含 Tag、commit 和镜像 ID。
9. 本地镜像不会被推送到远程仓库。
10. 迁移 dry-run 失败时阻止发布。
11. 回滚优先复用旧镜像 ID。
12. 旧镜像缺失时从旧 Tag 重建。
13. CI 不读取生产模型密钥。
14. 配置变化不改变版本 Tag。

## 14. 验收场景

1. GitHub 是唯一源码、CI 和版本平台。
2. GitHub 只打版本 Tag，不发布镜像。
3. GitHub Actions 只检查和测试。
4. 本地从批准 Tag 用 Compose 构建并运行。
5. CI 不持有生产秘密。
6. 本地构建和部署记录完整。
7. 回滚可复用旧镜像或旧 Tag。
8. 不引入镜像仓库、多平台适配、多环境晋升或 Kubernetes。
