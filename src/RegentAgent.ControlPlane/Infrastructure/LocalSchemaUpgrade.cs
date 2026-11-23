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
                ModelVersion TEXT NOT NULL, JobDescription TEXT NOT NULL DEFAULT '', Status TEXT NOT NULL,
                AssessmentJson TEXT NOT NULL, FairnessJson TEXT NOT NULL, CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Candidates_TenantId_PositionId_Status ON Candidates (TenantId, PositionId, Status);
            CREATE TABLE IF NOT EXISTS InterviewStages (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, CandidateId TEXT NOT NULL,
                Stage TEXT NOT NULL, Status TEXT NOT NULL, Score REAL NOT NULL,
                EvidenceJson TEXT NOT NULL, CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_InterviewStages_TenantId_CandidateId_Stage ON InterviewStages (TenantId, CandidateId, Stage);
            CREATE TABLE IF NOT EXISTS CandidateAppeals (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, CandidateId TEXT NOT NULL,
                Reason TEXT NOT NULL, Status TEXT NOT NULL, Resolution TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CandidateAppeals_TenantId_CandidateId_Status ON CandidateAppeals (TenantId, CandidateId, Status);
            CREATE TABLE IF NOT EXISTS FairnessAudits (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, PositionId TEXT NOT NULL,
                Period TEXT NOT NULL, SampleSize INTEGER NOT NULL, SelectionRateRatio REAL NOT NULL,
                Status TEXT NOT NULL, DetailsJson TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FairnessAudits_TenantId_PositionId_Period ON FairnessAudits (TenantId, PositionId, Period);
            CREATE TABLE IF NOT EXISTS IdempotencyRecords (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, Key TEXT NOT NULL,
                Operation TEXT NOT NULL, ResponseJson TEXT NOT NULL, Status TEXT NOT NULL, CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_IdempotencyRecords_TenantId_Key_Operation ON IdempotencyRecords (TenantId, Key, Operation);
            CREATE TABLE IF NOT EXISTS Outbox (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, EventType TEXT NOT NULL,
                PayloadJson TEXT NOT NULL, Status TEXT NOT NULL, Attempts INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL, ProcessedAt TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Outbox_Status_CreatedAt ON Outbox (Status, CreatedAt);
            CREATE TABLE IF NOT EXISTS Inbox (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, MessageId TEXT NOT NULL,
                EventType TEXT NOT NULL, PayloadJson TEXT NOT NULL, Status TEXT NOT NULL, CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Inbox_TenantId_MessageId ON Inbox (TenantId, MessageId);
            CREATE TABLE IF NOT EXISTS SchemaVersions (
                Version INTEGER NOT NULL PRIMARY KEY, Name TEXT NOT NULL, AppliedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS AuditCheckpoints (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL,
                ThroughSeq INTEGER NOT NULL, MerkleRoot TEXT NOT NULL,
                Profile TEXT NOT NULL, CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_AuditCheckpoints_TenantId_ThroughSeq ON AuditCheckpoints (TenantId, ThroughSeq);
            """;
        await db.Database.ExecuteSqlRawAsync(schema, cancellationToken);
        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT name FROM pragma_table_info('AuditRecords')").ToListAsync(cancellationToken);
        if (!columns.Contains("PreviousHash"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE AuditRecords ADD COLUMN PreviousHash TEXT NOT NULL DEFAULT '';", cancellationToken);
        if (!columns.Contains("Hash"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE AuditRecords ADD COLUMN Hash TEXT NOT NULL DEFAULT '';", cancellationToken);
        var projectColumns = await db.Database.SqlQueryRaw<string>(
            "SELECT name FROM pragma_table_info('Projects')").ToListAsync(cancellationToken);
        if (!projectColumns.Contains("Mission"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Projects ADD COLUMN Mission TEXT NOT NULL DEFAULT '';", cancellationToken);
        var candidateColumns = await db.Database.SqlQueryRaw<string>(
            "SELECT name FROM pragma_table_info('Candidates')").ToListAsync(cancellationToken);
        if (!candidateColumns.Contains("JobDescription"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Candidates ADD COLUMN JobDescription TEXT NOT NULL DEFAULT '';", cancellationToken);
    }
}
