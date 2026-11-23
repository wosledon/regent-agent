# ADR-0007：GitHub Tag 源码与本地 Compose 构建

- 状态：Accepted
- 日期：2026-11-23
- 范围：CI/CD 与自托管部署

## 背景

用户要求最简单、可自行托管的交付方式，不需要复杂制品晋升和多平台适配。

## 决策

- 源码、PR、CI 和版本只使用 GitHub。
- GitHub Actions 只执行编译、测试、扫描和迁移检查。
- GitHub 只打 `v*` 版本 Tag，不发布镜像、安装包或二进制制品。
- 自托管从 Tag 检出源码，用 Docker Compose + systemd 构建和运行。
- ReleaseManifest 记录 Tag、commit、基础镜像 digest 和本地构建镜像 ID。
- 不提供二进制安装包、激活或自动更新。

## 结果

- 交付链路简单，用户可审计和自行构建。
- 离线仅支持自备源码、基础镜像和依赖缓存。
- 回滚优先复用旧镜像，缺失时从旧 Tag 重建。

## 未采用

- GHCR/镜像发布：用户明确不需要。
- Gitea/Forgejo、Kubernetes、多环境晋升：超出当前范围。

## 关联

- [39 GitHub CI/CD 与本地部署](../design/39-github-actions-and-local-compose.md)
- [43 自托管安装与离线升级](../design/43-self-hosted-install-and-offline-upgrade.md)
