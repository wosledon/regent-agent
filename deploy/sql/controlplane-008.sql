-- Regent Agent ControlPlane schema version 8.
-- Persist approval decider authority and bounded outbox retry visibility.
-- Execute with the migration role; application startup never performs DDL.
BEGIN;

ALTER TABLE "Approvals" ADD COLUMN IF NOT EXISTS "DecidedBy" uuid NULL;
ALTER TABLE "Approvals" ADD COLUMN IF NOT EXISTS "DecidedAt" timestamp with time zone NULL;
ALTER TABLE "Outbox" ADD COLUMN IF NOT EXISTS "NextAttemptAt" timestamp with time zone NULL;
ALTER TABLE "Outbox" ADD COLUMN IF NOT EXISTS "LastError" text NULL;
ALTER TABLE "SchemaVersions" ADD COLUMN IF NOT EXISTS "Checksum" text NULL;

COMMENT ON COLUMN "Approvals"."DecidedBy" IS
    '作出审批决定的稳定用户标识；执行层保留该身份以证明决定由当前授权主体作出。';
COMMENT ON COLUMN "Approvals"."DecidedAt" IS
    '审批决定时间；必须早于或等于审批消费时间，用于审计执行链路。';
COMMENT ON COLUMN "Outbox"."NextAttemptAt" IS
    'Outbox 下一次可处理时间；处理中作为租约到期时间，失败时按有界退避更新。';
COMMENT ON COLUMN "Outbox"."LastError" IS
    '最近一次处理失败的非敏感异常类型名称；达到尝试上限后消息进入隔离状态。';
COMMENT ON COLUMN "SchemaVersions"."Checksum" IS
    '迁移文件 SHA-256 校验和；版本 7 起跳过前必须非空且匹配，版本 3～6 的旧记录允许为空并视为 legacy。';

INSERT INTO "SchemaVersions" ("Version", "Name", "AppliedAt")
VALUES (8, 'approval-decider-outbox-retry', now())
ON CONFLICT ("Version") DO NOTHING;

COMMIT;
