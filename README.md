# Regent-Agent

Regent-Agent 是一个自主 AI 公司的开源实现。仓库已完成 Q-001～Q-052 决策，并实现可本地运行的主要模块。

## 当前实现范围

- .NET 10 后端 Solution，16 个业务与基础设施项目。
- ControlPlane：开发认证、租户上下文、Boss 首屏、项目、任务、员工时间线、审批、审计、通知、合规、工作流、记忆、KPI、聊天和评测 API。
- Gateway：OpenAI/Anthropic 原生响应格式、确定性本地 Provider、用量记录、本地预算和 AES-GCM 信封加密密钥库。
- Runner：注册、心跳、任务领取、租约、检查点、完成上报、本地 Agent 执行和本地目录沙箱。
- Operations：Operator 开发登录、租户账户、SupportGrant、备份配置和运营审计。
- React 19 + TypeScript + MUI 前端 workspace。
- Tenant Dashboard：登录、首屏、项目、员工时间线、审批、行动中心、通知、消息、模型、预算、知识、工作流、绩效、评测、合规和设置页面；知识页包含混合召回、投毒隔离、独立纠错和影响图。
- Ops Console：登录、运营总览、租户、SupportGrant、备份和审计页面。
- `/dev/components` 组件展示页和共享组件注册表。

当前本地模式：

- 使用 SQLite、JSON 文档和本地文件存储。
- 使用确定性本地 Provider，不连接外部模型服务。
- 不连接 Postgres、RustFS 或其他远程服务。
- 不创建或测试容器。

设计文档见 [`docs/README.md`](./docs/README.md)，工程规范见 [`AGENTS.md`](./AGENTS.md)。

## 本地开发

### 后端

需要 .NET 10 SDK。

```bash
dotnet restore RegentAgent.slnx
dotnet build RegentAgent.slnx
```

分别启动：

```bash
dotnet run --project src/RegentAgent.ControlPlane
dotnet run --project src/RegentAgent.Gateway
dotnet run --project src/RegentAgent.Runner
dotnet run --project src/RegentAgent.Operations
```

开发登录账号：

- Tenant Dashboard：`owner@example.test`
- Ops Console：`operator@example.test`

### 前端

需要 Node.js 24 和 Corepack。

```bash
corepack enable
corepack pnpm install
corepack pnpm --filter @regent/tenant-dashboard dev
corepack pnpm --filter @regent/ops-console dev
```

打开：

- Tenant Dashboard：http://localhost:5173
- Ops Console：http://localhost:4173
- ControlPlane API：http://localhost:5080
- Gateway API：http://localhost:5081
- Operations API：http://localhost:5082
- OpenAPI：http://localhost:5080/openapi/v1.json

### 测试

```bash
dotnet test RegentAgent.slnx
corepack pnpm lint
corepack pnpm typecheck
corepack pnpm test
corepack pnpm build
corepack pnpm --filter @regent/tenant-dashboard exec playwright install chromium
corepack pnpm test:e2e
python3 scripts/validate-docs.py
python3 scripts/check-offline-cache.py --require
python3 scripts/capacity-baseline.py --requests 50 --concurrency 8
python3 scripts/backup-local.py --source src --output data/local-backup.zip
python3 scripts/restore-local.py data/local-backup.zip --target data/restored
```

## 后续能力

- 真实 OpenAI/Anthropic Provider 调用。
- 完整 OIDC、Operator 认证和找回流程。
- 生产 Postgres/RustFS 适配和迁移 Job。
- 生产备份、恢复和合规证据导出。
- 容器化部署资料验证。
