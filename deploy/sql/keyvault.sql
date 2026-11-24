-- Run with the migration role; never enable automatic migration in application startup.
CREATE SCHEMA IF NOT EXISTS keyvault;

CREATE TABLE IF NOT EXISTS keyvault.model_keys (
    key_ref text NOT NULL,
    tenant_id uuid NOT NULL,
    provider text NOT NULL,
    model text NOT NULL,
    version integer NOT NULL CHECK (version > 0),
    state text NOT NULL CHECK (state IN ('Active', 'Retired')),
    created_at timestamptz NOT NULL,
    encrypted_key text NOT NULL,
    key_nonce text NOT NULL,
    key_tag text NOT NULL,
    wrapped_data_key text NOT NULL,
    wrap_nonce text NOT NULL,
    wrap_tag text NOT NULL,
    PRIMARY KEY (tenant_id, key_ref)
);
CREATE INDEX IF NOT EXISTS model_keys_tenant_version_idx
    ON keyvault.model_keys (tenant_id, created_at, version);

ALTER TABLE keyvault.model_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE keyvault.model_keys FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS model_keys_tenant_isolation ON keyvault.model_keys;
CREATE POLICY model_keys_tenant_isolation ON keyvault.model_keys
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

REVOKE ALL ON keyvault.model_keys FROM PUBLIC;
-- Grant only the required DML privileges to the runtime role after it is created.
-- GRANT USAGE ON SCHEMA keyvault TO regent_gateway;
-- GRANT SELECT, INSERT, UPDATE, DELETE ON keyvault.model_keys TO regent_gateway;
