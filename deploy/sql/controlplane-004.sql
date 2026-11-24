-- Regent Agent ControlPlane schema version 4.
-- Adds minimal user-scoped membership reads for login and tenant listing.
BEGIN;
DROP POLICY IF EXISTS "Memberships_user_read" ON "Memberships";
CREATE POLICY "Memberships_user_read" ON "Memberships"
    FOR SELECT
    USING ("UserId" = nullif(current_setting('app.user_id', true), '')::uuid);

INSERT INTO "SchemaVersions" ("Version", "Name", "AppliedAt")
VALUES (4, 'controlplane-identity-rls-scope', now())
ON CONFLICT ("Version") DO NOTHING;
COMMIT;
