-- Regent Agent ControlPlane schema version 9.
-- Enforce tenant isolation for milestones with TenantId, forced RLS, and a composite project foreign key.
-- The derivation trigger keeps N-1 inserts that omit TenantId compatible without weakening NOT NULL or RLS.
-- Execute with the migration role; application startup never performs DDL.
BEGIN;

ALTER TABLE "Milestones" ADD COLUMN IF NOT EXISTS "TenantId" uuid;

-- The migration role owns the tables and has USAGE/CREATE on the target schema.
-- FORCE RLS still applies to its Projects reads, so the tenant-context-free
-- backfill and composite FK validation run after temporarily removing FORCE.
-- The final guard below restores FORCE or rolls back the transaction.
ALTER TABLE "Projects" NO FORCE ROW LEVEL SECURITY;

UPDATE "Milestones" AS milestone
SET "TenantId" = project."TenantId"
FROM "Projects" AS project
WHERE project."Id" = milestone."ProjectId"
  AND milestone."TenantId" IS NULL;

CREATE OR REPLACE FUNCTION "Milestones_derive_tenant_id"()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = pg_catalog
AS $milestone_derive_tenant_id$
BEGIN
    IF NEW."TenantId" IS NULL OR (
        TG_OP = 'UPDATE'
        AND NEW."ProjectId" IS DISTINCT FROM OLD."ProjectId"
        AND NEW."TenantId" IS NOT DISTINCT FROM OLD."TenantId"
    ) THEN
        EXECUTE format('SELECT "TenantId" FROM %I."Projects" WHERE "Id" = $1', TG_TABLE_SCHEMA)
        INTO NEW."TenantId"
        USING NEW."ProjectId";
    END IF;
    RETURN NEW;
END
$milestone_derive_tenant_id$;

DROP TRIGGER IF EXISTS "trg_Milestones_derive_tenant_id" ON "Milestones";
CREATE TRIGGER "trg_Milestones_derive_tenant_id"
BEFORE INSERT OR UPDATE ON "Milestones"
FOR EACH ROW EXECUTE FUNCTION "Milestones_derive_tenant_id"();

ALTER TABLE "Milestones" ALTER COLUMN "TenantId" SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_Projects_TenantId_Id" ON "Projects" ("TenantId", "Id");
CREATE INDEX IF NOT EXISTS "IX_Milestones_TenantId_ProjectId" ON "Milestones" ("TenantId", "ProjectId");
DROP INDEX IF EXISTS "IX_Milestones_ProjectId";

ALTER TABLE "Milestones" DROP CONSTRAINT IF EXISTS "FK_Milestones_Projects_ProjectId";
DO $milestone_foreign_key$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = '"Milestones"'::regclass
          AND conname = 'FK_Milestones_Projects_ProjectId'
    ) THEN
        ALTER TABLE "Milestones"
            ADD CONSTRAINT "FK_Milestones_Projects_ProjectId"
            FOREIGN KEY ("TenantId", "ProjectId") REFERENCES "Projects" ("TenantId", "Id") ON DELETE CASCADE;
    END IF;
END
$milestone_foreign_key$;

ALTER TABLE "Projects" FORCE ROW LEVEL SECURITY;

ALTER TABLE "Milestones" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Milestones" FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Milestones_tenant_isolation" ON "Milestones";
CREATE POLICY "Milestones_tenant_isolation" ON "Milestones"
    USING ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid);
REVOKE ALL ON "Milestones" FROM PUBLIC;

DO $tenant_rls_restore$
BEGIN
    IF (
        SELECT count(*) = 2
           AND bool_and(table_class.relrowsecurity AND table_class.relforcerowsecurity)
        FROM pg_class AS table_class
        JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
        WHERE table_namespace.nspname = current_schema()
          AND table_class.relname IN ('Projects', 'Milestones')
    ) IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Projects and Milestones row-level security must be enabled and forced after schema 9 migration';
    END IF;
END
$tenant_rls_restore$;

COMMENT ON TABLE "Milestones" IS
    '项目里程碑；TenantId、强制 RLS 与 Projects 复合外键共同约束租户范围，AcceptanceRule 定义可验证的完成门槛；兼容触发器仅在 TenantId 省略时从项目派生。';
COMMENT ON COLUMN "Milestones"."TenantId" IS
    '里程碑所属租户；从项目回填或由 BEFORE INSERT OR UPDATE 触发器在省略时派生，并与 ProjectId 组成复合外键和租户隔离策略。';
COMMENT ON COLUMN "Milestones"."ProjectId" IS
    '所属项目标识；必须与 TenantId 一起匹配 Projects，项目删除时级联移除里程碑。';
COMMENT ON INDEX "UQ_Projects_TenantId_Id" IS
    '保证项目在租户内唯一，并作为里程碑复合外键的父键。';
COMMENT ON INDEX "IX_Milestones_TenantId_ProjectId" IS
    '按租户和项目检索里程碑，同时支持租户隔离与项目级联删除。';
COMMENT ON CONSTRAINT "FK_Milestones_Projects_ProjectId" ON "Milestones" IS
    '里程碑必须属于同一租户的已存在项目，项目删除时级联移除里程碑。';

INSERT INTO "SchemaVersions" ("Version", "Name", "AppliedAt")
VALUES (9, 'milestone-tenant-isolation', now())
ON CONFLICT ("Version") DO NOTHING;

COMMIT;
