using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ButlerServiceTests
{
    [Fact]
    public async Task Butler_answers_and_generates_scheduled_reports()
    {
        var root = Path.Combine(Path.GetTempPath(), $"butler-{Guid.NewGuid():N}");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            await DbInitializer.InitializeAsync(db, CancellationToken.None);
            var service = new ButlerService(db,
                new JsonDocumentStore<ButlerReport>(Path.Combine(root, "reports.json")),
                new JsonDocumentStore<ReportSchedule>(Path.Combine(root, "schedules.json")), TimeProvider.System);
            var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var answer = await service.AskAsync(tenantId, "What is the project status?", CancellationToken.None);
            var report = await service.GenerateReportAsync(tenantId, "Daily", CancellationToken.None);
            var schedules = await service.ListSchedulesAsync(tenantId, CancellationToken.None);
            await service.SaveScheduleAsync(tenantId, new CreateReportScheduleRequest("Budget", "Weekly", true), CancellationToken.None);

            Assert.Equal("projects", answer.Intent);
            Assert.Contains("Tenant Dashboard MVP", answer.Answer);
            Assert.Equal("Completed", report.Status);
            Assert.Equal(2, schedules.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
