# ADR-0002：前端采用 React 19、TypeScript、MUI 与 SignalR

- 状态：Accepted
- 日期：2026-11-23
- 范围：Tenant Dashboard 与 Ops Console

## 背景

前端不使用 C# 技术栈，需要独立应用、统一 Material Design 组件体系和实时状态更新。

## 决策

- 前端使用 React 19、TypeScript 和 MUI。
- Tenant Dashboard 与 Ops Console 是两个独立 SPA。
- 实时通信使用 SignalR TypeScript 客户端。
- 两个前端只共享 TypeScript UI 包，不共享会话、权限或业务 API 客户端。
- 组件展示页使用 `/dev/components`，生产默认关闭。

## 结果

- 前后端技术栈解耦。
- 组件和主题可统一管理。
- 需要维护 OpenAPI 生成的 TypeScript Client 和 SignalR Hub 类型。

## 未采用

- Blazor/Razor：不符合前端技术栈决策。
- 单 SPA 加菜单隐藏：无法形成租户与运营的独立安全边界。

## 关联

- [前端技术文档](../Regent-Agent-前端技术文档.md)
- [前端页面与路由地图](../design/48-frontend-route-map.md)
