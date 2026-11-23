# ADR-0004：Gateway 密钥库存放于 Postgres keyvault schema

- 状态：Accepted
- 日期：2026-11-23
- 范围：BYOK 模型密钥存储

## 背景

模型密钥需要加密、轮换、审计和备份，同时不能让 ControlPlane、Runner 或运营人员解密。

## 决策

- Provider API Key 密文、包装数据密钥和版本元数据存放 Postgres `keyvault` schema。
- Gateway 使用独立数据库角色访问该 schema。
- 使用信封加密：数据密钥加密 API Key，主密钥包装数据密钥。
- 主密钥来自环境变量、宿主机 secret 文件或外部密钥系统，不进入数据库。
- `keyvault` 表启用 tenant_id 和 RLS。

## 结果

- 密钥生命周期与 Postgres 备份、迁移和审计体系统一。
- 数据库泄漏不等于密钥明文泄漏。
- 恢复必须同时具备密钥库备份和独立保管的主密钥。

## 未采用

- 独立本地密钥库文件：增加备份、漂移和恢复复杂度。
- 明文或可逆加密存业务表：违反秘密隔离边界。

## 关联

- [03 模型网关、计量与预算](../design/03-model-metering-budget.md)
- [04 安全、治理与恢复](../design/04-security-governance-recovery.md)
- [31 可选备份与恢复](../design/31-backup-profiles-and-recovery.md)
