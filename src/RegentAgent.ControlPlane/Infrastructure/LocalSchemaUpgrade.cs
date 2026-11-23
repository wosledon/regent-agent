using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Infrastructure;

public static class LocalSchemaUpgrade
{
    public static async Task ApplyAsync(RegentDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlite()) return;
        const string schema = """
            CREATE TABLE IF NOT EXISTS Charters (
                TenantId TEXT NOT NULL PRIMARY KEY,
                Mission TEXT NOT NULL, ConstraintsJson TEXT NOT NULL,
                MonthlyBudgetMinor INTEGER NOT NULL, Currency TEXT NOT NULL,
                GovernanceJson TEXT NOT NULL, Version INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Positions (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL,
                Name TEXT NOT NULL, Responsibilities TEXT NOT NULL,
                CapabilitiesJson TEXT NOT NULL, BudgetMinor INTEGER NOT NULL,
                ParentPositionId TEXT NULL, Status TEXT NOT NULL, Version INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Positions_TenantId_Name ON Positions (TenantId, Name);
            CREATE TABLE IF NOT EXISTS EmploymentPeriods (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL,
                EmployeeId TEXT NOT NULL, PositionId TEXT NOT NULL,
                Kind TEXT NOT NULL, StartedAt TEXT NOT NULL, EndedAt TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_EmploymentPeriods_TenantId_EmployeeId_EndedAt ON EmploymentPeriods (TenantId, EmployeeId, EndedAt);
            CREATE INDEX IF NOT EXISTS IX_EmploymentPeriods_TenantId_PositionId_EndedAt ON EmploymentPeriods (TenantId, PositionId, EndedAt);
            CREATE TABLE IF NOT EXISTS OrgDelegations (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL,
                DelegatorEmployeeId TEXT NOT NULL, DelegateEmployeeId TEXT NOT NULL,
                SubtreePositionId TEXT NOT NULL, ActionsJson TEXT NOT NULL,
                BudgetLimitMinor INTEGER NOT NULL, ExpiresAt TEXT NOT NULL, Status TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_OrgDelegations_TenantId_DelegateEmployeeId_Status ON OrgDelegations (TenantId, DelegateEmployeeId, Status);
            CREATE TABLE IF NOT EXISTS Candidates (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL,
                PositionId TEXT NOT NULL, DisplayName TEXT NOT NULL,
                ModelVersion TEXT NOT NULL, Status TEXT NOT NULL,
                AssessmentJson TEXT NOT NULL, FairnessJson TEXT NOT NULL, CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Candidates_TenantId_PositionId_Status ON Candidates (TenantId, PositionId, Status);
            """;
        await db.Database.ExecuteSqlRawAsync(schema, cancellationToken);
    }
}
