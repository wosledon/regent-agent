# 50 可插拔外部适配与本地默认

- 版本：v0.1
- 状态：Accepted
- 范围：Provider、模型目录、关系/对象存储、密钥库、外部身份、可观测性、审计锚定、备份与部署组件
- 关联：ADR-0013、03、15、16、17、22、26、27、31、39、43

## 1. 目标与边界

1. 所有依赖远程服务或额外组件的能力必须在组合根通过配置选择，业务模块只依赖稳定端口。
2. 本地默认实现必须可独立运行和测试；选择远程适配但配置不完整时快速失败，不静默回退。
3. 远程适配只实现协议、配置、超时、取消、重试和错误映射；本次不连接或调试真实组件。
4. 适配器不得扩大租户、权限、预算、审计或保留边界，也不得把外部内容写入敏感日志。
5. 生产目标仍是 Postgres + 强制 RLS、RustFS/S3、OpenAI/Anthropic 原生端点；本地实现不表述为生产替代。

## 2. 当前适配盘点

| 能力 | 当前实现 | 现有边界 | 需要补齐 |
|---|---|---|---|
| 模型调用 | `IProviderClient` + `LocalProviderClient` + `IProviderRouter` | 默认本地，可配置 remote；OpenAI/Anthropic HTTP 适配已实现 | 流式与真实契约冒烟仍按约束不调试 |
| 模型目录 | `IModelCatalogSource` + 本地/models.dev 源 | `ModelOperationsService` 通过端口选择，快照保存 raw JSON/checksum | 自动分档与真实远程同步仍按约束不调试 |
| 关系存储 | EF Core SQLite/Postgres 分支 | Provider 显式校验；生产禁止启动建表/种子，要求 SchemaVersion 和租户表强制 RLS | 真实 Postgres 迁移与 RLS 集成测试按约束不调试 |
| 对象存储 | `IObjectStore` + 本地/S3 适配 | `Storage:ObjectProvider` 在组合根选择 Local 或 S3，S3 保存 checksum 元数据 | 真实 RustFS 连通性按约束不调试 |
| Gateway 密钥 | `IKeyVault` + 本地文件/Postgres 密钥库 | `Gateway:KeyVault:Provider` 选择 local 或 postgres；Postgres 使用信封加密、强制 RLS 和租户事务作用域 | 真实 Postgres/外部 KMS 按约束不调试 |
| 外部身份 | `IExternalIdentityProvider` + 本地/GitHub/OIDC 适配 | `Identity:Mode` 选择 local 或 remote；OIDC 校验 discovery/JWKS、issuer/audience/nonce，GitHub 交换 code 并读取已验证邮箱 | 真实 IdP 连通性按约束不调试 |
| 可观测性 | `RegentAgent.Observability` + OpenTelemetry SDK | 四个服务通过 Profile 选择 off/local/otlp/elastic；启用 ASP.NET Core/HTTP 仪表、严格日志/Trace 脱敏和 5 秒导出超时 | 前端遥测与真实 OTLP/Elastic 连通性按约束不调试 |
| 审计锚定 | `IAuditAnchorStore` + 本地/对象存储适配 | `Audit:AnchorProvider` 选择 Local 或 ObjectStore，检查点写入后可按租户和序号验证 | 真实外部锚定服务按约束不调试 |
| 备份 | Python manifest + 可选 S3 上传/恢复 | 默认本地，`--target s3 --target-uri` 或 `s3://` 恢复；未安装 boto3 时 fail fast | 真实 S3/NAS 连通性按约束不调试 |
| 部署 | Compose 可选 Profile | 默认 SQLite/本地；`postgres`、`rustfs`、`observability` 显式启用 | 不启动或调试容器，仅校验 YAML 与配置契约 |

## 3. 统一选择规则

- 配置节使用 `Mode`、`Provider` 或 `Profile` 明确选择，默认值必须是本地模式。
- 组合根是唯一读取外部 endpoint、凭据引用和组件开关的位置；领域服务不读取 `IConfiguration`。
- 每个适配器暴露 `CanHandle` 或由工厂按配置分派；同一租户内不允许模糊匹配。
- 远程适配必须校验 HTTPS、endpoint allowlist、超时、取消和最大响应大小；禁止访问私网或元数据端点，除非显式允许。
- 秘密只通过环境、secret 文件或密钥系统注入；配置中只保存 `secret_ref`。
- 外部调用失败保留最近有效本地状态并标记 `Stale`，不得用空数据覆盖权威状态。

## 4. Provider 与模型目录端口

```csharp
public interface IProviderClient
{
    string Provider { get; }
    bool CanHandle(string provider, string protocol);
    Task<ProviderResponse> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken);
}

public interface IModelCatalogSource
{
    string SourceId { get; }
    Task<CatalogSnapshot> FetchAsync(DateOnly snapshotDate, CancellationToken cancellationToken);
}
```

- Gateway 已实现 `IProviderRouter`，按 `Gateway:ProviderMode=local|remote` 和端点协议选择 OpenAI、Anthropic 或 Local 客户端。
- 远程客户端接收并透传 provider 原生 JSON，只注入实际 model 和密钥；成功响应和原生错误响应保持 provider 形状，并使用有界超时、重试和 `Retry-After`。
- models.dev 适配保存原始响应、checksum、获取时间和解析结果；失败时保留最近有效快照，本地源与远程源使用同一快照契约。
- 本地源与远程源使用同一快照契约，价格字段保持 `models.dev`/`manual` 来源和 `Unpriced` 语义。

## 5. 存储与密钥端口

- `IObjectStore` 保持为对象字节端口；生产适配直接使用 S3 SDK/RustFS endpoint，不新增业务存储抽象。
- 组合根根据 `Storage:ObjectProvider=Local|S3` 选择实现；S3 配置包含 endpoint、bucket、region、forcePathStyle 和凭据引用。
- EF Core 根据 `Storage:Provider=Sqlite|Postgres` 选择关系提供程序；生产启动必须禁用自动迁移并验证 SchemaVersion。
- `IKeyVault` 保持为 Gateway 密钥端口；本地文件实现用于开发，Postgres 实现使用独立角色、`keyvault` schema 和 RLS。
- 主密钥永不进入数据库或备份对象；Postgres 密钥库只存密文、包装数据密钥和版本元数据。

## 6. 外部身份端口

```csharp
public interface IExternalIdentityProvider
{
    string Provider { get; }
    Task<ExternalIdentityResult> AuthenticateAsync(ExternalIdentityRequest request, CancellationToken cancellationToken);
}
```

- 本地适配器使用显式测试 issuer/subject；GitHub 适配实现 Authorization Code + PKCE、state 和已验证邮箱。
- OIDC 适配实现 discovery/JWKS、issuer+sub、audience、nonce、state、acr/amr 和邀请匹配。
- 远程身份不可用时 fail closed；已有本地会话按自身有效期继续，不静默回退本地密码。
- 外部 subject 不可变，邮箱只用于首次邀请或已验证唯一合并。

## 7. 可观测性、锚定与备份

- 使用 OpenTelemetry SDK 和统一启动扩展实现 `off/local/otlp/elastic` Profile；Elastic 通过 OTLP/Collector 接入，不写业务数据库。
- `local` 使用控制台/文件导出，`otlp` 使用配置 endpoint，`elastic` 使用可选 Collector/APM 拓扑；遥测故障不得阻塞业务。
- `IAuditAnchorStore` 负责检查点锚定和验证；本地 JSONL 为开发实现，HTTP/对象存储为可选远程实现。
- `IBackupTarget` 负责备份对象写入、读取、校验和删除；本地目录为默认，S3/NAS/外置盘通过配置选择。
- 备份默认关闭；未配置目标时恢复返回 `not_configured`，不得伪造 RPO/RTO 或成功状态。

## 8. 配置草案

```yaml
gateway:
  providerMode: local
  providers:
    openai:
      baseUrl: https://api.openai.com/v1
      timeoutSeconds: 120
    anthropic:
      baseUrl: https://api.anthropic.com/v1
      timeoutSeconds: 120
modelCatalog:
  mode: local
  modelsDevUrl: https://models.dev/api.json
storage:
  provider: Sqlite
  objectProvider: Local
  s3:
    endpoint: http://rustfs:9000
    bucket: regent-artifacts
    forcePathStyle: true
observability:
  profile: local
  otlpEndpoint: http://collector:4317
audit:
  anchorProvider: local
backup:
  target: local
  enabled: false
```

## 9. 验证要求

- 单元测试使用 fake HTTP handler、内存/临时文件和测试替身，不依赖真实组件。
- 配置测试覆盖默认本地模式、远程模式缺少 endpoint/secret、非法协议和未知 Provider。
- 契约测试覆盖 OpenAI/Anthropic 原生请求字段、响应、429、5xx、超时和取消。
- 存储测试覆盖对象键越界、checksum、幂等上传、下载权限和删除传播。
- 身份测试覆盖 state/nonce 不匹配、issuer/audience/subject 校验、邀请缺失和 fail closed。
- 可观测性测试覆盖 Profile 选择、redaction、Exporter 故障降级；备份测试覆盖默认关闭和目标校验。

## 10. 实施顺序

1. Provider 路由、原生 HTTP 客户端和 models.dev 目录源。
2. S3/RustFS、Postgres keyvault、GitHub/OIDC、OpenTelemetry、审计锚定和备份目标。
3. 更新 Compose Profile、配置样例和运维文档。
4. 按 01～49 设计逐模块审计，修复协议、状态机、权限、数据边界和失败语义偏差。
