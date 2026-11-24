using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Infrastructure;

public static class DbInitializer
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);

    public static async Task InitializeAsync(RegentDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Tenants.AnyAsync(x => x.Id == TenantId, cancellationToken))
        {
            await EnsureOrganizationSeedAsync(db, cancellationToken);
            return;
        }

        db.Tenants.Add(new TenantRecord
        {
            Id = TenantId,
            Name = "regent-demo",
            CompanyName = "Regent Autonomous Company"
        });
        db.Users.Add(new UserRecord
        {
            Id = OwnerId,
            Email = "owner@example.test",
            DisplayName = "Demo Owner"
        });
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = OwnerId
        });

        var employees = new[]
        {
            new EmployeeRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeNo = "EMP-0001", DisplayName = "Controller", Position = "Controller", State = EmployeeState.Active },
            new EmployeeRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeNo = "EMP-0002", DisplayName = "Engineering Lead", Position = "Engineering", State = EmployeeState.Busy, StateReason = "Implementing release candidate" },
            new EmployeeRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeNo = "EMP-0003", DisplayName = "Quality Reviewer", Position = "Quality", State = EmployeeState.Active },
            new EmployeeRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeNo = "EMP-0004", DisplayName = "Research Analyst", Position = "Research", State = EmployeeState.Sleeping, StateReason = "Waiting for next scheduled window" }
        };
        db.Employees.AddRange(employees);

        var projectA = new ProjectRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Name = "Tenant Dashboard MVP", Owner = "Engineering Lead",
            Stage = ProjectStage.Verifying, Health = ProjectHealth.AtRisk, TargetAt = Now.AddDays(5),
            BudgetMinor = 800_000, SpentMinor = 612_000, Currency = "CNY", UpdatedAt = Now.AddMinutes(-3)
        };
        var projectB = new ProjectRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Name = "Provider Contract Hardening", Owner = "Quality Reviewer",
            Stage = ProjectStage.Building, Health = ProjectHealth.OnTrack, TargetAt = Now.AddDays(9),
            BudgetMinor = 500_000, SpentMinor = 180_000, Currency = "CNY", UpdatedAt = Now.AddMinutes(-8)
        };
        var projectC = new ProjectRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Name = "Memory Poisoning Defense", Owner = "Research Analyst",
            Stage = ProjectStage.Approved, Health = ProjectHealth.Blocked, TargetAt = Now.AddDays(12),
            BudgetMinor = 350_000, SpentMinor = 42_000, Currency = "CNY", UpdatedAt = Now.AddMinutes(-15)
        };
        db.Projects.AddRange(projectA, projectB, projectC);

        projectA.Milestones.Add(new MilestoneRecord
        {
            Id = Guid.NewGuid(), ProjectId = projectA.Id, Name = "Vertical slice accepted",
            TargetAt = Now.AddDays(2), Status = "InProgress", AcceptanceRule = "Login to approval flow passes"
        });
        projectB.Milestones.Add(new MilestoneRecord
        {
            Id = Guid.NewGuid(), ProjectId = projectB.Id, Name = "Native contract suite",
            TargetAt = Now.AddDays(6), Status = "Pending", AcceptanceRule = "OpenAI and Anthropic contracts pass"
        });

        var tasks = new[]
        {
            new TaskRecord { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectA.Id, Title = "Build Boss dashboard", TaskType = "engineering.implement", Status = "InReview", Assignee = "Engineering Lead", Weight = 5, CostMinor = 186_000 },
            new TaskRecord { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectA.Id, Title = "Verify approval flow", TaskType = "acceptance.review", Status = "InProgress", Assignee = "Quality Reviewer", Weight = 3, CostMinor = 74_000 },
            new TaskRecord { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectB.Id, Title = "Record provider fixtures", TaskType = "test.execute", Status = "InProgress", Assignee = "Quality Reviewer", Weight = 3, CostMinor = 62_000 },
            new TaskRecord { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectC.Id, Title = "Review poisoning corpus", TaskType = "research.execute", Status = "Blocked", Assignee = "Research Analyst", Weight = 5, CostMinor = 42_000 },
            new TaskRecord { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectA.Id, Title = "Generate local delivery summary", TaskType = "documentation", Status = "Pending", Assignee = string.Empty, Weight = 2, CostMinor = 0 }
        };
        db.Tasks.AddRange(tasks);

        var engineering = employees[1];
        db.WorkEntries.AddRange(
            new WorkEntryRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = engineering.Id, TaskId = tasks[0].Id,
                OccurredAt = Now.AddMinutes(-42), Title = "Submitted dashboard vertical slice", EventType = "task.submitted",
                Status = "InReview", NextAction = "Independent review", CostMinor = 32_000,
                EvidenceJson = JsonSerializer.Serialize(new[] { "artifact:dashboard-slice", "test:ui-pass" })
            },
            new WorkEntryRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = engineering.Id, TaskId = tasks[0].Id,
                OccurredAt = Now.AddHours(-3), Title = "Completed shared component gallery", EventType = "task.progress",
                Status = "Done", NextAction = null, CostMinor = 48_000,
                EvidenceJson = JsonSerializer.Serialize(new[] { "artifact:component-gallery" })
            },
            new WorkEntryRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = engineering.Id, TaskId = tasks[1].Id,
                OccurredAt = Now.AddHours(-6), Title = "Fixed approval state transition", EventType = "task.completed",
                Status = "Done", NextAction = "Quality verification", CostMinor = 28_000,
                EvidenceJson = JsonSerializer.Serialize(new[] { "test:approval-transition" })
            });

        db.Approvals.AddRange(
            new ApprovalRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ActionType = "release.execute", Title = "Approve dashboard release candidate",
                Impact = "Deploys the first tenant dashboard vertical slice.", Severity = Severity.High,
                Requester = "Controller", CreatedAt = Now.AddMinutes(-20), ExpiresAt = Now.AddHours(4),
                OptionsJson = JsonSerializer.Serialize(new[] { "Approve", "Reject" }),
                EvidenceJson = JsonSerializer.Serialize(new[] { "evidence:test-pass", "evidence:security-clean" })
            },
            new ApprovalRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ActionType = "budget.increase", Title = "Increase provider contract budget",
                Impact = "Raises project budget by CNY 500.", Severity = Severity.Medium,
                Requester = "Quality Reviewer", CreatedAt = Now.AddHours(-2), ExpiresAt = Now.AddHours(22),
                OptionsJson = JsonSerializer.Serialize(new[] { "Approve", "Reject" }),
                EvidenceJson = JsonSerializer.Serialize(new[] { "evidence:cost-forecast" })
            });

        db.Notifications.AddRange(
            new NotificationRecord { Id = Guid.NewGuid(), TenantId = TenantId, RecipientId = OwnerId, Level = "High", Title = "Dashboard release candidate awaits approval", CreatedAt = Now.AddMinutes(-20) },
            new NotificationRecord { Id = Guid.NewGuid(), TenantId = TenantId, RecipientId = OwnerId, Level = "Medium", Title = "Provider contract budget needs review", CreatedAt = Now.AddHours(-2) });

        db.AuditRecords.Add(new AuditRecord
        {
            TenantId = TenantId, Seq = 1, EventType = "tenant.seeded", Actor = "system",
            PayloadJson = "{}", CreatedAt = Now
        });

        await db.SaveChangesAsync(cancellationToken);
        await EnsureOrganizationSeedAsync(db, cancellationToken);
    }

    private static async Task EnsureOrganizationSeedAsync(RegentDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Charters.AnyAsync(x => x.TenantId == TenantId, cancellationToken))
        {
            db.Charters.Add(new CharterRecord
            {
                TenantId = TenantId, Mission = "Build useful software products autonomously.",
                ConstraintsJson = JsonSerializer.Serialize(new[] { "Keep tenant data isolated", "Require evidence for completion" }),
                MonthlyBudgetMinor = 8_000_000, Currency = "CNY",
                GovernanceJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["approval"] = "boss.final",
                    ["work_schedule"] = "24x7.configurable"
                }),
                Version = 1, UpdatedAt = Now
            });
        }

        if (!await db.Positions.AnyAsync(x => x.TenantId == TenantId, cancellationToken))
        {
            var controller = new PositionRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, Name = "Controller",
                Responsibilities = "Plan the organization and coordinate delivery.",
                CapabilitiesJson = JsonSerializer.Serialize(new[] { "org.design", "project.decide", "task.assign" }),
                BudgetMinor = 1_000_000, Status = "Filled"
            };
            db.Positions.Add(controller);
            db.Positions.AddRange(
                new PositionRecord { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Engineering", ParentPositionId = controller.Id, Responsibilities = "Implement and verify product changes.", CapabilitiesJson = "[\"code.implement\",\"test.execute\"]", BudgetMinor = 2_000_000, Status = "Filled" },
                new PositionRecord { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Quality", ParentPositionId = controller.Id, Responsibilities = "Independently verify delivery evidence.", CapabilitiesJson = "[\"acceptance.review\",\"test.execute\"]", BudgetMinor = 1_000_000, Status = "Filled" },
                new PositionRecord { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Research", ParentPositionId = controller.Id, Responsibilities = "Research opportunities and preserve evidence.", CapabilitiesJson = "[\"research.execute\",\"product.define\"]", BudgetMinor = 1_000_000, Status = "Filled" });
            await db.SaveChangesAsync(cancellationToken);
        }

        var positions = (await db.Positions.AsNoTracking().Where(x => x.TenantId == TenantId).ToListAsync(cancellationToken))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var employees = await db.Employees.Where(x => x.TenantId == TenantId).ToListAsync(cancellationToken);
        foreach (var employee in employees.Where(x => positions.ContainsKey(x.Position)))
        {
            if (await db.EmploymentPeriods.AnyAsync(x => x.TenantId == TenantId && x.EmployeeId == employee.Id && x.EndedAt == null, cancellationToken)) continue;
            db.EmploymentPeriods.Add(new EmploymentPeriodRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employee.Id,
                PositionId = positions[employee.Position].Id, Kind = "Primary", StartedAt = Now
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
