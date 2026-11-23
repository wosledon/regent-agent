using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;

namespace RegentAgent.ControlPlane.Data;

public sealed class RegentDbContext(DbContextOptions<RegentDbContext> options) : DbContext(options)
{
    public DbSet<TenantRecord> Tenants => Set<TenantRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<MembershipRecord> Memberships => Set<MembershipRecord>();
    public DbSet<CharterRecord> Charters => Set<CharterRecord>();
    public DbSet<PositionRecord> Positions => Set<PositionRecord>();
    public DbSet<EmploymentPeriodRecord> EmploymentPeriods => Set<EmploymentPeriodRecord>();
    public DbSet<OrgDelegationRecord> OrgDelegations => Set<OrgDelegationRecord>();
    public DbSet<CandidateRecord> Candidates => Set<CandidateRecord>();
    public DbSet<EmployeeRecord> Employees => Set<EmployeeRecord>();
    public DbSet<ProjectRecord> Projects => Set<ProjectRecord>();
    public DbSet<MilestoneRecord> Milestones => Set<MilestoneRecord>();
    public DbSet<TaskRecord> Tasks => Set<TaskRecord>();
    public DbSet<WorkEntryRecord> WorkEntries => Set<WorkEntryRecord>();
    public DbSet<ApprovalRecord> Approvals => Set<ApprovalRecord>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<AuditCheckpointRecord> AuditCheckpoints => Set<AuditCheckpointRecord>();
    public DbSet<ArtifactRecord> Artifacts => Set<ArtifactRecord>();
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectRecord>(entity =>
        {
            entity.Property(x => x.Stage).HasConversion<string>();
            entity.Property(x => x.Health).HasConversion<string>();
            entity.HasMany(x => x.Milestones).WithOne().HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.Tasks).WithOne().HasForeignKey(x => x.ProjectId);
            entity.HasIndex(x => new { x.TenantId, x.UpdatedAt });
        });

        modelBuilder.Entity<CharterRecord>(entity =>
        {
            entity.HasKey(x => x.TenantId);
        });

        modelBuilder.Entity<PositionRecord>(entity =>
        {
            entity.HasIndex(x => new { x.TenantId, x.Name });
        });

        modelBuilder.Entity<EmploymentPeriodRecord>(entity =>
        {
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.EndedAt });
            entity.HasIndex(x => new { x.TenantId, x.PositionId, x.EndedAt });
        });

        modelBuilder.Entity<OrgDelegationRecord>(entity =>
        {
            entity.HasIndex(x => new { x.TenantId, x.DelegateEmployeeId, x.Status });
        });

        modelBuilder.Entity<CandidateRecord>(entity =>
        {
            entity.HasIndex(x => new { x.TenantId, x.PositionId, x.Status });
        });

        modelBuilder.Entity<EmployeeRecord>(entity =>
        {
            entity.Property(x => x.State).HasConversion<string>();
            entity.HasIndex(x => new { x.TenantId, x.EmployeeNo }).IsUnique();
        });

        modelBuilder.Entity<TaskRecord>(entity =>
        {
            entity.HasIndex(x => new { x.TenantId, x.ProjectId, x.Status });
        });

        modelBuilder.Entity<WorkEntryRecord>(entity =>
        {
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.OccurredAt });
        });

        modelBuilder.Entity<ApprovalRecord>(entity =>
        {
            entity.Property(x => x.Severity).HasConversion<string>();
            entity.Property(x => x.State).HasConversion<string>();
            entity.HasIndex(x => new { x.TenantId, x.State, x.ExpiresAt });
        });

        modelBuilder.Entity<AuditRecord>(entity =>
        {
            entity.HasIndex(x => new { x.TenantId, x.Seq }).IsUnique();
        });

        modelBuilder.Entity<AuditCheckpointRecord>(entity =>
        {
            entity.HasIndex(x => new { x.TenantId, x.ThroughSeq }).IsUnique();
        });

        modelBuilder.Entity<ArtifactRecord>(entity =>
        {
            entity.HasIndex(x => new { x.TenantId, x.ProjectId, x.UploadState });
        });

        modelBuilder.Entity<NotificationRecord>(entity =>
        {
            entity.HasIndex(x => new { x.TenantId, x.RecipientId, x.CreatedAt });
        });
    }
}
