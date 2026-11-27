-- Regent Agent ControlPlane schema version 5.
-- Adds the pgvector memory projection and ltree organization paths.
-- Execute with the migration role; production application startup never performs DDL.
BEGIN;

CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS ltree;

CREATE TABLE "MemoryEmbeddings" (
    "ClaimId" text NOT NULL,
    "TenantId" uuid NOT NULL,
    "ContentHash" text NOT NULL,
    "ModelVersion" text NOT NULL,
    "Vector" vector(256) NOT NULL,
    "IndexedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MemoryEmbeddings" PRIMARY KEY ("TenantId", "ClaimId")
);

COMMENT ON TABLE "MemoryEmbeddings" IS
    'Tenant-scoped pgvector projection for memory claims; claim content remains in the local JSON module.';
COMMENT ON COLUMN "MemoryEmbeddings"."ClaimId" IS
    'Memory claim identifier; unique together with TenantId.';
COMMENT ON COLUMN "MemoryEmbeddings"."TenantId" IS
    'Owning tenant and mandatory RLS boundary.';
COMMENT ON COLUMN "MemoryEmbeddings"."ContentHash" IS
    'Hash of the indexed claim content used to detect stale embeddings.';
COMMENT ON COLUMN "MemoryEmbeddings"."ModelVersion" IS
    'Embedding model version that produced the vector.';
COMMENT ON COLUMN "MemoryEmbeddings"."Vector" IS
    'Finite 256-dimensional embedding queried with cosine distance.';
COMMENT ON COLUMN "MemoryEmbeddings"."IndexedAt" IS
    'UTC time at which this embedding projection was last written.';

CREATE INDEX "IX_MemoryEmbeddings_Vector" ON "MemoryEmbeddings"
    USING HNSW ("Vector" vector_cosine_ops);
COMMENT ON INDEX "IX_MemoryEmbeddings_Vector" IS
    'HNSW cosine index used by permission-prefiltered memory recall.';

ALTER TABLE "MemoryEmbeddings" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "MemoryEmbeddings" FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "MemoryEmbeddings_tenant_isolation" ON "MemoryEmbeddings";
CREATE POLICY "MemoryEmbeddings_tenant_isolation" ON "MemoryEmbeddings"
    USING ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid);
REVOKE ALL ON "MemoryEmbeddings" FROM PUBLIC;

ALTER TABLE "Positions" ADD COLUMN IF NOT EXISTS "Path" ltree;

DO $backfill$
DECLARE
    tenant_id uuid;
BEGIN
    FOR tenant_id IN SELECT "Id" FROM "Tenants" LOOP
        PERFORM set_config('app.tenant_id', tenant_id::text, true);
        UPDATE "Positions" SET "Path" = NULL WHERE "TenantId" = tenant_id;
        WITH RECURSIVE hierarchy("Id", "Path", "Depth") AS (
            SELECT position."Id",
                   'root.' || replace(position."Id"::text, '-', ''),
                   1
            FROM "Positions" position
            WHERE position."TenantId" = tenant_id AND position."ParentPositionId" IS NULL
            UNION ALL
            SELECT child."Id",
                   hierarchy."Path" || '.' || replace(child."Id"::text, '-', ''),
                   hierarchy."Depth" + 1
            FROM "Positions" child
            JOIN hierarchy ON child."ParentPositionId" = hierarchy."Id"
            WHERE child."TenantId" = tenant_id AND hierarchy."Depth" < 4
        )
        UPDATE "Positions" position
        SET "Path" = hierarchy."Path"::ltree
        FROM hierarchy
        WHERE position."Id" = hierarchy."Id" AND position."TenantId" = tenant_id;

        IF (SELECT count(*) FROM "Positions" WHERE "TenantId" = tenant_id)
           <> (SELECT count(*) FROM "Positions" WHERE "TenantId" = tenant_id AND "Path" IS NOT NULL) THEN
            RAISE EXCEPTION 'Position hierarchy for tenant % contains a cycle, missing parent, cross-tenant parent, or depth over four', tenant_id;
        END IF;
    END LOOP;
END
$backfill$;

ALTER TABLE "Positions" ALTER COLUMN "Path" SET NOT NULL;
CREATE INDEX "IX_Positions_Path" ON "Positions" USING GiST ("Path");
COMMENT ON COLUMN "Positions"."Path" IS
    'Canonical ltree path root.<position-guid>[.<child-guid>] with at most four position levels.';
COMMENT ON INDEX "IX_Positions_Path" IS
    'GiST ltree index for tenant-scoped descendant and subtree queries.';

DO $grants$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'regent_controlplane') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON "MemoryEmbeddings" TO "regent_controlplane";
    END IF;
END
$grants$;

INSERT INTO "SchemaVersions" ("Version", "Name", "AppliedAt")
VALUES (5, 'controlplane-pgvector-ltree', now())
ON CONFLICT ("Version") DO NOTHING;
COMMIT;
