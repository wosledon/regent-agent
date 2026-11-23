# ADR-0001：后端采用 .NET 10 与 MAF

- 状态：Accepted
- 日期：2026-11-23
- 范围：后端语言、运行时和 Agent 框架

## 背景

系统需要长期维护的租户业务、模型编排、任务调度和沙箱能力，且维护者主要使用 C#。

## 决策

- 后端使用 C# 和 .NET 10 LTS。
- 常驻服务使用 ASP.NET Core、Generic Host 和 BackgroundService。
- Agent、Workflow、Skill 和 Hosting 使用 Microsoft Agent Framework 1.0。
- 账本、权限、事务、消息和沙箱等核心能力使用纯 C# 实现，不绑定 MAF 内部模型。

## 结果

- 技术栈统一，适合长期维护。
- MAF 负责 Agent 编排，核心业务规则保持独立。
- 需要锁定兼容的 .NET SDK、NuGet 和 MAF 版本。

## 未采用

- Go/Rust：并发优势不是当前主要瓶颈。
- Python 后端：增加多语言运维和部署成本。

## 关联

- [技术设计总览](../Regent-Agent-技术设计.md)
- [后端技术文档](../Regent-Agent-后端技术文档.md)
