# Regent-Agent

Regent-Agent 是一个自主 AI 公司的开源实现。仓库已完成 Q-001～Q-052 决策，并实现可本地运行的主要模块。

## 当前实现范围

- .NET 10 后端 Solution，17 个业务与基础设施项目（含 AgentIdentity）。
- ControlPlane：人类治理控制面与 `/agent/v1` 员工 Agent 数据面；提供租户、项目、任务、组织、记忆、工作流、聊天、评测和受控 Tool 调用 API。
- Gateway：本地/远程可插拔 Provider 路由、OpenAI/Anthropic 原生 JSON/SSE 透传适配、流式用量记录、租户级预算和 AES-GCM 信封加密密钥库；模型调用必须使用员工 Assignment Token，KeyVault 可选本地文件或 Postgres `keyvault`（信封加密 + RLS）。
- Runner：注册、心跳、任务领取、租约、检查点、完成上报、本地 Agent 执行和本地目录沙箱。
- 四个服务统一接入可插拔 OpenTelemetry Profile（off/local/otlp/elastic），默认本地 Console 导出。
- Operations：Operator 开发登录、租户账户、SupportGrant、备份配置和运营审计。
- React 19 + TypeScript + MUI 前端 workspace。
- Tenant Dashboard：登录、首屏、项目、员工时间线、审批、行动中心、通知、消息、模型、预算、知识、工作流、绩效、评测、合规和设置页面；组织协作工作台支持办公室、组织图、协作网络、过程时间线切换，以及员工/群聊/项目追溯；知识页包含混合召回、投毒隔离、独立纠错和影响图。
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
python3 scripts/scenario-trial.py
python3 scripts/check-offline-cache.py --require
python3 scripts/capacity-baseline.py --requests 50 --concurrency 8
python3 scripts/backup-local.py --source src --output data/local-backup.zip
python3 scripts/restore-local.py data/local-backup.zip --target data/restored
```

## 后续能力

- 真实 OpenAI/Anthropic Provider 与 IdP 连通性验证（remote 模式需服务凭据，生产管理端点需管理员凭据）。
- Operator MFA、完整人工找回、真实支付/税务/退款和跨环境导入。
- 生产 Postgres/RustFS 集成、RLS/事务/多 Worker 并发和迁移 Job。
- 生产备份、WORM/异地恢复、合规证据导出与真实 OTLP/Elastic。
- 容器构建、部署、升级、回滚、soak 与混沌验证。
