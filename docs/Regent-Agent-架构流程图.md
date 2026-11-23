# Regent-Agent 完整架构流程图

> 用途：从用户入口、控制面、执行层、模型层、数据层和运营层检查整体架构是否闭环。  
> 状态：v0.1（待评审）  
> 配套文档：[技术设计总览](./Regent-Agent-技术设计.md) 与 [详细设计索引](./design/README.md)

## 1. 阅读说明

图中实线表示请求、命令或数据流，虚线表示事件、注册、管理或运维同步。

四部分边界：

1. **ControlPlane**：租户公司业务权威，不持有模型密钥。
2. **AI Gateway**：唯一持有模型密钥，负责原生协议代理、路由、预算和计量。
3. **Runner**：无状态 Controller/Employee 计算宿主，不持有持久业务真相。
4. **Operations Platform**：提供方账户、配额、资源池、事件、计费、备份和支持访问。

## 2. 总体架构流程图

```mermaid
flowchart TB
    subgraph Humans["人类与外部系统"]
        Boss["Boss / Owner"]
        OpsOperator["运营人员"]
        OpenAI["OpenAI"]
        Anthropic["Anthropic"]
        ExternalTools["CI / 测试 / 浏览器 / 已授权 HTTP"]
    end

    subgraph Frontends["用户界面"]
        TenantDashboard["Tenant Dashboard<br/>租户认证 / 组织 / 项目 / 预算 / 升级"]
        OpsConsole["Ops Console<br/>运营认证 / 租户 / 资源池 / 事件 / 计费"]
        UIShared["UI.Shared<br/>仅共享组件，不共享会话"]
        TenantDashboard -.-> UIShared
        OpsConsole -.-> UIShared
    end

    subgraph ControlPlane["ControlPlane：租户业务权威"]
        TenantAPI["Tenant API / BFF<br/>JWT / 租户隔离 / 幂等"]
        Domain["领域服务<br/>Organization / Projects / Workflows / Memory / Performance / Communication"]
        ToolPEP["Tool Registry / PEP<br/>Authorized Tools / Tool Profile / 执行鉴权"]
        Scheduler["调度器<br/>能力匹配 / 租约 / fencing / WIP"]
        Messaging["消息总线<br/>messages / Outbox / Inbox / seq 游标"]
        CommRoute["通信路由<br/>管理链 / 专业链 / 向上穿透"]
        Ledger["双账本与预算<br/>模型成本 / 工具成本 / 编制 / 预留"]
        AuditSentinel["审计 / 公司志 / Sentinel<br/>不可变事件 / 确定性监控"]
        Butler["Butler<br/>只读 MCP 工具 / 定时报告"]
        Escalation["升级队列<br/>Boss 决策 / 超时默认拒绝"]

        TenantAPI --> Domain
        TenantAPI --> ToolPEP
        TenantAPI --> Ledger
        TenantAPI --> Messaging
        TenantAPI --> AuditSentinel
        Domain --> Scheduler
        Domain --> Messaging
        Domain --> ToolPEP
        Domain --> Ledger
        Domain --> AuditSentinel
        Domain --> Escalation
        Domain --> CommRoute
        Scheduler --> Messaging
        CommRoute --> Messaging
        Messaging --> AuditSentinel
        Butler --> ToolPEP
        Butler --> Ledger
        Escalation --> AuditSentinel
    end

    subgraph RunnerLayer["Runner × N：无状态计算"]
        RunnerHost["Generic Host<br/>注册 / 心跳 / 租约 / 恢复"]
        Controller["Controller Runtime<br/>组织规划 / 项目编排 / 继任"]
        Employee["Employee Runtime<br/>独立上下文 / 单线程主循环 / 休眠唤醒"]
        ToolClient["工具客户端<br/>Primary / Auxiliary / Restricted"]
        Sandbox["两层沙箱<br/>团队工位 / 一次性执行容器"]
        BoundedCache["有界内存缓存<br/>不承载持久业务状态"]

        RunnerHost --> Controller
        RunnerHost --> Employee
        Controller --> ToolClient
        Employee --> ToolClient
        ToolClient --> Sandbox
        Employee -.-> BoundedCache
    end

    subgraph Gateway["AI Gateway：模型资源服务器"]
        GatewayAPI["原生协议端点<br/>/v1/chat/completions<br/>/v1/messages"]
        Router["鉴权 / 租户路由 / 模型策略"]
        Budget["预算预留 / 硬限制"]
        Metering["计量 / 用量事件"]
        KeyVault["BYOK 密钥库<br/>模型密钥唯一持有者"]
        NativeAdapters["原生 Provider 插件<br/>不做 OpenAI ↔ Anthropic 转换"]

        GatewayAPI --> Router
        Router --> Budget
        Budget --> NativeAdapters
        NativeAdapters --> Metering
        Router --> KeyVault
    end

    subgraph Operations["Operations Platform：提供方管理面"]
        OpsAPI["Ops API<br/>独立 operator 身份"]
        TenantAccounts["租户账户 / 套餐 / 配额"]
        ResourcePools["Runner / Gateway 池<br/>容量 / 标签 / drain / 版本"]
        Incidents["事件 / 状态页 / 值班"]
        Billing["订阅计费 / 对账"]
        Support["SupportGrant<br/>限时 / 限范围 / 可撤销"]
        OpsBackup["备份作业 / 恢复演练"]

        OpsAPI --> TenantAccounts
        OpsAPI --> ResourcePools
        OpsAPI --> Incidents
        OpsAPI --> Billing
        OpsAPI --> Support
        OpsAPI --> OpsBackup
    end

    subgraph Storage["持久化与恢复"]
        PG[("Postgres<br/>领域状态 / 审计 / 消息 / 关系")]
        Vector[("pgvector + 全文索引<br/>记忆召回")]
        RustFS[("RustFS<br/>Git 快照 / 成果 / 报告 / 发布包")]
        LocalWorkspace[("Runner 本地或容器目录<br/>活动 worktree / 临时缓存")]
        Backup[("加密备份<br/>数据库 / 配置 / 密钥库 / 成果")]
    end

    Boss --> TenantDashboard
    OpsOperator --> OpsConsole
    TenantDashboard -->|tenant token| TenantAPI
    OpsConsole -->|operator token| OpsAPI

    OpsAPI -.->|租户状态 / 配额 / 策略同步| TenantAPI
    OpsAPI -.->|资源池 / 部署 / drain| RunnerHost
    OpsAPI -.->|模型目录 / 健康 / 容量| GatewayAPI
    Support -.->|限时受控访问| TenantAPI

    Scheduler -->|任务 / assignment token| RunnerHost
    RunnerHost -->|注册 / 心跳 / 续租| Scheduler
    Controller -->|Tool Call| ToolPEP
    Employee -->|Tool Call| ToolPEP
    ToolClient -->|发现 / 展开 / 激活 / 调用| ToolPEP
    ToolClient -->|消息 / 路由请求| CommRoute
    ToolPEP -->|允许后执行领域命令| Domain

    Controller -->|模型请求| GatewayAPI
    Employee -->|模型请求| GatewayAPI
    Butler -->|模型请求| GatewayAPI
    NativeAdapters --> OpenAI
    NativeAdapters --> Anthropic
    Metering -.->|权威模型用量| Ledger
    ToolClient -.->|工具用量事件| Ledger

    Sandbox -->|代码 / 测试 / 构建| Workspace
    Sandbox -->|提交成果 / 报告| Artifact
    Sandbox -->|受控工具执行| ExternalTools

    Domain --> PG
    Messaging --> PG
    Ledger --> PG
    AuditSentinel --> PG
    Domain --> Vector
    Domain --> Workspace
    Domain --> Artifact
    PG -.-> Backup
    Workspace -.-> Backup
    Artifact -.-> Backup
    KeyVault -.-> Backup
    OpsBackup --> Backup
```

## 3. Tool 发现、渐进披露与执行流程

```mermaid
sequenceDiagram
    participant Agent as Controller / Employee
    participant Runner as Runner
    participant CP as ControlPlane
    participant Registry as Tool Registry / PEP
    participant Domain as 领域服务
    participant DB as Postgres / Outbox

    Agent->>Runner: 产生 Tool 调用意图
    Runner->>CP: /tools/discover(task_id, profile)
    CP->>Registry: 计算 Authorized Tools + Tool Profile
    Registry-->>Runner: Primary 完整 Schema + Auxiliary 简介
    Runner-->>Agent: 注入当前工具画像

    alt 调用 Primary Tool
        Agent->>Runner: invoke(tool, args, idempotency_key)
    else 调用 Auxiliary Tool
        Agent->>Runner: expand / activate(tool, reason)
        Runner->>CP: /tools/expand 或 /tools/activate
        CP->>Registry: 校验权限上限与任务范围
        Registry-->>Runner: NeedsActivation / Allowed / Forbidden
        Runner-->>Agent: 返回完整 Schema 或拒绝原因
        Agent->>Runner: invoke(tool, args, idempotency_key)
    end

    Runner->>CP: /tools/{name}/invoke
    CP->>Registry: 执行时再次鉴权
    Registry->>Domain: 允许后执行领域命令
    Domain->>DB: 状态变更 + Outbox + 审计
    Domain-->>CP: 领域结果
    CP-->>Runner: 结果 / 审批要求 / 错误码
    Runner-->>Agent: 工具结果
```

关键点：

- `Authorized Tools` 是权限上限。
- `Tool Profile` 只决定默认披露什么。
- Auxiliary 展开后仍需 PEP 执行鉴权。
- Restricted 工具需要额外授权或审批。
- LLM 不能直接访问数据库或未注册函数。

## 4. 项目交付与组织协作闭环

```mermaid
flowchart LR
    Brief["产品简报 / 项目目标"] --> Project["创建并批准 Project"]
    Project --> Requirement["创建 Requirement<br/>范围 / 优先级 / 验收标准"]
    Requirement --> DoR{"就绪审查 DoR"}
    DoR -->|不通过| Requirement
    DoR -->|通过| Split["拆分 Task<br/>能力 / 估算 / 依赖 / WIP"]
    Split --> Ready["Task Ready"]
    Ready --> Assign["自动推荐或主动认领"]
    Assign --> Claim["原子认领"]
    Claim --> Work["开发 / 设计 / 执行"]
    Work --> Submit["提交 Artifact + 测试结果"]
    Submit --> Review{"评审 / 对抗审查"}
    Review -->|修改| Work
    Review -->|通过| Test["测试与独立验收"]
    Test --> Defect{"发现缺陷？"}
    Defect -->|是| Report["测试提交 Defect"]
    Report --> Fix["开发修复"]
    Fix --> Retest["测试复测"]
    Retest -->|未通过| Fix
    Retest -->|通过| Test
    Defect -->|否| Accept["Requirement Accepted"]
    Test --> Accept
    Accept --> Milestone["里程碑 / 发布门禁"]
    Milestone --> Release["发布 / 运行观测"]
    Release --> Retro["复盘 / 终止或迭代"]
    Retro --> Memory["沉淀 Memory / Knowledge Base"]
    Retro --> Workflow["改进 Workflow 候选"]
    Retro --> KPI["更新 KPI / Performance Review"]

    Requirement -.-> Discussion["线程讨论 / 证据 / 待办"]
    Work -.-> Discussion
    Review -.-> Discussion
    Discussion -.-> Debate["Debate / Review"]
    Debate -.-> Decision{"限时裁决"}
    Decision -.->|形成结论| Requirement
    Decision -.->|超时| Escalate["升级到决策者"]
    Escalate -.-> Decision
```

## 5. 模型调用、预算与计量流程

```mermaid
sequenceDiagram
    participant Runner as Runner / Butler
    participant GW as AI Gateway
    participant Policy as 鉴权 / 预算
    participant KV as BYOK KeyVault
    participant Provider as OpenAI / Anthropic
    participant Ledger as ControlPlane 账本

    Runner->>GW: 原生协议请求 + assignment token
    GW->>Policy: 验证签名、租户、员工、模型档位
    Policy->>Policy: 预留预算 / 检查硬限制
    Policy->>KV: 按租户读取模型密钥
    Policy->>Provider: 注入密钥并调用原生端点
    Provider-->>GW: 原生响应 / 流式输出
    GW-->>Runner: 原生响应 / 流式输出
    GW->>Ledger: 权威用量事件（模型、token、费用）
    Ledger->>Ledger: 结算预留并写入双账本
```

边界：

- OpenAI 使用 `/v1/chat/completions`。
- Anthropic 使用 `/v1/messages`。
- 不做 OpenAI 与 Anthropic 协议转换。
- 控制面不读取模型密钥。

## 6. 公司启动、调度与故障恢复

```mermaid
flowchart TB
    Register["注册租户"] --> Charter["配置 Charter + BYOK"]
    Charter --> RunnerReg["Runner 注册 / 能力画像"]
    RunnerReg --> Controller["Controller: Unassigned"]
    Controller --> SelfPosition["主控自建 Position"]
    SelfPosition --> Bind["绑定 EmployeeRuntime"]
    Bind --> Active["Controller: Active"]
    Active --> Org["按能力缺口规划组织"]
    Org --> Project["创建项目 / 需求 / 任务"]
    Project --> Schedule["能力 / 镜像 / 槽位 / WIP 匹配"]
    Schedule --> Token["签发单任务 assignment token"]
    Token --> Execute["Runner 获取租约并执行"]
    Execute --> Report["提交 Artifact / 状态 / 工具用量"]
    Report --> Complete["确认完成 / 释放租约"]
    Execute -.->|崩溃 / 心跳超时| Recover["租约过期 / fencing / 重调度"]
    Recover --> Schedule
```

Runner 丢失进程内存不丢业务真相；任务、消息和账本以 Postgres 为准，快照和成果以 RustFS 为准，活动工作区可从快照重建。

## 7. 自驱动、KPI 与绩效闭环

```mermaid
flowchart LR
    Work["执行工作"] --> Facts["收集事实证据<br/>Task / Artifact / CI / 成本 / 评审"]
    Facts --> Snapshot["生成不可变 KpiSnapshot"]
    Snapshot --> Self["员工自评 / 反思"]
    Self --> Proposal["ImprovementProposal"]
    Self --> Reviewer["评估者复核"]
    Proposal --> Approve["授权范围内审批"]
    Approve --> Improve["应用流程 / 模型 / 工具 / 组织改进"]
    Improve --> Work
    Reviewer --> Decision["PerformanceReview 结论"]
    Decision --> Action["保持 / 辅导 / 调岗 / 降职 / 冻结 / 解雇"]
    Action --> Followup["下一周期验证效果"]
    Followup --> Facts
    Action -.->|申诉| Appeal["暂停高影响动作 / 独立复核"]
    Appeal -.-> Decision
```

KPI 是证据信号，不直接等于自动解雇。高影响动作需要充分样本、独立复核、申诉和审计。

## 8. 数据权威与信任边界

| 数据或能力 | 权威源 | 其他组件规则 |
|---|---|---|
| 公司组织、项目、工作项、消息 | ControlPlane / Postgres | Runner 只读缓存，不自行改写 |
| 模型密钥 | AI Gateway KeyVault | 控制面、Runner 和前端均不可读取 |
| 模型协议请求与响应 | 原生 Provider | 网关不转换 OpenAI 与 Anthropic 协议 |
| 模型用量与成本 | AI Gateway 用量事件 | Runner 不重复上报模型成本 |
| 工具用量 | Runner 用量事件 | 控制面入账并与模型成本分开 |
| 活动代码工作区 | Runner 本地或容器隔离目录 | 可丢失，从 RustFS 快照恢复 |
| 工作区快照、成果、报告、发布包 | RustFS | 未确认上传不得完成任务，Ready 对象不可覆盖 |
| 租户账户、套餐、资源池 | Operations Platform | 通过 Ops API/事件同步，不直接写租户表 |
| 工具权限 | CapabilityGrant + ToolPolicy | 发现层与执行层同时生效 |
| 审计与公司志 | ControlPlane 不可变事件 | 不提供静默改写或删除 |
| 员工任务身份 | ControlPlane assignment token | 与 Runner 部署身份分离 |

## 9. 可继续补充的架构视图

当前主图覆盖运行时闭环；以下内容适合后续单独出图，而不是继续堆进总图：

1. **招聘与面试流水线**：岗位、候选人、笔试、实操、评分、占槽与录用。
2. **模型评估与能力矩阵**：benchmark 版本、MCP 工具面、云端模型并发与结果复用。
3. **CI/CD 与发布环境**：Git 宿主、构建、测试、制品、发布、回滚和环境晋升。
4. **可观测性拓扑**：OpenTelemetry、指标、日志、追踪、告警、SLO 与错误预算。
5. **备份、恢复与迁移**：加密备份、主密钥恢复、租户导出和恢复演练。
6. **内部通知中心**：站内持久化、SignalR、分级和去重；外部渠道后置。
7. **商业化与计费**：套餐、支付渠道、发票、订阅状态和用量对账。
8. **公共 API 与插件生态**：公共 API、SDK、Webhook、MCP/插件市场和跨租户分享均后置。

## 10. 架构检查清单

- [ ] Runner 之间没有直接业务通信。
- [ ] 控制面不持有模型密钥。
- [ ] OpenAI 与 Anthropic 没有协议转换层。
- [ ] Tool 发现和执行都鉴权。
- [ ] 工具画像不能扩大权限上限。
- [ ] 项目状态不由 LLM 自由描述。
- [ ] 需求就绪、评审、缺陷复测和验收有确定性门禁。
- [ ] 高风险评审作者与评审者分离。
- [ ] 模型成本以网关为权威源。
- [ ] Runner 无持久业务缓冲。
- [ ] 租户控制面与运营平台身份、权限、界面和审计分离。
- [ ] 高影响绩效动作不能由 KPI 自动触发。
