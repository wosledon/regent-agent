# ADR-0006：API、Tool 与 MCP 仅用于系统内部

- 状态：Accepted
- 日期：2026-11-23
- 范围：开放接口和扩展生态

## 背景

当前优先完成自主交付闭环，公共 API 和插件市场会增加认证、限流、计费、审核和兼容承诺。

## 决策

- `/v1` 仅用于系统内部调用。
- 不提供公共 REST API、对外 API Token、OAuth Client 或公共 SDK。
- Tool 以 ToolManifest + AIFunction 为主。
- MCP 仅用于 Butler 只读工具和模型评测工具面。
- 不建设 MCP 市场、插件市场、跨租户 Skill 分享或自动安装更新。
- 租户内部扩展仍按 Q-021 供应链规则审核。

## 结果

- 安全边界和实现范围更小。
- 内部 `/v1` 不构成公共 SemVer 或长期兼容承诺。
- 公共生态能力将来作为新需求评审。

## 未采用

- 同时建设公共 API 和市场：超出当前 MVP。
- 空壳公共接口：制造未实现兼容承诺。

## 关联

- [42 内部 API 与扩展边界](../design/42-internal-api-and-extension-boundary.md)
- [47 OpenAPI 基线](../design/47-openapi-baseline.md)
