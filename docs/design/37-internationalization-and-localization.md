# 37 国际化与本地化

- 版本：v0.1（待评审）
- 范围：zh-CN/en-US、租户与用户 locale、时区、格式、翻译治理和 Agent 输出语言
- 对应需求：FR-080、BR-126、BR-127、BR-128、BR-129
- 对应缺口：G-51；无障碍缺口 G-52 已延后

## 1. 目标与边界

- 第一梯队界面支持简体中文和英文。
- 存储格式稳定，展示格式可本地化。
- 翻译不改变代码标识、API 契约、审计证据和法律原文。
- 完整无障碍支持后置，不进入 MVP 发布门禁。

## 2. LocalePolicy

```text
id
tenant_id
default_ui_locale
default_content_locale
timezone
date_format_style
number_format_style
week_start
fallback_locale
version
```

- 租户设置默认语言和时区。
- 用户可覆盖 UI locale 和展示时区。
- 不支持的语言回退到租户默认或源语言。
- 策略版本变化不追溯改写历史内容。

## 3. 支持范围

| 范围 | 第一梯队 |
|---|---|
| UI 文案 | zh-CN、en-US |
| 登录与引导 | zh-CN、en-US |
| 仪表盘与审批 | zh-CN、en-US |
| 错误、通知和安全文案 | zh-CN、en-US |
| Agent 工作产物 | 跟随租户或任务语言 |
| 中文技术文档 | 权威版本 |
| 英文公开 README、安装、API 文档 | 同步发布 |
| 其他语言 | 资源包后置 |

## 4. 语言选择顺序

```text
用户显式选择
  → 租户默认
  → 浏览器 Accept-Language
  → zh-CN 回退
```

- 服务端是权限和策略真相，前端只保存展示偏好。
- 语言切换不改变租户、角色、权限或数据范围。
- 深链接切换语言后保留当前页面和筛选。

## 5. 资源与文案

- 使用 BCP 47 语言标签。
- 所有 UI 文案使用资源 key，不硬编码。
- 禁止字符串拼接表达完整句子。
- 支持复数、性别和 RTL 布局。
- 缺失翻译回退源语言并发出遥测，不显示资源 key。
- 源文案变化使相关翻译失效。

## 6. TranslationResource

```text
id
resource_key
source_locale
source_text
target_locale
target_text
status
version
source_version
reviewed_by?
reviewed_at?
```

状态：

- `Draft`
- `MachineTranslated`
- `Reviewed`
- `Published`
- `Stale`

## 7. TranslationReview

- 安全、法务、审批、退款和合规文案必须人工复核。
- 普通界面文案可以机器翻译后抽查。
- 复核者不能修改源文案来掩盖翻译问题。
- 发布版本绑定产品版本，可回滚。
- 法律原文与译文并存时，明确哪个版本权威。

## 8. 时间与时区

- 数据库和 API 使用 UTC/RFC 3339。
- 展示使用 IANA 时区，例如 `Asia/Shanghai`。
- 不保存固定 UTC offset 作为时区真相。
- 相对时间同时提供准确时间。
- cron 和调度使用租户时区，Q-041 的作息规则继续适用。
- 夏令时不存在或重复时间按已有确定性规则处理。

## 9. 日期、数字和币种

- 日期、数字、百分比、排序和周起始按 locale 格式化。
- 使用 CLDR/ICU 数据，不手写国家格式分支。
- 币种显式使用 ISO 4217，不能从语言推断。
- 金额内部使用最小货币单位整数，禁止浮点。
- 界面同时显示币种代码，避免符号歧义。
- 导出和 API 保持稳定机器格式，本地化只用于展示。

## 10. 搜索与排序

- 搜索保留原始文本，可同时使用语言感知分词。
- 大小写、重音和 Unicode 规范化不能改变原文。
- 排序按 locale，技术 ID 和审计 seq 保持稳定排序。
- 多语言结果标注语言和翻译状态。

## 11. Agent 输出语言

- 默认跟随租户内容语言，任务可显式覆盖。
- 输出记录 `content_locale` 和 `translation_status`。
- 原始输入与译文都保留引用关系。
- 代码标识、API 字段、Schema、审计 payload 和法律原文不自动翻译。
- 翻译产物标记为翻译，不冒充原文。
- 多语言质量进入 Q-036 的偏差审计。

## 12. 文档

- 中文技术文档为权威源。
- 英文公开 README、安装和 API 文档与发布同步。
- 详细设计文档不要求首版全量翻译。
- 文档版本、源语言和翻译状态可追溯。
- 机器翻译不能替代法律、安全或审批文案复核。

## 13. 伪本地化与测试

- 开发环境启用伪本地化，检查硬编码和布局溢出。
- zh-CN 和 en-US 各运行关键 E2E。
- 覆盖登录、仪表盘、项目详情、升级审批和表单。
- 测试长文本、复数、日期、数字、币种和时区。
- 验证缺失资源回退和 Stale 翻译提示。
- API 和审计不因 locale 改变语义。

## 14. API 与事件

- `GET /locale-policy`
- `PUT /locale-policy`
- `GET /translations`
- `POST /translations/{id}/review`

请求上下文：

```text
Accept-Language
ui_locale
content_locale
timezone
```

事件：

- `LocalePolicyChanged`
- `TranslationPublished`
- `TranslationStaled`
- `TranslationReviewed`

## 15. 指标

| 指标 | 目标 |
|---|---:|
| 缺失翻译 key 数 | 0 |
| 关键流程双语覆盖率 | 100% |
| Stale 安全/法律文案发布数 | 0 |
|  locale 格式测试覆盖率 | 100% |

## 16. 无障碍后置

- 完整无障碍支持不进入 MVP 发布门禁。
- 当前不要求 WCAG 2.2 AA 认证、屏幕阅读器专项或键盘流程全覆盖。
- 后续以 WCAG 2.2 AA 为目标，覆盖 UI 和生成内容。
- 后续实施需补充 AccessibilityAudit、辅助技术测试和发布门禁。
- 本延期不改变 Q-036 的招聘合理便利规则。

## 17. 测试场景

1. 用户语言覆盖租户默认。
2. 不支持的语言正确回退。
3. 缺失资源不显示 key。
4. 切换语言不改变权限和租户。
5. UTC 时间按用户时区展示。
6. API 时间仍为 RFC 3339 UTC。
7. 日期和数字按 locale 格式化。
8. 币种不随语言变化。
9. 金额内部保持整数。
10. 翻译版本可回滚。
11. 源文案变化使翻译 Stale。
12. 安全和法律文案未经复核不能发布。
13. Agent 输出记录语言和翻译状态。
14. 代码标识和审计 payload 不被翻译。
15. 中英文关键 E2E 均通过。
16. 无障碍不阻塞 MVP 发布。

## 18. 验收场景

1. zh-CN 和 en-US 关键流程完整可用。
2. 租户与用户 locale 正确解析。
3. 时间、数字和币种格式稳定。
4. 翻译资源可追踪、可复核和可回滚。
5. Agent 输出保留原文与翻译关系。
6. 安全、法律和审批文案经过人工复核。
7. 完整无障碍明确后置，不形成虚假的合规声明。

## 19. 参考

- W3C WCAG 2.2（后续目标）：https://www.w3.org/TR/WCAG22/
- ASP.NET Core 本地化：https://learn.microsoft.com/aspnet/core/fundamentals/localization
