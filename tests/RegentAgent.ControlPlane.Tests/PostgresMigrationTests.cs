using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Memory;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[CollectionDefinition("Postgres migrations", DisableParallelization = true)]
public sealed class PostgresMigrationCollection;

[Collection("Postgres migrations")]
public sealed class PostgresMigrationTests
{
    [PostgresMigrationFact]
    public async Task Migration_005_creates_vector_ltree_comments_rls_and_conditional_grants()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        await schema.ApplyMigrationAsync(3);
        await schema.ApplyMigrationAsync(4);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await schema.InsertTenantAsync(tenantA);
        await schema.InsertTenantAsync(tenantB);
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var grandchild = Guid.NewGuid();
        var leaf = Guid.NewGuid();
        var otherRoot = Guid.NewGuid();
        await schema.InsertPositionBefore005Async(tenantA, root, null);
        await schema.InsertPositionBefore005Async(tenantA, child, root);
        await schema.InsertPositionBefore005Async(tenantA, grandchild, child);
        await schema.InsertPositionBefore005Async(tenantA, leaf, grandchild);
        await schema.InsertPositionBefore005Async(tenantB, otherRoot, null);

        await schema.ApplyMigrationAsync(5);

        Assert.Equal(5L, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
        Assert.Equal(1L, await schema.ScalarAsync("SELECT count(*) FROM pg_extension WHERE extname = 'vector';"));
        Assert.Equal(1L, await schema.ScalarAsync("SELECT count(*) FROM pg_extension WHERE extname = 'ltree';"));
        Assert.Equal("vector(256)", await schema.ScalarAsync("""
            SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings' AND a.attname = 'Vector';
            """));
        Assert.Equal(true, await schema.ScalarAsync("""
            SELECT a.attnotnull FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings' AND a.attname = 'Vector';
            """));
        Assert.Contains("USING hnsw (\"Vector\" vector_cosine_ops)",
            (string)(await schema.ScalarAsync("SELECT pg_get_indexdef(format('%I.%I', current_schema(), 'IX_MemoryEmbeddings_Vector')::regclass);"))!,
            StringComparison.Ordinal);
        Assert.Equal("ltree", await schema.ScalarAsync("""
            SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'Positions' AND a.attname = 'Path';
            """));
        Assert.Equal(true, await schema.ScalarAsync("""
            SELECT a.attnotnull FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'Positions' AND a.attname = 'Path';
            """));
        Assert.Contains("USING gist (\"Path\")",
            (string)(await schema.ScalarAsync("SELECT pg_get_indexdef(format('%I.%I', current_schema(), 'IX_Positions_Path')::regclass);"))!,
            StringComparison.Ordinal);
        Assert.Equal(PositionPath.Root(root), await schema.ScalarAsync($"SELECT \"Path\"::text FROM \"Positions\" WHERE \"Id\" = '{root}';"));
        Assert.Equal($"{PositionPath.Root(root)}.{child:N}", await schema.ScalarAsync($"SELECT \"Path\"::text FROM \"Positions\" WHERE \"Id\" = '{child}';"));
        Assert.Equal($"{PositionPath.Root(root)}.{child:N}.{grandchild:N}", await schema.ScalarAsync($"SELECT \"Path\"::text FROM \"Positions\" WHERE \"Id\" = '{grandchild}';"));
        Assert.Equal($"{PositionPath.Root(root)}.{child:N}.{grandchild:N}.{leaf:N}", await schema.ScalarAsync($"SELECT \"Path\"::text FROM \"Positions\" WHERE \"Id\" = '{leaf}';"));
        Assert.Equal(PositionPath.Root(otherRoot), await schema.ScalarAsync($"SELECT \"Path\"::text FROM \"Positions\" WHERE \"Id\" = '{otherRoot}';"));

        await AssertCommentsAndSecurityAsync(schema);
        var runtimeRoleExists = Equals(true, await schema.ScalarAsync("SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'regent_controlplane');"));
        if (runtimeRoleExists)
        {
            Assert.Equal(4L, await schema.ScalarAsync("""
                SELECT count(DISTINCT privilege.privilege_type)
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                CROSS JOIN LATERAL aclexplode(c.relacl) privilege
                JOIN pg_roles role ON role.oid = privilege.grantee
                WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings' AND role.rolname = 'regent_controlplane';
                """));
        }
    }

    [PostgresMigrationFact]
    public async Task Migration_006_comments_catalog_is_idempotent_and_preserves_structure()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        await schema.ApplyMigrationAsync(3);
        await schema.ApplyMigrationAsync(4);
        var tenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        await schema.InsertTenantAsync(tenantId);
        await schema.InsertPositionBefore005Async(tenantId, positionId, null);
        await schema.ApplyMigrationAsync(5);
        Assert.Equal(PositionPath.Root(positionId),
            await schema.ScalarAsync($"SELECT \"Path\"::text FROM \"Positions\" WHERE \"Id\" = '{positionId}';"));
        var post005PositionId = Guid.NewGuid();
        await schema.InsertPositionAsync(tenantId, post005PositionId, null, PositionPath.Root(post005PositionId));
        Assert.Equal(PositionPath.Root(post005PositionId),
            await schema.ScalarAsync($"SELECT \"Path\"::text FROM \"Positions\" WHERE \"Id\" = '{post005PositionId}';"));

        await Assert.ThrowsAsync<PostgresException>(() => schema.VerifySchemaCommentsAsync());
        var structureBefore = await schema.CatalogSnapshotAsync();
        var rowsBefore = await schema.BusinessRowCountsAsync();

        await schema.ApplyMigrationAsync(6);

        Assert.Equal(6L, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
        Assert.Equal(1L, await schema.ScalarAsync("SELECT count(*) FROM \"SchemaVersions\" WHERE \"Version\" = 6;"));
        AssertCompleteCommentCoverage(await schema.VerifySchemaCommentsAsync());
        Assert.Equal(structureBefore, await schema.CatalogSnapshotAsync());
        Assert.Equal(rowsBefore, await schema.BusinessRowCountsAsync());

        await schema.ApplyMigrationAsync(6);

        Assert.Equal(6L, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
        Assert.Equal(1L, await schema.ScalarAsync("SELECT count(*) FROM \"SchemaVersions\" WHERE \"Version\" = 6;"));
        AssertCompleteCommentCoverage(await schema.VerifySchemaCommentsAsync());
        Assert.Equal(structureBefore, await schema.CatalogSnapshotAsync());
        Assert.Equal(rowsBefore, await schema.BusinessRowCountsAsync());
    }

    [PostgresMigrationFact]
    public async Task Migration_007_binds_approval_identity_target_and_consumption_idempotently()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        await schema.ApplyMigrationAsync(3);
        await schema.ApplyMigrationAsync(4);
        await schema.ApplyMigrationAsync(5);
        await schema.ApplyMigrationAsync(6);
        var tenantId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        await schema.InsertTenantAsync(tenantId);
        await schema.InsertProjectAsync(tenantId, projectId, "Before schema 7");
        await schema.InsertOutboxAsync(tenantId, outboxId, "project.created");
        await schema.InsertApprovalBefore007Async(tenantId, approvalId);
        var rowsBefore = await schema.BusinessRowCountsAsync();
        await schema.ApplyMigrationAsync(7);

        Assert.Equal(7L, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
        Assert.Equal(7L, await schema.ScalarAsync("""
            SELECT count(*) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'Approvals'
              AND a.attname IN ('RequesterUserId', 'TargetEmployeeId', 'TargetCandidateId', 'TargetPositionId', 'ActionHash', 'ConsumedAt', 'ConsumedBy');
            """));
        Assert.Equal("00000000-0000-0000-0000-000000000000", await schema.ScalarAsync(
            $"SELECT \"RequesterUserId\"::text FROM \"Approvals\" WHERE \"Id\" = '{approvalId}';"));
        Assert.Equal(true, await schema.ScalarAsync(
            $"SELECT \"TargetEmployeeId\" IS NULL AND \"TargetCandidateId\" IS NULL AND \"TargetPositionId\" IS NULL AND \"ActionHash\" = '' AND \"ConsumedAt\" IS NULL AND \"ConsumedBy\" IS NULL FROM \"Approvals\" WHERE \"Id\" = '{approvalId}';"));
        Assert.Equal("Before schema 7", await schema.ScalarAsync($"SELECT \"Name\" FROM \"Projects\" WHERE \"Id\" = '{projectId}';"));
        Assert.Equal("project.created", await schema.ScalarAsync($"SELECT \"EventType\" FROM \"Outbox\" WHERE \"Id\" = '{outboxId}';"));
        AssertCompleteCommentCoverage(await schema.VerifySchemaCommentsAsync(), 238);
        var structure = await schema.CatalogSnapshotAsync();
        var rowsAfter = await schema.BusinessRowCountsAsync();
        Assert.Equal(rowsBefore, rowsAfter);
        await schema.ApplyMigrationAsync(7);
        Assert.Equal(structure, await schema.CatalogSnapshotAsync());
        Assert.Equal(rowsAfter, await schema.BusinessRowCountsAsync());
    }

    [PostgresMigrationFact]
    public async Task Migration_008_adds_decider_and_outbox_retry_fields_idempotently()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        await schema.ApplyMigrationAsync(3);
        await schema.ApplyMigrationAsync(4);
        await schema.ApplyMigrationAsync(5);
        await schema.ApplyMigrationAsync(6);
        await schema.ApplyMigrationAsync(7);
        var tenantId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        await schema.InsertTenantAsync(tenantId);
        await schema.InsertProjectAsync(tenantId, projectId, "Before schema 8");
        await schema.InsertOutboxAsync(tenantId, outboxId, "project.updated");
        await schema.InsertApprovalBefore007Async(tenantId, approvalId);
        var rowsBefore = await schema.BusinessRowCountsAsync();
        await schema.ApplyMigrationAsync(8);

        Assert.Equal(8L, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
        Assert.Equal(4L, await schema.ScalarAsync("""
            SELECT count(*) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema()
              AND ((c.relname = 'Approvals' AND a.attname IN ('DecidedBy', 'DecidedAt'))
                OR (c.relname = 'Outbox' AND a.attname IN ('NextAttemptAt', 'LastError')));
            """));
        Assert.Equal(true, await schema.ScalarAsync(
            $"SELECT \"DecidedBy\" IS NULL AND \"DecidedAt\" IS NULL FROM \"Approvals\" WHERE \"Id\" = '{approvalId}';"));
        Assert.Equal(true, await schema.ScalarAsync(
            $"SELECT \"NextAttemptAt\" IS NULL AND \"LastError\" IS NULL FROM \"Outbox\" WHERE \"Id\" = '{outboxId}';"));
        Assert.Equal("Before schema 8", await schema.ScalarAsync($"SELECT \"Name\" FROM \"Projects\" WHERE \"Id\" = '{projectId}';"));
        Assert.Equal("project.updated", await schema.ScalarAsync($"SELECT \"EventType\" FROM \"Outbox\" WHERE \"Id\" = '{outboxId}';"));
        Assert.Equal(true, await schema.ScalarAsync(
            "SELECT bool_and(\"Checksum\" IS NOT NULL) FROM \"SchemaVersions\" WHERE \"Version\" >= 7;"));
        AssertCompleteCommentCoverage(await schema.VerifySchemaCommentsAsync(), 242);
        var structure = await schema.CatalogSnapshotAsync();
        var rowsAfter = await schema.BusinessRowCountsAsync();
        Assert.Equal(rowsBefore, rowsAfter);
        await schema.ApplyMigrationAsync(8);
        Assert.Equal(structure, await schema.CatalogSnapshotAsync());
        Assert.Equal(rowsAfter, await schema.BusinessRowCountsAsync());
    }

    [PostgresMigrationFact]
    public async Task Migration_009_enforces_tenant_scoped_milestones_and_composite_project_key()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 6)) await schema.ApplyMigrationAsync(version);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var milestoneA = Guid.NewGuid();
        var milestoneB = Guid.NewGuid();
        await schema.InsertTenantAsync(tenantA);
        await schema.InsertTenantAsync(tenantB);
        await schema.InsertProjectAsync(tenantA, projectA, "Tenant A project");
        await schema.InsertProjectAsync(tenantB, projectB, "Tenant B project");
        await schema.InsertMilestoneBefore009Async(projectA, milestoneA);
        await schema.InsertMilestoneBefore009Async(projectB, milestoneB);
        var rowsBefore = await schema.BusinessRowCountsAsync();

        await schema.ApplyMigrationAsync(9);

        Assert.Equal(9L, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
        Assert.Equal(tenantA, await schema.ScalarAsync($"SELECT \"TenantId\" FROM \"Milestones\" WHERE \"Id\" = '{milestoneA}';"));
        Assert.Equal(tenantB, await schema.ScalarAsync($"SELECT \"TenantId\" FROM \"Milestones\" WHERE \"Id\" = '{milestoneB}';"));
        Assert.Equal(true, await schema.ScalarAsync("""
            SELECT c.relrowsecurity AND c.relforcerowsecurity FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'Milestones';
            """));
        var foreignKey = (string)(await schema.ScalarAsync("""
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
            WHERE conrelid = '"Milestones"'::regclass AND conname = 'FK_Milestones_Projects_ProjectId';
            """))!;
        Assert.Contains("FOREIGN KEY (\"TenantId\", \"ProjectId\")", foreignKey, StringComparison.Ordinal);
        Assert.Contains("REFERENCES \"Projects\"(\"TenantId\", \"Id\")", foreignKey, StringComparison.Ordinal);
        AssertCompleteCommentCoverage(await schema.VerifySchemaCommentsAsync(), 243, 56);
        var structure = await schema.CatalogSnapshotAsync();
        var rowsAfter = await schema.BusinessRowCountsAsync();
        Assert.Equal(rowsBefore, rowsAfter);

        await schema.ApplyMigrationAsync(9);

        Assert.Equal(structure, await schema.CatalogSnapshotAsync());
        Assert.Equal(rowsAfter, await schema.BusinessRowCountsAsync());
    }

    [PostgresMigrationFact]
    public async Task Schema_validator_accepts_nine_and_rejects_missing_milestone_trigger()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 7)) await schema.ApplyMigrationAsync(version);
        await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
            .UseNpgsql(schema.ConnectionString).Options);

        await PostgresSchemaValidator.ValidateAsync(db, CancellationToken.None);
        await schema.ExecuteAsync("DROP TRIGGER \"trg_Milestones_derive_tenant_id\" ON \"Milestones\";");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresSchemaValidator.ValidateAsync(db, CancellationToken.None));
        Assert.Contains("tenant derivation trigger", error.Message, StringComparison.Ordinal);
    }

    [PostgresMigrationFact]
    public Task Migration_009_backfills_existing_milestones_as_non_superuser_table_owner() =>
        AssertMigration009BackfillsAsNonSuperuserTableOwnerAsync(1);

    [PostgresMigrationFact]
    public Task Migration_009_backfills_multiple_tenants_and_validates_foreign_key_as_non_superuser_table_owner() =>
        AssertMigration009BackfillsAsNonSuperuserTableOwnerAsync(2);

    private static async Task AssertMigration009BackfillsAsNonSuperuserTableOwnerAsync(int tenantCount)
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 6)) await schema.ApplyMigrationAsync(version);
        var tenants = Enumerable.Range(0, tenantCount)
            .Select(_ => (TenantId: Guid.NewGuid(), ProjectId: Guid.NewGuid(), MilestoneId: Guid.NewGuid()))
            .ToArray();
        foreach (var tenant in tenants)
        {
            await schema.InsertTenantAsync(tenant.TenantId);
            await schema.InsertProjectAsync(tenant.TenantId, tenant.ProjectId, "Table owner project");
            await schema.InsertMilestoneBefore009Async(tenant.ProjectId, tenant.MilestoneId);
        }

        await schema.MakeRuntimeRoleTableOwnerAsync();
        await schema.ApplyMigrationAsRuntimeRoleAsync(9, tenants[^1].TenantId);
        await schema.ApplyMigrationAsRuntimeRoleAsync(9, tenants[0].TenantId);

        await using var connection = new NpgsqlConnection(schema.RuntimeConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT NOT COALESCE((
                       SELECT bool_or(rolsuper)
                       FROM pg_roles
                       WHERE rolname = current_user OR pg_has_role(current_user, oid, 'MEMBER')
                   ), false)
               AND NOT COALESCE((
                       SELECT bool_or(rolbypassrls)
                       FROM pg_roles
                       WHERE rolname = current_user OR pg_has_role(current_user, oid, 'MEMBER')
                   ), false)
               AND NOT EXISTS (
                   SELECT 1
                   FROM pg_class AS table_class
                   JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
                   WHERE table_namespace.nspname = current_schema()
                     AND table_class.relkind IN ('r', 'p')
                     AND pg_get_userbyid(table_class.relowner) <> current_user
               )
            FROM pg_roles WHERE rolname = current_user;
            """;
        Assert.Equal(true, await command.ExecuteScalarAsync());
        command.CommandText = """
            SELECT pg_get_userbyid(schema_namespace.nspowner) <> current_user
               AND has_schema_privilege(current_user, current_schema(), 'USAGE')
               AND has_schema_privilege(current_user, current_schema(), 'CREATE')
            FROM pg_namespace AS schema_namespace
            WHERE schema_namespace.nspname = current_schema();
            """;
        Assert.Equal(true, await command.ExecuteScalarAsync());

        foreach (var tenant in tenants)
        {
            command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id::text, false);";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("tenant_id", tenant.TenantId);
            await command.ExecuteNonQueryAsync();
            command.CommandText = "SELECT count(*) FROM \"Milestones\";";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
            command.CommandText = "SELECT \"TenantId\" FROM \"Milestones\" WHERE \"Id\" = @milestone_id;";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("milestone_id", tenant.MilestoneId);
            Assert.Equal(tenant.TenantId, await command.ExecuteScalarAsync());
        }

        command.CommandText = """
            SELECT convalidated
            FROM pg_constraint
            WHERE conrelid = '"Milestones"'::regclass
              AND conname = 'FK_Milestones_Projects_ProjectId';
            """;
        Assert.Equal(true, await command.ExecuteScalarAsync());
        command.CommandText = """
            SELECT count(*) = 2
               AND bool_and(table_class.relrowsecurity AND table_class.relforcerowsecurity)
            FROM pg_class AS table_class
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = current_schema()
              AND table_class.relname IN ('Projects', 'Milestones');
            """;
        Assert.Equal(true, await command.ExecuteScalarAsync());
    }

    [PostgresMigrationFact]
    public async Task Migration_009_derives_tenant_for_n_minus_one_inserts_and_preserves_rls()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 6)) await schema.ApplyMigrationAsync(version);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var milestoneId = Guid.NewGuid();
        await schema.InsertTenantAsync(tenantA);
        await schema.InsertTenantAsync(tenantB);
        await schema.InsertProjectAsync(tenantA, projectA, "Tenant A project");
        await schema.InsertProjectAsync(tenantB, projectB, "Tenant B project");

        await schema.ApplyMigrationAsync(9);
        await schema.ApplyRuntimeGrantsAsync();
        await PostgresTestSupport.AssertRuntimeRoleAsync(schema.RuntimeConnectionString);

        await using (var connection = new NpgsqlConnection(schema.RuntimeConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id::text, false);";
            command.Parameters.AddWithValue("tenant_id", tenantA);
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                INSERT INTO "Milestones" ("Id", "ProjectId", "Name", "TargetAt", "Status", "AcceptanceRule")
                VALUES (@id, @project_id, 'N-1 milestone', now(), 'Pending', 'Synthetic acceptance');
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("id", milestoneId);
            command.Parameters.AddWithValue("project_id", projectA);
            await command.ExecuteNonQueryAsync();
            command.CommandText = "SELECT \"TenantId\" FROM \"Milestones\" WHERE \"Id\" = @id;";
            Assert.Equal(tenantA, await command.ExecuteScalarAsync());

            command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id::text, false);";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("tenant_id", tenantB);
            await command.ExecuteNonQueryAsync();
            command.CommandText = "SELECT count(*) FROM \"Milestones\" WHERE \"Id\" = @id;";
            command.Parameters.AddWithValue("id", milestoneId);
            Assert.Equal(0L, await command.ExecuteScalarAsync());

            command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id::text, false);";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("tenant_id", tenantA);
            await command.ExecuteNonQueryAsync();
            command.CommandText = "SELECT count(*) FROM \"Milestones\" WHERE \"Id\" = @id;";
            command.Parameters.AddWithValue("id", milestoneId);
            Assert.Equal(1L, await command.ExecuteScalarAsync());

            command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id::text, false);";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("tenant_id", tenantB);
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                INSERT INTO "Milestones" ("Id", "TenantId", "ProjectId", "Name", "TargetAt", "Status", "AcceptanceRule")
                VALUES (@id, @tenant_id, @project_id, 'Cross-tenant milestone', now(), 'Pending', 'Synthetic acceptance');
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("tenant_id", tenantB);
            command.Parameters.AddWithValue("project_id", projectA);
            var foreignKeyError = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("23503", foreignKeyError.SqlState);
        }

        Assert.Equal(true, await schema.ScalarAsync("""
            SELECT a.attnotnull FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'Milestones' AND a.attname = 'TenantId';
            """));
        var triggerDefinition = (string)(await schema.ScalarAsync("""
            SELECT pg_get_triggerdef(oid) FROM pg_trigger
            WHERE tgrelid = '"Milestones"'::regclass AND tgname = 'trg_Milestones_derive_tenant_id';
            """))!;
        Assert.Contains("BEFORE INSERT OR UPDATE", triggerDefinition, StringComparison.OrdinalIgnoreCase);

        await schema.ApplyMigrationAsync(9);

        Assert.Equal(1L, await schema.ScalarAsync("""
            SELECT count(*) FROM pg_trigger
            WHERE tgrelid = '"Milestones"'::regclass AND tgname = 'trg_Milestones_derive_tenant_id' AND NOT tgisinternal;
            """));
        await PostgresTestSupport.AssertRuntimeRoleAsync(schema.RuntimeConnectionString);
    }

    [PostgresMigrationFact]
    public async Task PostgreSQL_deparsed_canonical_policies_and_required_membership_policy_validate()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 7)) await schema.ApplyMigrationAsync(version);
        var serverVersion = Convert.ToInt32(await schema.ScalarAsync("SHOW server_version_num;"));
        Assert.Contains(serverVersion / 10000, new[] { 17, 18 });
        var tenantTables = await schema.ScalarAsync("""
            SELECT count(*)
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema()
              AND c.relkind IN ('r', 'p')
              AND (
                  c.relname = 'MemoryEmbeddings'
                  OR EXISTS (
                      SELECT 1 FROM pg_attribute a
                      WHERE a.attrelid = c.oid AND a.attname = 'TenantId'
                        AND a.attnum > 0 AND NOT a.attisdropped
                  )
              );
            """);
        var canonicalPolicies = await schema.ScalarAsync($"""
            SELECT count(*)
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema()
              AND c.relkind IN ('r', 'p')
              AND (
                  c.relname = 'MemoryEmbeddings'
                  OR EXISTS (
                      SELECT 1 FROM pg_attribute a
                      WHERE a.attrelid = c.oid AND a.attname = 'TenantId'
                        AND a.attnum > 0 AND NOT a.attisdropped
                  )
              )
              AND EXISTS (
                  SELECT 1 FROM pg_policy p
                  WHERE p.polrelid = c.oid
                    AND {PostgresRlsPolicyCatalog.CanonicalTenantPolicy("p", "c")}
              );
            """);
        var membershipPolicies = await schema.ScalarAsync($"""
            SELECT count(*)
            FROM pg_policy p
            JOIN pg_class c ON c.oid = p.polrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema()
              AND {PostgresRlsPolicyCatalog.MembershipUserReadPolicy("p", "c")};
            """);

        Assert.Equal(tenantTables, canonicalPolicies);
        Assert.Equal(1L, membershipPolicies);
    }

    [PostgresMigrationFact]
    public async Task Schema_comment_verifier_rejects_zero_object_category()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);

        var error = await Assert.ThrowsAsync<PostgresException>(() => schema.VerifySchemaCommentsAsync());

        Assert.Contains("zero-object denominator for columns", error.Message, StringComparison.Ordinal);
        Assert.Contains(schema.Name, error.Message, StringComparison.Ordinal);
    }

    [PostgresMigrationFact]
    public async Task Schema_comment_verifier_counts_pfuc_constraints_and_excludes_pg18_not_null_rows()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 7)) await schema.ApplyMigrationAsync(version);

        var coverage = await schema.VerifySchemaCommentsAsync();
        var constraints = coverage.Single(item => item.ObjectType == "constraints");
        Assert.Equal(29L, constraints.Total);
        Assert.Equal(29L, constraints.Commented);
        Assert.Equal(0L, constraints.Missing);

        var serverVersion = Convert.ToInt32(await schema.ScalarAsync("SHOW server_version_num;"));
        var notNullRows = Convert.ToInt64(await schema.ScalarAsync("""
            SELECT count(*) FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND con.contype = 'n';
            """));
        if (serverVersion >= 180000)
        {
            Assert.True(notNullRows > 0, "PostgreSQL 18 must expose table NOT NULL catalog rows.");
            var eligibleRows = Convert.ToInt64(await schema.ScalarAsync("""
                SELECT count(*) FROM pg_constraint con
                JOIN pg_class c ON c.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND con.contype IN ('p', 'f', 'u', 'c');
                """));
            var allRows = Convert.ToInt64(await schema.ScalarAsync("""
                SELECT count(*) FROM pg_constraint con
                JOIN pg_class c ON c.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema();
                """));
            Assert.Equal(29L, eligibleRows);
            Assert.Equal(eligibleRows + notNullRows, allRows);
        }
        else
        {
            Assert.Equal(0L, notNullRows);
        }
    }

    [PostgresMigrationFact]
    public async Task Schema_comment_verifier_counts_unique_and_check_constraints_and_rejects_missing_comment()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 7)) await schema.ApplyMigrationAsync(version);
        await schema.ExecuteAsync("""
            ALTER TABLE "Tenants"
                ADD CONSTRAINT "UQ_Tenants_Comment_Verifier" UNIQUE ("Id"),
                ADD CONSTRAINT "CK_Tenants_Comment_Verifier" CHECK (length("Name") > 0);
            COMMENT ON CONSTRAINT "UQ_Tenants_Comment_Verifier" ON "Tenants" IS '租户标识唯一约束注释验证。';
            COMMENT ON CONSTRAINT "CK_Tenants_Comment_Verifier" ON "Tenants" IS '租户名称非空检查约束注释验证。';
            COMMENT ON INDEX "UQ_Tenants_Comment_Verifier" IS '租户标识唯一约束索引注释验证。';
            """);

        var coverage = await schema.VerifySchemaCommentsAsync();
        var constraints = coverage.Single(item => item.ObjectType == "constraints");
        Assert.Equal(31L, constraints.Total);
        Assert.Equal(31L, constraints.Commented);
        var uniqueAndCheckRows = Convert.ToInt64(await schema.ScalarAsync("""
            SELECT count(*) FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema()
              AND con.conname IN ('UQ_Tenants_Comment_Verifier', 'CK_Tenants_Comment_Verifier')
              AND con.contype IN ('u', 'c');
            """));
        Assert.Equal(2L, uniqueAndCheckRows);

        await schema.ExecuteAsync("COMMENT ON CONSTRAINT \"UQ_Tenants_Comment_Verifier\" ON \"Tenants\" IS NULL;");

        var error = await Assert.ThrowsAsync<PostgresException>(() => schema.VerifySchemaCommentsAsync());
        Assert.Contains("constraints", error.Message, StringComparison.Ordinal);
        Assert.Contains("30/31", error.Message, StringComparison.Ordinal);
        Assert.Contains("UQ_Tenants_Comment_Verifier", error.Message, StringComparison.Ordinal);
    }

    [PostgresMigrationTheory]
    [InlineData("table")]
    [InlineData("column")]
    [InlineData("index")]
    [InlineData("constraint")]
    [InlineData("extension")]
    public async Task Comment_verification_rejects_invalid_text_and_006_restores_it(string objectType)
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        await schema.ApplyMigrationAsync(3);
        await schema.ApplyMigrationAsync(4);
        await schema.ApplyMigrationAsync(5);
        await schema.ApplyMigrationAsync(6);
        AssertCompleteCommentCoverage(await schema.VerifySchemaCommentsAsync());
        var structureBefore = await schema.CatalogSnapshotAsync();
        var coverageType = objectType switch
        {
            "table" => "tables",
            "column" => "columns",
            "index" => "indexes",
            "constraint" => "constraints",
            "extension" => "extensions",
            _ => throw new ArgumentOutOfRangeException(nameof(objectType))
        };
        var invalidComments = new string?[] { null, "   ", "English only." };

        foreach (var comment in invalidComments)
        {
            await schema.SetSchemaCommentAsync(objectType, comment);
            try
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => schema.VerifySchemaCommentsAsync());
                Assert.Contains(coverageType, error.Message, StringComparison.Ordinal);
            }
            finally
            {
                await schema.ApplyMigrationAsync(6);
            }

            AssertCompleteCommentCoverage(await schema.VerifySchemaCommentsAsync());
            Assert.Equal(1L, await schema.ScalarAsync("SELECT count(*) FROM \"SchemaVersions\" WHERE \"Version\" = 6;"));
            Assert.Equal(structureBefore, await schema.CatalogSnapshotAsync());
        }
    }

    [PostgresMigrationTheory]
    [InlineData("cycle")]
    [InlineData("missing-parent")]
    [InlineData("cross-tenant-parent")]
    [InlineData("over-depth")]
    public async Task Invalid_hierarchy_aborts_all_of_005_and_leaves_schema_four(string scenario)
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        var (tenantA, tenantB) = await CreateSchemaFourWithTenantsAsync(schema);
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        switch (scenario)
        {
            case "cycle":
                await schema.InsertPositionBefore005Async(tenantA, ids[0], ids[1]);
                await schema.InsertPositionBefore005Async(tenantA, ids[1], ids[0]);
                break;
            case "missing-parent":
                await schema.InsertPositionBefore005Async(tenantA, ids[0], ids[1]);
                break;
            case "cross-tenant-parent":
                await schema.InsertPositionBefore005Async(tenantB, ids[0], null);
                await schema.InsertPositionBefore005Async(tenantA, ids[1], ids[0]);
                break;
            case "over-depth":
                await schema.InsertPositionBefore005Async(tenantA, ids[0], null);
                for (var i = 1; i < ids.Length; i++) await schema.InsertPositionBefore005Async(tenantA, ids[i], ids[i - 1]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var error = await Record.ExceptionAsync(() => schema.ApplyMigrationAsync(5));

        Assert.IsType<PostgresException>(error);
        Assert.Null(await schema.ScalarAsync("SELECT to_regclass(format('%I.%I', current_schema(), 'MemoryEmbeddings'))::text;"));
        Assert.Equal(0L, await schema.ScalarAsync("""
            SELECT count(*) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'Positions' AND a.attname = 'Path';
            """));
        Assert.Equal(4L, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
    }

    [PostgresMigrationFact]
    public async Task Schema_version_service_never_bootstraps_npgsql_even_when_requested()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        await schema.ApplyMigrationAsync(3);
        await schema.ApplyMigrationAsync(4);
        await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
            .UseNpgsql(schema.ConnectionString).Options);
        var service = new SchemaVersionService(db, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateAsync(CancellationToken.None, true));

        Assert.Equal(4L, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
    }

    [PostgresMigrationTheory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task Development_postgres_rejects_old_schema_without_bootstrap_or_seeding(int version)
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        if (version >= 4) await schema.ApplyMigrationAsync(3);
        if (version >= 4) await schema.ApplyMigrationAsync(4);
        if (version >= 5) await schema.ApplyMigrationAsync(5);
        if (version >= 6) await schema.ApplyMigrationAsync(6);
        if (version >= 7) await schema.ApplyMigrationAsync(7);
        if (version >= 8) await schema.ApplyMigrationAsync(8);
        await using var factory = new ControlPlaneRuntimeFactory("Development", "Postgres", false, schema.ConnectionString);

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        if (version == 0)
            Assert.Null(await schema.ScalarAsync("SELECT to_regclass(format('%I.%I', current_schema(), 'SchemaVersions'))::text;"));
        else
        {
            Assert.Equal((long)version, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
            var memoryTable = await schema.ScalarAsync("SELECT to_regclass(format('%I.%I', current_schema(), 'MemoryEmbeddings'))::text;");
            if (version == 4) Assert.Null(memoryTable);
            else Assert.NotNull(memoryTable);
        }
        Assert.False(File.Exists(Path.Combine(factory.Root, "data", "modules", "memory-vectors.json")));
    }

    [PostgresMigrationTheory]
    [InlineData("vector-index")]
    [InlineData("path-not-null")]
    [InlineData("outbox-retry")]
    [InlineData("approval-requester-type")]
    [InlineData("approval-action-default")]
    [InlineData("approval-target-not-null")]
    [InlineData("outbox-retry-type")]
    [InlineData("outbox-error-default")]
    [InlineData("checksum-missing")]
    [InlineData("checksum-type")]
    [InlineData("checksum-not-null")]
    [InlineData("checksum-default")]
    [InlineData("checksum-history-null")]
    [InlineData("checksum-history-tampered")]
    [InlineData("approvals-policy")]
    public async Task Incomplete_schema_nine_fails_startup_without_writes(string defect)
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        await schema.ApplyMigrationAsync(3);
        await schema.ApplyMigrationAsync(4);
        await schema.ApplyMigrationAsync(5);
        await schema.ApplyMigrationAsync(6);
        await schema.ApplyMigrationAsync(7);
        await schema.ApplyMigrationAsync(8);
        await schema.ApplyMigrationAsync(9);
        await schema.ApplyRuntimeGrantsAsync();
        var defectSql = defect switch
        {
            "vector-index" => "DROP INDEX \"IX_MemoryEmbeddings_Vector\";",
            "path-not-null" => "ALTER TABLE \"Positions\" ALTER COLUMN \"Path\" DROP NOT NULL;",
            "outbox-retry" => "ALTER TABLE \"Outbox\" DROP COLUMN \"NextAttemptAt\";",
            "approval-requester-type" => "ALTER TABLE \"Approvals\" ALTER COLUMN \"RequesterUserId\" TYPE text USING \"RequesterUserId\"::text;",
            "approval-action-default" => "ALTER TABLE \"Approvals\" ALTER COLUMN \"ActionHash\" SET DEFAULT 'drift';",
            "approval-target-not-null" => "ALTER TABLE \"Approvals\" ALTER COLUMN \"TargetEmployeeId\" SET NOT NULL;",
            "outbox-retry-type" => "ALTER TABLE \"Outbox\" ALTER COLUMN \"NextAttemptAt\" TYPE text USING \"NextAttemptAt\"::text;",
            "outbox-error-default" => "ALTER TABLE \"Outbox\" ALTER COLUMN \"LastError\" SET DEFAULT 'drift';",
            "checksum-missing" => "ALTER TABLE \"SchemaVersions\" DROP COLUMN \"Checksum\";",
            "checksum-type" => "ALTER TABLE \"SchemaVersions\" ALTER COLUMN \"Checksum\" TYPE integer USING NULL::integer;",
            "checksum-not-null" => "ALTER TABLE \"SchemaVersions\" ALTER COLUMN \"Checksum\" SET NOT NULL;",
            "checksum-default" => "ALTER TABLE \"SchemaVersions\" ALTER COLUMN \"Checksum\" SET DEFAULT 'drift';",
            "checksum-history-null" => "UPDATE \"SchemaVersions\" SET \"Checksum\" = NULL WHERE \"Version\" = 7;",
            "checksum-history-tampered" => "UPDATE \"SchemaVersions\" SET \"Checksum\" = '0000000000000000000000000000000000000000000000000000000000000000' WHERE \"Version\" = 3;",
            "approvals-policy" => "DROP POLICY \"Approvals_tenant_isolation\" ON \"Approvals\";",
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        await schema.ExecuteAsync(defectSql);
        await using var factory = new ControlPlaneRuntimeFactory("Development", "Postgres", false, schema.RuntimeConnectionString);
        var expectedMessage = defect switch
        {
            "vector-index" => "MemoryEmbeddings HNSW vector_cosine_ops index is missing.",
            "path-not-null" => "Positions.Path must be ltree NOT NULL.",
            "outbox-retry" or "outbox-retry-type" or "outbox-error-default" =>
                "Outbox retry columns must have the exact schema 8 types, nullability, and defaults.",
            "approval-requester-type" or "approval-action-default" or "approval-target-not-null" =>
                "Approvals authority columns must have the exact schema 8 types, nullability, and defaults.",
            "checksum-missing" or "checksum-type" or "checksum-not-null" or "checksum-default" =>
                "SchemaVersions.Checksum must be text NULL with no default.",
            "checksum-history-null" or "checksum-history-tampered" =>
                "SchemaVersions must contain the exact embedded-manifest SHA-256 checksums for migrations 003 through 009.",
            "approvals-policy" => "Approvals tenant isolation policy is missing or incomplete.",
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains(expectedMessage, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(9L, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
        Assert.Equal(0L, await schema.ScalarAsync("SELECT count(*) FROM \"Tenants\";"));
        Assert.Equal(0L, await schema.ScalarAsync("SELECT count(*) FROM \"MemoryEmbeddings\";"));
        Assert.False(File.Exists(Path.Combine(factory.Root, "data", "modules", "memory-vectors.json")));
    }

    [PostgresMigrationFact]
    public async Task Postgres_startup_rejects_migration_role_separately_from_schema_validation()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 7)) await schema.ApplyMigrationAsync(version);
        await using var factory = new ControlPlaneRuntimeFactory("Development", "Postgres", false, schema.ConnectionString);

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("Postgres RLS validation failed", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0L, await schema.ScalarAsync("SELECT count(*) FROM \"Tenants\";"));
    }

    [PostgresMigrationTheory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Valid_schema_nine_starts_without_seeding_or_ddl(string environment)
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        await schema.ApplyMigrationAsync(3);
        await schema.ApplyMigrationAsync(4);
        await schema.ApplyMigrationAsync(5);
        await schema.ApplyMigrationAsync(6);
        await schema.ApplyMigrationAsync(7);
        await schema.ApplyMigrationAsync(8);
        await schema.ApplyMigrationAsync(9);
        await schema.ApplyRuntimeGrantsAsync();
        await PostgresTestSupport.AssertRuntimeRoleAsync(schema.RuntimeConnectionString);
        await PostgresTestSupport.AssertRuntimePrivilegesAsync(schema.RuntimeConnectionString);
        Assert.Equal(9L, await schema.ScalarAsync("SELECT MAX(\"Version\") FROM \"SchemaVersions\";"));
        var relationsBefore = await schema.ScalarAsync("SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = current_schema();");
        await using var factory = new ControlPlaneRuntimeFactory(environment, "Postgres", false, schema.RuntimeConnectionString);

        using var client = factory.CreateClient();

        Assert.IsType<PostgresMemoryVectorStore>(factory.Services.GetRequiredService<IMemoryVectorStore>());
        Assert.Equal(relationsBefore, await schema.ScalarAsync("SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = current_schema();"));
        Assert.Equal(0L, await schema.ScalarAsync("SELECT count(*) FROM \"Tenants\";"));
        Assert.Equal(0L, await schema.ScalarAsync("SELECT count(*) FROM \"Users\";"));
        Assert.Equal(0L, await schema.ScalarAsync("SELECT count(*) FROM \"Positions\";"));
        Assert.Equal(0L, await schema.ScalarAsync("SELECT count(*) FROM \"MemoryEmbeddings\";"));
        Assert.False(File.Exists(Path.Combine(factory.Root, "data", "modules", "memory-vectors.json")));
    }

    [PostgresMigrationFact]
    public async Task Runtime_grants_enforce_append_only_audit_and_read_only_schema_history()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 7)) await schema.ApplyMigrationAsync(version);
        await schema.ApplyRuntimeGrantsAsync();
        await PostgresTestSupport.AssertRuntimePrivilegesAsync(schema.RuntimeConnectionString);

        var forbidden = new[]
        {
            "SET ROLE regent_controlplane; UPDATE \"AuditRecords\" SET \"Hash\" = 'tampered';",
            "SET ROLE regent_controlplane; DELETE FROM \"AuditRecords\";",
            "SET ROLE regent_controlplane; UPDATE \"AuditCheckpoints\" SET \"MerkleRoot\" = 'tampered';",
            "SET ROLE regent_controlplane; DELETE FROM \"AuditCheckpoints\";",
            "SET ROLE regent_controlplane; INSERT INTO \"SchemaVersions\" (\"Version\", \"Name\", \"AppliedAt\") VALUES (99, 'forbidden', now());",
            "SET ROLE regent_controlplane; UPDATE \"SchemaVersions\" SET \"Name\" = 'forbidden';",
            "SET ROLE regent_controlplane; DELETE FROM \"SchemaVersions\";"
        };
        foreach (var sql in forbidden)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => schema.ExecuteAsync(sql));
            Assert.Equal("42501", error.SqlState);
        }
    }

    [PostgresMigrationFact]
    public async Task Runtime_grants_remove_pre_existing_schema_and_column_privileges()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 7)) await schema.ApplyMigrationAsync(version);
        await schema.ExecuteAsync($"GRANT CREATE ON SCHEMA \"{schema.Name}\" TO regent_controlplane, PUBLIC;");
        await schema.ExecuteAsync("GRANT UPDATE (\"Hash\") ON \"AuditRecords\" TO regent_controlplane;");
        await schema.ExecuteAsync("GRANT INSERT (\"Version\") ON \"SchemaVersions\" TO regent_controlplane;");

        await schema.ApplyRuntimeGrantsAsync();

        await PostgresTestSupport.AssertRuntimePrivilegesAsync(schema.RuntimeConnectionString);
        await Assert.ThrowsAsync<PostgresException>(() => schema.ExecuteAsync(
            "SET ROLE regent_controlplane; CREATE TABLE \"ForbiddenByRuntime\" (\"Id\" integer);"));
        await Assert.ThrowsAsync<PostgresException>(() => schema.ExecuteAsync(
            "SET ROLE regent_controlplane; UPDATE \"AuditRecords\" SET \"Hash\" = 'tampered';"));
        await Assert.ThrowsAsync<PostgresException>(() => schema.ExecuteAsync(
            "SET ROLE regent_controlplane; INSERT INTO \"SchemaVersions\" (\"Version\", \"Name\", \"AppliedAt\") VALUES (99, 'forbidden', now());"));
    }

    [PostgresMigrationTheory]
    [InlineData("unexpected-policy")]
    [InlineData("missing-policy")]
    [InlineData("weakened-tenant-policy")]
    [InlineData("tenant-policy-setting-case")]
    [InlineData("tenant-policy-setting-space")]
    [InlineData("tenant-policy-wrong-quoted-column")]
    [InlineData("missing-membership-policy")]
    [InlineData("membership-policy-using-true")]
    [InlineData("membership-policy-setting-case")]
    [InlineData("membership-policy-wrong-quoted-column")]
    [InlineData("duplicated-membership-policy")]
    [InlineData("membership-policy-wrong-command")]
    [InlineData("membership-policy-wrong-roles")]
    [InlineData("owned-table")]
    [InlineData("owned-non-tenant-table")]
    [InlineData("privileged-role-membership")]
    [InlineData("createrole-role")]
    [InlineData("createdb-role")]
    [InlineData("createrole-role-membership")]
    [InlineData("createdb-role-membership")]
    [InlineData("audit-update-grant")]
    [InlineData("audit-column-update-grant")]
    [InlineData("schema-insert-grant")]
    [InlineData("schema-create-grant")]
    [InlineData("unmapped-tenant-table")]
    public async Task Invalid_rls_configuration_fails_startup(string defect)
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 7)) await schema.ApplyMigrationAsync(version);
        await schema.ApplyRuntimeGrantsAsync();
        string? privilegedRole = null;
        var owner = (string)(await schema.ScalarAsync("SELECT current_user;"))!;
        var runtimeRoleWasCreateRole = Equals(true, await schema.ScalarAsync(
            "SELECT rolcreaterole FROM pg_roles WHERE rolname = 'regent_controlplane';"));
        var runtimeRoleWasCreateDb = Equals(true, await schema.ScalarAsync(
            "SELECT rolcreatedb FROM pg_roles WHERE rolname = 'regent_controlplane';"));
        switch (defect)
        {
            case "unexpected-policy":
                await schema.ExecuteAsync("CREATE POLICY \"Projects_open_read\" ON \"Projects\" FOR SELECT USING (true);");
                break;
            case "missing-policy":
                await schema.ExecuteAsync("DROP POLICY \"Projects_tenant_isolation\" ON \"Projects\";");
                break;
            case "weakened-tenant-policy":
                await schema.ExecuteAsync("""
                    DROP POLICY "Projects_tenant_isolation" ON "Projects";
                    CREATE POLICY "Projects_tenant_isolation" ON "Projects"
                    USING ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid OR true)
                    WITH CHECK ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid OR true);
                    """);
                break;
            case "tenant-policy-setting-case":
                await schema.ExecuteAsync("""
                    DROP POLICY "Projects_tenant_isolation" ON "Projects";
                    CREATE POLICY "Projects_tenant_isolation" ON "Projects"
                    USING ("TenantId" = nullif(current_setting('APP.TENANT_ID', true), '')::uuid)
                    WITH CHECK ("TenantId" = nullif(current_setting('APP.TENANT_ID', true), '')::uuid);
                    """);
                break;
            case "tenant-policy-setting-space":
                await schema.ExecuteAsync("""
                    DROP POLICY "Projects_tenant_isolation" ON "Projects";
                    CREATE POLICY "Projects_tenant_isolation" ON "Projects"
                    USING ("TenantId" = nullif(current_setting('app. tenant_id', true), '')::uuid)
                    WITH CHECK ("TenantId" = nullif(current_setting('app. tenant_id', true), '')::uuid);
                    """);
                break;
            case "tenant-policy-wrong-quoted-column":
                await schema.ExecuteAsync("""
                    ALTER TABLE "Projects" ADD COLUMN "tenantid" uuid;
                    DROP POLICY "Projects_tenant_isolation" ON "Projects";
                    CREATE POLICY "Projects_tenant_isolation" ON "Projects"
                    USING ("tenantid" = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK ("tenantid" = nullif(current_setting('app.tenant_id', true), '')::uuid);
                    """);
                break;
            case "missing-membership-policy":
                await schema.ExecuteAsync("DROP POLICY \"Memberships_user_read\" ON \"Memberships\";");
                break;
            case "membership-policy-using-true":
                await schema.ExecuteAsync("""
                    DROP POLICY "Memberships_user_read" ON "Memberships";
                    CREATE POLICY "Memberships_user_read" ON "Memberships" FOR SELECT USING (true);
                    """);
                break;
            case "membership-policy-setting-case":
                await schema.ExecuteAsync("""
                    DROP POLICY "Memberships_user_read" ON "Memberships";
                    CREATE POLICY "Memberships_user_read" ON "Memberships"
                    FOR SELECT USING ("UserId" = nullif(current_setting('APP.USER_ID', true), '')::uuid);
                    """);
                break;
            case "membership-policy-wrong-quoted-column":
                await schema.ExecuteAsync("""
                    ALTER TABLE "Memberships" ADD COLUMN "userid" uuid;
                    DROP POLICY "Memberships_user_read" ON "Memberships";
                    CREATE POLICY "Memberships_user_read" ON "Memberships"
                    FOR SELECT USING ("userid" = nullif(current_setting('app.user_id', true), '')::uuid);
                    """);
                break;
            case "duplicated-membership-policy":
                await schema.ExecuteAsync("""
                    CREATE POLICY "Memberships_user_read_copy" ON "Memberships"
                    FOR SELECT USING ("UserId" = nullif(current_setting('app.user_id', true), '')::uuid);
                    """);
                break;
            case "membership-policy-wrong-command":
                await schema.ExecuteAsync("""
                    DROP POLICY "Memberships_user_read" ON "Memberships";
                    CREATE POLICY "Memberships_user_read" ON "Memberships"
                    USING ("UserId" = nullif(current_setting('app.user_id', true), '')::uuid)
                    WITH CHECK ("UserId" = nullif(current_setting('app.user_id', true), '')::uuid);
                    """);
                break;
            case "membership-policy-wrong-roles":
                await schema.ExecuteAsync("""
                    DROP POLICY "Memberships_user_read" ON "Memberships";
                    CREATE POLICY "Memberships_user_read" ON "Memberships"
                    FOR SELECT TO regent_controlplane
                    USING ("UserId" = nullif(current_setting('app.user_id', true), '')::uuid);
                    """);
                break;
            case "owned-table":
                await schema.ExecuteAsync("ALTER TABLE \"Projects\" OWNER TO regent_controlplane;");
                break;
            case "owned-non-tenant-table":
                await schema.ExecuteAsync("ALTER TABLE \"Users\" OWNER TO regent_controlplane;");
                break;
            case "privileged-role-membership":
                privilegedRole = $"regent_test_bypass_{Guid.NewGuid():N}";
                await schema.ExecuteAsync($"CREATE ROLE \"{privilegedRole}\" BYPASSRLS; GRANT \"{privilegedRole}\" TO regent_controlplane;");
                break;
            case "createrole-role":
                await schema.ExecuteAsync("ALTER ROLE regent_controlplane CREATEROLE;");
                break;
            case "createdb-role":
                await schema.ExecuteAsync("ALTER ROLE regent_controlplane CREATEDB;");
                break;
            case "createrole-role-membership":
                privilegedRole = $"regent_test_createrole_{Guid.NewGuid():N}";
                await schema.ExecuteAsync($"CREATE ROLE \"{privilegedRole}\" CREATEROLE; GRANT \"{privilegedRole}\" TO regent_controlplane;");
                break;
            case "createdb-role-membership":
                privilegedRole = $"regent_test_createdb_{Guid.NewGuid():N}";
                await schema.ExecuteAsync($"CREATE ROLE \"{privilegedRole}\" CREATEDB; GRANT \"{privilegedRole}\" TO regent_controlplane;");
                break;
            case "audit-update-grant":
                await schema.ExecuteAsync("GRANT UPDATE ON \"AuditRecords\" TO regent_controlplane;");
                break;
            case "audit-column-update-grant":
                await schema.ExecuteAsync("GRANT UPDATE (\"Hash\") ON \"AuditRecords\" TO regent_controlplane;");
                break;
            case "schema-insert-grant":
                await schema.ExecuteAsync("GRANT INSERT ON \"SchemaVersions\" TO regent_controlplane;");
                break;
            case "schema-create-grant":
                await schema.ExecuteAsync($"GRANT CREATE ON SCHEMA \"{schema.Name}\" TO regent_controlplane;");
                break;
            case "unmapped-tenant-table":
                await schema.ExecuteAsync("CREATE TABLE \"UnmappedTenantRows\" (\"Id\" integer PRIMARY KEY, \"TenantId\" uuid NOT NULL);");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(defect));
        }

        try
        {
            await using var factory = new ControlPlaneRuntimeFactory("Development", "Postgres", false, schema.RuntimeConnectionString);
            var error = Record.Exception(() => factory.CreateClient());
            Assert.NotNull(error);
            Assert.Contains("Postgres RLS validation failed", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (defect == "owned-table")
                await schema.ExecuteAsync($"ALTER TABLE \"Projects\" OWNER TO \"{owner.Replace("\"", "\"\"")}\";");
            else if (defect == "owned-non-tenant-table")
                await schema.ExecuteAsync($"ALTER TABLE \"Users\" OWNER TO \"{owner.Replace("\"", "\"\"")}\";");
            if (defect == "createrole-role")
                await schema.ExecuteAsync($"ALTER ROLE regent_controlplane {(runtimeRoleWasCreateRole ? "CREATEROLE" : "NOCREATEROLE")};");
            else if (defect == "createdb-role")
                await schema.ExecuteAsync($"ALTER ROLE regent_controlplane {(runtimeRoleWasCreateDb ? "CREATEDB" : "NOCREATEDB")};");
            if (privilegedRole is not null)
            {
                await schema.ExecuteAsync($"REVOKE \"{privilegedRole}\" FROM regent_controlplane; DROP ROLE \"{privilegedRole}\";");
            }
        }
    }

    [PostgresMigrationFact]
    public async Task Canonical_policies_and_milestone_rls_enforce_two_tenant_canaries()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 7)) await schema.ApplyMigrationAsync(version);
        await schema.ApplyRuntimeGrantsAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var milestoneA = Guid.NewGuid();
        var milestoneB = Guid.NewGuid();
        await schema.InsertTenantAsync(tenantA);
        await schema.InsertTenantAsync(tenantB);
        await schema.InsertUserAsync(userA);
        await schema.InsertUserAsync(userB);
        await schema.InsertMembershipAsync(tenantA, userA);
        await schema.InsertMembershipAsync(tenantB, userB);
        await schema.InsertProjectAsync(tenantA, projectA, "Tenant A project");
        await schema.InsertProjectAsync(tenantB, projectB, "Tenant B project");
        await schema.InsertMilestoneAsync(tenantA, projectA, milestoneA);
        await schema.InsertMilestoneAsync(tenantB, projectB, milestoneB);

        await using var withoutTenant = new NpgsqlConnection(schema.RuntimeConnectionString);
        await withoutTenant.OpenAsync();
        await using (var command = withoutTenant.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM \"Projects\";";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }

        await using var runtime = new NpgsqlConnection(schema.RuntimeConnectionString);
        await runtime.OpenAsync();
        await using var transaction = await runtime.BeginTransactionAsync();
        await using (var command = runtime.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id, true), set_config('app.user_id', @user_id, true);";
            command.Parameters.AddWithValue("tenant_id", tenantA.ToString());
            command.Parameters.AddWithValue("user_id", userA.ToString());
            await command.ExecuteNonQueryAsync();
        }
        await using (var command = runtime.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT count(*) FROM \"Projects\";";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        await using (var command = runtime.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT count(*) FROM \"Memberships\";";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        await using (var command = runtime.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT count(*) FROM \"Milestones\";";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        await using (var command = runtime.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO "Milestones" ("Id", "TenantId", "ProjectId", "Name", "TargetAt", "Status", "AcceptanceRule")
                VALUES (@id, @tenant_id, @project_id, 'Cross-tenant', now(), 'Pending', 'Must fail');
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("tenant_id", tenantA);
            command.Parameters.AddWithValue("project_id", projectB);
            await Assert.ThrowsAnyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }
        await transaction.RollbackAsync();
    }

    private static async Task<(Guid TenantA, Guid TenantB)> CreateSchemaFourWithTenantsAsync(PostgresTemporarySchema schema)
    {
        await schema.ApplyMigrationAsync(3);
        await schema.ApplyMigrationAsync(4);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await schema.InsertTenantAsync(tenantA);
        await schema.InsertTenantAsync(tenantB);
        return (tenantA, tenantB);
    }

    private static void AssertCompleteCommentCoverage(IReadOnlyList<SchemaCommentCoverage> coverage,
        long expectedColumns = 231, long expectedIndexes = 55)
    {
        Assert.Equal(new[] { "columns", "constraints", "extensions", "indexes", "tables" }, coverage.Select(item => item.ObjectType));
        Assert.Equal(new long[] { expectedColumns, 29, 2, expectedIndexes, 27 }, coverage.Select(item => item.Total));
        Assert.All(coverage, item =>
        {
            Assert.True(item.Total > 0, $"{item.ObjectType} must have a non-zero denominator.");
            Assert.Equal(item.Total, item.Commented);
            Assert.Equal(0, item.Missing);
            Assert.Equal(100m, item.CoveragePercent);
            Assert.Equal(string.Empty, item.MissingObjects);
        });
    }

    private static async Task AssertCommentsAndSecurityAsync(PostgresTemporarySchema schema)
    {
        Assert.False(string.IsNullOrWhiteSpace((string?)await schema.ScalarAsync("SELECT obj_description(format('%I.%I', current_schema(), 'MemoryEmbeddings')::regclass, 'pg_class');")));
        Assert.False(string.IsNullOrWhiteSpace((string?)await schema.ScalarAsync("""
            SELECT col_description(format('%I.%I', current_schema(), 'MemoryEmbeddings')::regclass, a.attnum) FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings' AND a.attname = 'Vector';
            """)));
        Assert.False(string.IsNullOrWhiteSpace((string?)await schema.ScalarAsync("SELECT obj_description(format('%I.%I', current_schema(), 'IX_MemoryEmbeddings_Vector')::regclass, 'pg_class');")));
        Assert.False(string.IsNullOrWhiteSpace((string?)await schema.ScalarAsync("""
            SELECT col_description(format('%I.%I', current_schema(), 'Positions')::regclass, a.attnum) FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'Positions' AND a.attname = 'Path';
            """)));
        Assert.False(string.IsNullOrWhiteSpace((string?)await schema.ScalarAsync("SELECT obj_description(format('%I.%I', current_schema(), 'IX_Positions_Path')::regclass, 'pg_class');")));
        foreach (var table in new[] { "MemoryEmbeddings", "Positions" })
        {
            Assert.Equal(true, await schema.ScalarAsync($"""
                SELECT c.relrowsecurity AND c.relforcerowsecurity FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND c.relname = '{table}';
                """));
            Assert.Equal(1L, await schema.ScalarAsync($"""
                SELECT count(*) FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND c.relname = '{table}'
                  AND {PostgresRlsPolicyCatalog.CanonicalTenantPolicy("p", "c")};
                """));
        }
    }
}
