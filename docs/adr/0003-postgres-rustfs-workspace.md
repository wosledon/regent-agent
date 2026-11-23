# ADR-0003：Postgres 为状态权威，RustFS 为对象权威

- 状态：Accepted
- 日期：2026-11-23
- 范围：结构化数据、对象存储和活动工作区

## 背景

系统需要多租户隔离、事务一致性、二进制对象存储和可恢复的任务工作区。

## 决策

- 生产结构化状态统一使用 Postgres，并启用强制 RLS。
- SQLite 仅用于单租户开发和测试。
- 二进制快照、成果、报告和归档统一使用 RustFS，通过 S3 SDK 访问。
- 活动 Git 工作区位于 Runner 本地或隔离容器目录，可丢失并恢复。
- 不保留 ArtifactStore、WorkspaceStore 多后端抽象。

## 结果

- 状态、对象和临时计算空间边界清晰。
- 跨模块使用 Outbox、Inbox 和 Saga，不使用 XA/2PC。
- 备份和恢复必须分别覆盖 Postgres、RustFS 和主密钥。

## 未采用

- SQLite 生产多租户：无法满足 RLS 和并发要求。
- 本地文件系统作为持久对象存储：无法满足共享和恢复要求。

## 关联

- [15 数据 Schema 与事务边界](../design/15-data-schema-and-transactions.md)
- [49 核心 ERD 与数据字典](../design/49-core-data-model-and-erd.md)
