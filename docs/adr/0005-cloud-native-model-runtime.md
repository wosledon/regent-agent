# ADR-0005：模型运行仅支持 OpenAI 与 Anthropic 云端原生端点

- 状态：Accepted
- 日期：2026-11-23
- 范围：Provider 协议和本地推理

## 背景

系统需要明确的模型运行边界，避免协议转换、本地 GPU 调度和隐式跨模型降级。

## 决策

- 只支持 OpenAI 与 Anthropic 云端原生端点。
- OpenAI 使用 `/v1/chat/completions`，Anthropic 使用 `/v1/messages`。
- 不做请求、响应、工具调用或错误结构的跨协议转换。
- 不支持本地 GPU、Ollama、本地模型档或 GPU 排队抢占。
- 模型失败只允许同模型、同版本、同推理强度和同协议恢复。

## 结果

- Provider 契约和错误语义清晰。
- 完全断网时无法进行模型推理。
- 本地推理如将来引入，必须作为新需求评审。

## 未采用

- 通用 OpenAI-compatible 网关：掩盖原生协议差异。
- 自动跨模型 fallback：破坏员工模型绑定和成本归属。

## 关联

- [03 模型网关、计量与预算](../design/03-model-metering-budget.md)
- [Q-044 决策](../Regent-Agent-待确认项.md)
