-- Regent Agent ControlPlane schema version 7.
-- Bind approvals to stable requesters and execution targets, and record one-time consumption.
-- Execute with the migration role; application startup never performs DDL.
BEGIN;

ALTER TABLE "Approvals" ADD COLUMN IF NOT EXISTS "RequesterUserId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
ALTER TABLE "Approvals" ADD COLUMN IF NOT EXISTS "TargetEmployeeId" uuid NULL;
ALTER TABLE "Approvals" ADD COLUMN IF NOT EXISTS "TargetCandidateId" uuid NULL;
ALTER TABLE "Approvals" ADD COLUMN IF NOT EXISTS "TargetPositionId" uuid NULL;
ALTER TABLE "Approvals" ADD COLUMN IF NOT EXISTS "ActionHash" text NOT NULL DEFAULT '';
ALTER TABLE "Approvals" ADD COLUMN IF NOT EXISTS "ConsumedAt" timestamp with time zone NULL;
ALTER TABLE "Approvals" ADD COLUMN IF NOT EXISTS "ConsumedBy" uuid NULL;

COMMENT ON COLUMN "Approvals"."RequesterUserId" IS
    '发起审批的稳定用户标识；自我批准检查使用该标识而非可变的显示名称。';
COMMENT ON COLUMN "Approvals"."TargetEmployeeId" IS
    '审批绑定的权威目标员工；终止等员工动作必须精确匹配该目标，空值不得用于员工终止。';
COMMENT ON COLUMN "Approvals"."TargetCandidateId" IS
    '审批绑定的权威候选人；录用动作必须精确匹配该目标。';
COMMENT ON COLUMN "Approvals"."TargetPositionId" IS
    '审批绑定的权威岗位；录用与再入职动作必须精确匹配该目标。';
COMMENT ON COLUMN "Approvals"."ActionHash" IS
    'v2 规范哈希，覆盖租户、规范化动作类型和三个稳定目标 ID；v1 哈希只读不可执行。';
COMMENT ON COLUMN "Approvals"."ConsumedAt" IS
    '审批被消费执行的 UTC 时间；通过条件更新原子写入，非空表示不得再次执行。';
COMMENT ON COLUMN "Approvals"."ConsumedBy" IS
    '消费审批的动作主体标识；用于审计关联，不替代目标员工或当前操作者鉴权。';

INSERT INTO "SchemaVersions" ("Version", "Name", "AppliedAt")
VALUES (7, 'approval-actor-target-consumption', now())
ON CONFLICT ("Version") DO NOTHING;

COMMIT;
