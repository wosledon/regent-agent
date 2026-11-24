# 43 自托管安装与离线升级

- 版本：v0.2（升级计划、Expand/Contract 与回滚演练脚本已实现）
- 范围：Tag 源码安装、Compose/systemd、脚本、许可证、离线缓存和升级恢复
- 对应需求：FR-087、BR-148、BR-149、BR-150、BR-151
- 对应缺口：G-58

## 1. 范围

- 自托管只使用 GitHub 版本 Tag 源码。
- 使用 Docker Compose + systemd。
- 仓库提供简单安装、升级、校验和回滚脚本。
- 直接使用 Apache-2.0。

不提供：

- 二进制安装器或 GUI 安装器。
- 一键安装包。
- License Key、激活服务器或电话回拨。
- 自动更新检查、下载或安装。
- 完整离线安装包。
- 完全断网模型推理。

## 2. 前置条件

- 支持的 Linux 发行版。
- Docker Engine 和 Docker Compose。
- systemd。
- Git。
- 足够的 CPU、内存、磁盘和数据卷空间。
- 出站 HTTPS 访问 GitHub、依赖源和模型 Provider。

安装脚本必须检查：

- 操作系统和 Docker/Compose 版本。
- 端口冲突。
- 数据目录权限。
- 磁盘空间。
- 必需环境变量。
- 秘密文件权限。
- 版本 Tag 和 commit。

## 3. 在线安装

```text
git clone repository
git checkout vX.Y.Z
cp .env.example .env
填写非秘密配置和 secret_ref
./scripts/install.sh
  → 校验前置条件
  → 生成本地 secret 文件
  → docker compose build
  → 初始化数据卷
  → 执行初始化迁移
  → docker compose up -d
  → health / ready / smoke
```

- 安装脚本不能静默下载浮动版本。
- 生产必须固定版本 Tag。
- 初始化迁移与日常启动分离。
- 安装日志不得包含秘密。
- 失败时保留诊断信息并返回非零退出码。
- 生产 ControlPlane 必须配置 `Metering__Token`；Gateway 必须配置 `Gateway__ControlPlaneUrl` 和 `Gateway__MeteringToken`（Compose 默认复用 `Metering__Token`），否则启动失败。
- Gateway 用量投影队列持久化到 `Gateway__UsageProjectionPath`，ControlPlane 不可用时保留事件并退避重试，不静默丢弃。

## 4. 仓库脚本

- `install.sh`：首次安装。
- `upgrade.sh`：指定目标 Tag 升级。
- `validate.sh`：配置、Compose、磁盘和端口检查。
- `rollback.sh`：恢复上一版本镜像或 Tag。
- `status.sh`：查看服务、版本和健康状态。

脚本只封装既有流程，不创建新的控制面或自动更新机制。

## 5. 许可证与遥测

- 第一方代码、脚本、文档和模板使用 Apache-2.0。
- 安装和运行不需要激活。
- 不向 Regent 发送安装遥测或电话回拨。
- 保留 `LICENSE`、`NOTICE`、SBOM 和第三方许可清单。
- 第三方依赖、模型和数据保留各自许可证。

## 6. 更新

- 不自动检查 GitHub 新版本。
- 不自动下载或安装更新。
- 用户自行选择版本 Tag。
- 升级必须显式指定目标 Tag。
- 禁止使用 `main`、浮动分支或 `latest` 作为生产版本。
- 更新说明和兼容矩阵随 Tag 提供。

## 7. 离线安装与升级

当前不提供官方完整离线包，只支持用户自备缓存：

- 版本 Tag 源码快照。
- 基础镜像缓存。
- NuGet、npm 等依赖缓存。
- 漏洞数据库和 models.dev 快照。
- 许可证和 SBOM 数据。

离线规则：

- 设置 `OFFLINE=1` 后禁止联网回退。
- 安装前检查所有缓存和校验值。
- 缺少依赖时快速失败并列出缺失项。
- 不得把缓存缺失当作成功。
- 缓存校验失败时拒绝构建。
- 不承诺不同主机位级一致的构建结果。

由于模型运行只支持云端 Provider，完全断网时无法执行模型推理；离线只能完成控制面安装、已有数据查看和本地管理操作。

## 8. 升级流程

```text
指定目标 Tag
  → 校验兼容矩阵和前置条件
  → Drain
  → 按 BackupProfile 备份或记录跳过
  → Expand Migration
  → Deploy
  → Smoke
  → Resume
  → Contract
```

- 构建失败不切换运行版本。
- 迁移失败停止发布。
- Smoke 失败按既有规则回滚或前滚。
- Contract 后禁止应用回滚。
- 备份默认关闭，跳过时记录风险接受。

## 9. 失败恢复

- 保留当前 Tag、上一版本 Tag 和本地镜像 ID。
- 优先复用上一版本镜像。
- 旧镜像不存在时从上一 Tag 重新构建。
- 数据库按 Expand/Contract 边界决定回滚或前滚。
- 失败不删除旧数据卷、旧源码或旧构建记录。
- 恢复后执行健康检查和最小 smoke。
- 失败日志、诊断包和审计不得包含秘密。

## 10. 安全

- 安装脚本不使用 `sudo` 执行不可信下载内容。
- 下载源码时校验 Tag、commit 和校验值。
- 第三方 Action、基础镜像和依赖固定版本或 digest。
- secret 文件最小权限，不进入 Git、镜像和日志。
- 自托管默认不暴露运营平台。
- 安装脚本不得关闭防火墙或降低 Docker 安全配置。

## 11. 实体

- `InstallRun`：主机、Tag、commit、前置检查、步骤和结果。
- `OfflineCacheManifest`：源码、镜像、依赖和校验值。
- `UpgradeRun`：目标 Tag、备份策略、迁移、部署、Smoke 和恢复结果。

## 12. 指标

| 指标 | 目标 |
|---|---:|
| 空白主机安装成功率 | 100% |
| 非 Tag 生产部署次数 | 0 |
| 自动升级次数 | 0 |
| 离线依赖静默缺失次数 | 0 |
| 升级失败后旧版本可用率 | 100% |
| 安装日志秘密泄漏数 | 0 |

## 13. 测试场景

1. 空白 Linux 主机按文档安装成功。
2. Docker 或 Compose 版本不足时快速失败。
3. 端口冲突时安装不继续。
4. secret 文件权限过宽时拒绝启动。
5. 未指定版本 Tag 时拒绝生产安装。
6. 浮动分支或 latest 被拒绝。
7. 安装日志不包含秘密。
8. 安装不需要激活或电话回拨。
9. 系统不自动检查或安装更新。
10. 离线模式缺少基础镜像时快速失败。
11. 离线模式缺少 NuGet/npm 缓存时快速失败。
12. 离线模式不会联网回退。
13. 完全断网时明确提示模型推理不可用。
14. 构建失败保留旧运行版本。
15. 迁移失败停止升级。
16. Smoke 失败可回滚或前滚。
17. Contract 后禁止应用回滚。
18. 备份关闭时记录风险接受。
19. 回滚优先复用旧镜像 ID。
20. 回滚后健康和 smoke 通过。

## 14. 验收场景

1. 自托管只需 Tag 源码、Compose 和仓库脚本。
2. 无二进制安装包、激活和自动更新。
3. 离线缓存缺失必须可见且快速失败。
4. 完全断网不承诺模型推理。
5. 升级失败保留旧版本并按既有边界恢复。
6. 安装、升级和回滚均无秘密泄漏。

### 14.1 当前脚本边界

- `scripts/verify-release.py` 生成绑定 Tag、commit、schema、lockfile、LicenseInventory、SBOM 和迁移哈希的 ReleaseManifest；dry-run 不要求创建 Tag。
- `scripts/plan-upgrade.py` 只接受不可变 `vMAJOR.MINOR.PATCH` Tag，校验 N/N-1 兼容、备份或风险接受，并生成 Drain/Backup/Expand/Deploy/Smoke/Resume/Contract 顺序。
- `deploy/sql/controlplane-003-contract.sql` 是显式 Contract 门禁：必须已完成 Expand、Smoke 并设置 `regent.allow_contract=on`，否则拒绝执行。
- `scripts/upgrade-drill.py` 覆盖成功、Deploy 失败、Smoke 失败和 Contract 失败；Contract 前允许恢复旧镜像/Tag，Contract 后只能前滚纠正迁移。
- 所有升级与迁移脚本均提供 dry-run；本地已构建并运行六个应用容器，复用既有 Postgres/RustFS 完成注册、租户隔离、RLS 和导出冒烟，但未执行真实生产升级或回滚。
- `scripts/configure-elasticsearch.py` 校验并配置本地 ES 原生 OTLP 端点、单节点 0 副本和索引模板；凭据只从环境变量读取，不写入仓库。
