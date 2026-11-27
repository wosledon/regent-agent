# 09 记忆系统与遗忘机制

- 版本：v0.6（schema 9 注释与本地实现基线）
- 范围：分层记忆、知识库、混合 RAG、图谱多跳、遗忘、删除、投毒纠错与评估
- 对应缺口：G-19、G-26、G-32

## 1. 定位

记忆系统负责让员工和组织“记得对、找得到、权限不泄露、过期能失效”。

它是统一记忆模型与知识库发布视图，不是原始日志仓库：

```text
个人记忆 → 组织记忆 → 知识库
   │            │           │
   └──────── 混合 RAG + 有界图谱多跳 ────┘
```

目标架构以 Postgres 为权威源、pgvector/全文检索/关系表为索引与检索结构，RustFS 保存大内容。当前 schema 9 延续 005 的 `MemoryEmbeddings` 向量投影并由 006 补充中文目录注释；claim、Finding、图谱和纠错等本地模块仍使用 JSON，不能把目标存储形态描述为已全部迁移。

## 2. 核心不变量

1. 个人记忆默认私有，主控和其他员工不能直接读取。
2. 组织记忆绑定 `scope_position_id`，知识库是公司根范围的公共资产。
3. 每条记忆必须有来源、版本、置信度和验证状态。
4. 检索先做租户和范围过滤，再做向量、关键词和关系扩展。
5. 检索结果是数据，不改变 Tool 或能力权限。
6. 不可信来源的 taint 必须跨摘要、组织和知识库提升保留，不得洗白。
7. 已发布版本不可变，更正和替代通过新版本完成。
8. 遗忘由确定性策略执行，LLM 不任意删除记忆。
9. 彻底删除必须同步清理向量、全文、图关系、缓存和派生内容。
10. 自动捕获只生成候选，不自动发布组织知识。
11. 跨租户共享默认关闭。
12. 秘密不得进入记忆；PII 必须按 SensitiveDataPolicy 脱敏并保留令牌映射引用。
13. 员工离职后私有记忆默认隔离，再入职不自动恢复。
14. 个人 claim 的所有权使用稳定 `author_user_id` 或 `author_employee_id`；显示名不参与授权。历史仅含显示名的 claim 默认 fail closed，只有调用方显式启用且租户内姓名唯一时才允许兼容读取或隔离。

## 3. 记忆分层与类型

### 3.1 分层

| 层 | 内容 | 默认可见 | 生命周期 |
|---|---|---|---|
| 工作记忆 | 当前任务上下文、中间结果 | 当前员工 | 任务结束快速清理 |
| 个人记忆 | 观察、经验、偏好、失败尝试 | 仅自己 | 员工控制 |
| 组织记忆 | 已验证的项目知识、决策、复盘 | 岗位子树 | 组织策略 |
| 知识库 | 公司级事实、方法、规范、FAQ | 全公司 | 根范围治理 |

### 3.2 类型

```text
MemoryKind:
  episodic      发生过什么
  semantic      事实、概念、结论
  procedural    方法、流程、技能
  preference    个人或岗位偏好
  reflection    复盘、教训、改进
```

- 原始任务记录通常是 Episodic。
- 可复用经验通常是 Semantic 或 Reflection。
- Workflow 属于 Procedural，可引用记忆作为依据。
- Preference 默认私有。

## 4. 核心实体

```text
MemoryRecord:
  id, tenant_id, kind
  scope_type: personal | organization | knowledge_base
  owner_user_id?, owner_employee_id?, scope_position_id?
  title, summary, current_version_id
  confidence, confidence_basis
  verification_status, sensitivity
  status, retention_class
  created_at, last_verified_at, valid_until?

MemoryVersion:
  id, memory_id, version
  content_artifact_id
  source_type, source_refs
  author_id, created_at, change_summary

MemoryRelation:
  id, tenant_id, from_id, to_id, relation_type, weight

MemoryProposal:
  id, memory_id, from_version_id
  target_scope_type, scope_position_id?
  reason, proposed_by, created_at

MemoryReview:
  id, proposal_id, reviewer_employee_id
  decision, comment, decided_at

MemoryEmbedding:
  memory_version_id, embedding_model, dimension
  vector, content_hash, indexed_at

MemoryUsageFeedback:
  id, memory_version_id, employee_id
  useful | outdated | incorrect | conflicting
  comment, created_at

KnowledgeArticle:
  id, tenant_id, title, summary, body_artifact_id
  kind, source_memory_ids[]
  version, review_status, tags
  confidence, last_verified_at
```

## 5. 范围与沉淀

### 5.1 个人范围

- 仅 `owner_employee_id` 可读写。
- 主控、Leader 和其他员工默认不可见。
- 员工可自行分享单条记忆，但分享不等于组织发布。

### 5.2 组织范围

- 绑定 `scope_position_id`。
- 可见成员由当前组织树中该岗位及其子树决定。
- 员工只能沿自己所在岗位的上级路径提名。
- 提名生成新的组织版本，不移动原个人记录。

### 5.3 知识库

- 组织范围中的根岗位公共视图。
- 只接收已审查的 Semantic、Procedural 和 Reflection 内容。
- 普通员工只读，修改通过 Proposal。
- 不直接接收原始日志、偏好、未验证推断和敏感数据。

### 5.4 组织变化

- 范围引用稳定 `position_id`，按当前组织树计算成员。
- 发布记录保存组织树版本和路径快照。
- 范围岗位关闭或合并时进入 `scope_orphaned`，停止扩散，直到重新指定范围或归档。

## 6. 状态与置信度

```text
Captured → Candidate → Curated → Proposed → Published → Verified
                                      │  ├─ RequestChanges → Curated
                                      │  └─ Rejected
Verified → Stale → Forgotten → Archived → Purged
```

- Captured：个人原始记录。
- Candidate：候选知识。
- Curated：已整理来源、脱敏和置信度。
- Published：正式共享。
- Verified：最近验证有效。
- Stale：需要重新验证。
- Forgotten：停止普通召回。
- Archived：仅授权追溯。
- Purged：内容和索引彻底清除。

置信度由来源权威度、验证次数、冲突情况和时效性计算，不由 LLM 自评。

## 7. 遗忘机制

### 7.1 四层遗忘

| 层级 | 行为 | 可召回 |
|---|---|---|
| 降权 | 仍存在，相关性和置信度下降 | 是，排名靠后 |
| 停止召回 | Stale 或 Forgotten | 普通检索不可见 |
| 归档 | 移出活跃索引，保留审计 | 仅授权追溯 |
| 删除 | 清除内容和索引 | 否 |

### 7.2 触发条件

- 时间衰减。
- 长期低使用。
- 低置信度。
- 验证过期。
- `valid_until` 到期。
- 新版本 `supersedes` 旧版本。
- 冲突长期未解决。
- 员工或组织主动删除。
- 安全事件或合规要求。

### 7.3 检索评分

```text
effective_score =
  relevance
  × confidence
  × recency_decay
  × verification_factor
  × usage_weight
  × scope_weight
```

评分只影响排序，不单独决定物理删除。

### 7.4 保留策略

- 工作记忆：任务结束后 7 天清除。
- 个人情节记忆：180 天后归档 1 年。
- 语义记忆：依赖有效期和验证时间，过期后归档。
- 程序记忆：随工作流版本废弃，不自动消失。
- 安全、制度、事故和合规知识：长期保留，不参与普通衰减。
- 法律保留期间禁止物理清除，但不恢复普通召回。
- 统一规则见 [30 在线保留与归档](./30-retention-and-archiving.md)。

## 8. 合并与压缩

- 重复记忆合并。
- 多条情节记忆可总结为语义记忆。
- 摘要必须保留 `derived_from` 来源。
- 原始证据进入归档，不直接消失。
- 摘要不能获得高于来源的置信度。
- 冲突和反证不能被压缩掉。
- 工作记忆摘要必须能追溯到 TaskCheckpoint、工具事件和 Artifact。
- 检索记忆放在动态上下文后缀，不插入系统规则、岗位章程和 Tool Schema 的稳定前缀。
- 跨任务只复用稳定前缀，不复制任务私有对话、检查点和未审查记忆。

## 9. 混合 RAG 召回

### 9.1 流程

```text
查询
 → 身份/租户/范围解析
 → SQL 权限预过滤
 → 向量 + 关键词 + 元数据召回
 → 有界关系扩展
 → 去重、冲突检测、重排序
 → 组装带引用的上下文
```

### 9.2 首轮召回

- pgvector 相似度。
- PostgreSQL 全文检索。
- 类型、项目、岗位、标签、时间等元数据过滤。
- 按相关度、置信度和时效性融合排序。

### 9.3 上下文组装

- 去重。
- 多样性控制。
- token 预算。
- 引用 ID。
- 冲突提示。
- 个人、组织、知识库使用不同权重。

### 9.4 聊天加权召回

聊天是弱证据：已提升为 Decision 的聊天权重 0.7，普通项目群 0.4，私人聊天 0.3，未验证传言 0.1。最终分数还乘相关度、时间衰减、作者可信度和验证程度。

- 先执行会话成员和范围权限过滤。
- 关联 Task、被采纳或有 Artifact 佐证可提高权重。
- 重复刷屏、过期和违规内容降权或不召回。
- 默认召回线程摘要和相关消息，完整线程按需展开。
- 摘要保留 `derived_from`，不能覆盖正式 Decision。

## 10. 图谱多跳

### 10.1 关系类型

```text
derived_from
supports
contradicts
supersedes
applies_to
related_to
used_by_workflow
```

### 10.2 扩展策略

- 事实查询：默认 0 跳。
- 方案查询：最多 1 跳。
- 原因、复盘、决策查询：最多 2 跳。
- 默认最多 50 个节点。
- 每一跳重新检查租户、范围、归档和冲突状态。
- 使用 Postgres 递归 CTE 或有界 BFS，不引入图数据库。

### 10.3 反证召回

检索时必须同时考虑：

- 支持当前结论的记忆。
- 反对当前结论的记忆。
- 已替代版本。
- 失败案例。
- 过期规则。

## 11. 写入与索引

```text
memory.capture
 → 参数校验
 → 敏感信息扫描与脱敏
 → 去重与冲突检查
 → 写入 Record + Version
 → 写 Outbox
 → Embedding Worker
 → 更新向量、全文和关系索引
```

- Embedding 调用走 AI Gateway，费用正常入账。
- 内容哈希变化时创建新向量。
- Embedding 模型和版本必须记录。
- 换模型时重建索引，旧索引保留到新索引验证完成。

## 12. 删除传播

删除事件通过 Outbox 分发：

```text
memory.purge.requested
 → 停止召回
 → 删除向量
 → 删除全文索引
 → 删除图关系
 → 清理缓存
 → 删除派生摘要
 → 清除 RustFS 内容
 → 更新审计和备份保留计划
```

- 先逻辑删除，再异步物理清除。
- 所有步骤幂等。
- 清除失败进入重试和告警。
- 备份中的副本按保留策略到期清除。

## 13. 本地实现基线（2026-11-25）

- `IMemoryVectorStore` 统一租户内 CRUD、租户清除、显式候选集合和最多 200 条的有界余弦搜索。`LocalEmbeddingProvider` 仍生成确定性的 256 维 `local-hash-v1` 向量。
- `Storage:Provider=Postgres` 且未启用 `Storage:UseInMemory` 时，`PostgresMemoryVectorStore` 使用 Npgsql/ADO.NET：每次操作开启事务、`SET LOCAL app.tenant_id`、参数化租户/claim 谓词，并以 `vector(256)` 字面量写入。租户 ID、维度和有限浮点数校验失败时直接报错，不回退 JSON。
- PostgreSQL `MemoryEmbeddings` 使用 `(TenantId, ClaimId)` 复合主键、强制 RLS 和 `vector_cosine_ops` HNSW 索引；`<=>` 余弦距离按完整权限候选集合取 top-k，生产 SQL 与集成 EXPLAIN 共用同一参数构造。真实运行角色测试保留 `TenantId`、`ClaimId = ANY`、`<=>` 和 `LIMIT`，不裁剪候选；当前本地统计下优化器可为这组高选择性 ACL 条件选择复合主键或顺序扫描，不能把 HNSW 描述为必然计划。SQLite/InMemory 继续使用 `JsonMemoryVectorStore` 和原有 `memory-vectors.json` 格式及本地余弦实现。
- 租户 GUID 在向量端口和 `MemoryIndexService` 边界按 `Guid` 值解析；大写、花括号和非连字符文本形式视为同一租户，JSON 文档形状不变，图谱与 Finding 的租户过滤也采用相同语义。
- `MemoryIndexService` 先构建租户、状态、scope 和个人作者 ACL 候选集（最多 200），再只把这些 claim ID 传给向量存储；个人 ACL 优先匹配 `AuthorUserId` 或 `AuthorEmployeeId`，不再按显示名回退。随后仍按向量 0.55、关键词 0.35、元数据 0.10 融合，元数据包含置信度、验证状态、使用次数和时效衰减。
- 人类 `/v1/memories*` 端点从认证上下文传入稳定用户 ID，Agent memory tool 从 assignment identity 传入稳定员工 ID；纠正和提名复制 claim 时保留原稳定作者 ID。
- 图扩展最多取 3 个种子、每个种子 2 跳且最多 20 个节点；扩展只能提升已通过权限过滤的候选。普通召回阈值仍为 0.30，Finding 降权、冲突检测、quarantined/forgotten/superseded 排除和纠错图迁移行为保持不变。
- 删除和租户清除通过同一向量端口同步删除 PostgreSQL 向量或 JSON 向量；PostgreSQL RLS 同时限制读写，应用层候选过滤不能替代 RLS。
- `Positions.Path` 已在 PostgreSQL 映射为 ltree 并建立 GiST 索引，支持子树和 `nlevel` 查询；当前混合召回仍使用既有 scope/ACL 候选逻辑，尚未把完整岗位子树传播接入检索。
- claim、Finding、图谱节点/边、CorrectionTask、影响记录和上下文绑定仍为 JSON 本地模块；PostgreSQL 全文检索、Embedding Gateway 计费和完整生产权限模型仍未实现。

## 14. 维护任务

| 任务 | 职责 |
|---|---|
| DecayJob | 计算衰减并降权 |
| VerificationExpiryJob | 标记过期验证 |
| ConflictResolutionJob | 识别冲突并生成审查任务 |
| ConsolidationJob | 合并重复、生成派生摘要 |
| ArchiveJob | 移出活跃索引 |
| PurgeJob | 物理删除内容与索引 |
| EmbeddingRebuildJob | 更换模型后重建索引 |

任务通过 Outbox 驱动，不直接扫描修改所有表。

## 15. 评估

建立标准问题集和期望记忆 ID，至少评估：

- Recall@K。
- 稳定前缀缓存命中率与可缓存输入 token 命中率。
- 动态检索插入对稳定前缀缓存的影响。
- MRR。
- 引用准确率。
- 冲突遗漏率。
- 过期记忆命中率。
- 权限泄漏数，目标 0。
- P95 延迟。
- token 和 Embedding 成本。
- 删除传播延迟。
- 遗忘前后召回变化。

## 16. Tool 与 API

```text
memory.capture
memory.search
memory.relate
memory.curate
memory.propose
memory.review
memory.publish
memory.correct
memory.archive
memory.forget
```

API：

- `POST /memories`
- `POST /memories/search`
- `POST /memories/{id}/relations`
- `POST /memories/{id}/proposals`
- `POST /memory-proposals/{id}/review`
- `POST /memories/{id}/archive`
- `POST /memories/{id}/purge`

所有 Tool 受 Tool Registry/PEP 管理，工作流和记忆都不能提升权限。

## 17. 投毒、隔离与纠错

- 记忆拆分为 MemoryClaim，每个 claim 独立保存来源、证据、置信度、taint、冲突和引用。
- MemoryFinding 按风险分级：高风险停止召回，低风险降权复核。
- CorrectionTask 使用普通任务机制，复核者与作者和来源提交者分离。
- 纠错生成 MemoryCorrection 新版本，不静默修改旧 claim。
- MemoryImpactRecord 追踪员工、任务、摘要、决策和外部动作引用。
- 在途任务重新检索，已完成工作创建缺陷、复审或回归，不自动回滚。
- 纠正通过 Outbox 传播到向量、全文、图、缓存、派生摘要和通知。
- 隔离支持反证申诉，员工报告投毒不扣 KPI。
- 详见 [19 记忆投毒与纠错](./19-memory-poisoning-and-correction.md)。

## 18. 安全

- 所有查询强制 `tenant_id`。
- 个人记忆严格限制稳定 owner ID；Agent claim 同时绑定 `AuthorEmploymentPeriodId`，显示名相同、更名或再入职均不改变旧周期访问限制，缺失稳定 ID 的历史 claim 默认不可读、不可隔离。
- 组织记忆按岗位子树过滤。
- 权限过滤进入 SQL，不能只在应用层。
- 检索结果标记为数据，不改变系统指令或 Tool 权限。
- 自动捕获不保存密钥、Authorization 头和原始系统提示词。
- 外部内容默认低信任，不能自动发布高置信度知识。
- 发布、更正、遗忘和删除全部审计。

## 19. 验收场景

1. 员工个人记忆不能被主控或其他员工检索。
2. 个人记忆可以沿组织树向上提名，不能跨分支发布。
3. 知识库只包含已审查的根范围知识。
4. 检索先过滤权限，再执行向量和关键词召回。
5. 多跳扩展不会进入个人记忆或其他租户。
6. 过期记忆降低权重并最终停止召回。
7. 新版本替代旧版本，历史版本可追溯。
8. 摘要保留来源，不获得高于原文的置信度。
9. 删除后向量、全文、图关系、缓存和派生摘要均停止召回。
10. 遗忘由确定性策略执行，LLM 不能任意删除。
11. 安全、制度和合规知识不参与普通衰减。
12. Embedding 模型更换后可重建索引并验证召回质量。
13. 检索评估可以发现权限泄漏、过期命中、冲突遗漏和准确率下降。
14. 跨租户记忆、知识库和索引默认不可见。
15. 注入攻击内容进入记忆后仍保留来源和 taint。
16. 高风险 claim 被隔离后立即停止普通召回。
17. CorrectionTask 作者不能复审自己的纠错。
18. 纠错生成新版本并保留旧版本追溯。
19. 引用影响可以覆盖任务、摘要、决策和外部动作。
20. 纠正传播失败可重试且不重复副作用。
21. 秘密不会进入记忆、向量或全文索引。
22. PII 删除会同步清除令牌映射和派生内容。
23. 员工离职后私有记忆立即停止普通检索。
24. 再入职不会自动恢复旧私有记忆。
