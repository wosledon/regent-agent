using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Api;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.ControlPlane.Realtime;
using RegentAgent.Chat;
using RegentAgent.Communication;
using RegentAgent.Evaluation;
using RegentAgent.Memory;
using RegentAgent.Performance;
using RegentAgent.Workflows;
using RegentAgent.Contracts.Artifacts;
using RegentAgent.Contracts.Dashboard;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Organization;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Contracts.Projects;
using RegentAgent.Contracts.Runtime;
using RegentAgent.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

builder.Services.AddOpenApi("v1");
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<ArtifactService>();
builder.Services.AddScoped<TaskExecutionService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddSingleton(sp => new WorkflowService(
    new JsonDocumentStore<WorkflowDefinition>("data/modules/workflows.json"),
    new JsonDocumentStore<WorkflowRun>("data/modules/workflow-runs.json")));
builder.Services.AddSingleton(sp => new MemoryService(
    new JsonDocumentStore<MemoryClaim>("data/modules/memory.json"),
    new JsonDocumentStore<CorrectionTask>("data/modules/corrections.json")));
builder.Services.AddSingleton(sp => new PerformanceService(
    new JsonDocumentStore<KpiSnapshot>("data/modules/kpis.json"),
    new JsonDocumentStore<PerformanceReview>("data/modules/reviews.json")));
builder.Services.AddSingleton(sp => new ComplianceService(
    new JsonDocumentStore<ComplianceProfile>("data/modules/compliance.json")));
builder.Services.AddSingleton<CommunicationRouter>();
builder.Services.AddSingleton(sp => new ChatService(
    new JsonDocumentStore<Conversation>("data/modules/conversations.json"),
    new JsonDocumentStore<ChatMessage>("data/modules/messages.json")));
builder.Services.AddSingleton(sp => new EvaluationService(
    new JsonDocumentStore<BenchmarkSuite>("data/modules/suites.json"),
    new JsonDocumentStore<EvaluationRun>("data/modules/evaluations.json"),
    new JsonDocumentStore<JudgeAssignment>("data/modules/judges.json")));
builder.Services.AddSingleton<IObjectStore>(sp =>
    new LocalFileObjectStore(sp.GetRequiredService<IConfiguration>()["Storage:ObjectRoot"] ?? "data/objects"));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<RegentDbContext>(options =>
{
    var provider = builder.Configuration["Storage:Provider"] ?? "Sqlite";
    if (builder.Configuration.GetValue<bool>("Storage:UseInMemory") || provider == "InMemory")
    {
        options.UseInMemoryDatabase("regent-controlplane");
    }
    else if (provider == "Sqlite")
    {
        Directory.CreateDirectory("data");
        options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=data/regent.db");
    }
    else
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
    }
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "regent.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Frontend:Origins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
    await db.Database.EnsureCreatedAsync();
    await LocalSchemaUpgrade.ApplyAsync(db, CancellationToken.None);
    await DbInitializer.InitializeAsync(db, CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<WorkflowService>().SeedAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<MemoryService>().SeedAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<PerformanceService>().SeedAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<ChatService>().SeedAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<EvaluationService>().SeedAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<ComplianceService>().SeedAsync(CancellationToken.None);
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (RegentDbContext db, CancellationToken cancellationToken) =>
{
    var ready = await db.Database.CanConnectAsync(cancellationToken);
    return ready ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
}).AllowAnonymous();

app.MapPost("/auth/dev-login", async (DevLoginRequest request, RegentDbContext db,
    HttpContext context, CancellationToken cancellationToken) =>
{
    if (!app.Environment.IsDevelopment())
    {
        return Results.NotFound();
    }

    var email = request.Email.Trim().ToLowerInvariant();
    var memberships = await db.Memberships.AsNoTracking()
        .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { m.TenantId, m.Role, u.Id, u.Email, u.DisplayName })
        .Where(x => x.Email == email)
        .ToListAsync(cancellationToken);
    var membership = memberships.FirstOrDefault(x => request.TenantId is null || x.TenantId == request.TenantId)
        ?? memberships.FirstOrDefault();
    if (membership is null)
    {
        return ApiResults.Problem(RegentAgent.Contracts.Results.Error.Unauthorized("Unknown development user."), context.TraceIdentifier);
    }

    await SignInTenantAsync(context, membership.Id, membership.TenantId, membership.DisplayName, membership.Role);
    return Results.Ok(new { email = membership.Email, display_name = membership.DisplayName, tenant_id = membership.TenantId });
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/auth/switch-tenant", async (SwitchTenantRequest request, RegentDbContext db,
    HttpContext context, CancellationToken cancellationToken) =>
{
    var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var membership = await db.Memberships.AsNoTracking()
        .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { m.TenantId, m.Role, u.Id, u.DisplayName })
        .SingleOrDefaultAsync(x => x.TenantId == request.TenantId && x.Id == userId, cancellationToken);
    if (membership is null) return Results.NotFound();
    await SignInTenantAsync(context, userId, membership.TenantId, membership.DisplayName, membership.Role);
    return Results.Ok(new { tenant_id = membership.TenantId });
}).RequireAuthorization();

var api = app.MapGroup("/v1").RequireAuthorization();

api.MapGet("/tenants", async (TenantService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListForUserAsync(cancellationToken))).Produces<IReadOnlyList<TenantDto>>().WithTags("Tenancy");
api.MapPost("/tenants", async (CreateTenantRequest request, TenantService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateAsync(request.Name, request.CompanyName, cancellationToken)))
    .Produces<TenantDto>().WithTags("Tenancy");
api.MapGet("/charter", async (OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetCharterAsync(cancellationToken))).Produces<CharterDto>().WithTags("Organization");
api.MapPut("/charter", async (CharterDto charter, OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.UpdateCharterAsync(charter, cancellationToken))).Produces<CharterDto>().WithTags("Organization");
api.MapPost("/company/start", async (OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.StartCompanyAsync(cancellationToken))).Produces<EmployeeSummary>().WithTags("Organization");
api.MapGet("/positions", async (OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListPositionsAsync(cancellationToken))).Produces<IReadOnlyList<PositionDto>>().WithTags("Organization");
api.MapPost("/positions", async (PositionDto position, OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreatePositionAsync(position, cancellationToken))).Produces<PositionDto>().WithTags("Organization");
api.MapPost("/employees/{employeeId:guid}/assignments", async (Guid employeeId, AssignPositionRequest request,
    OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AssignAsync(employeeId, request.PositionId, cancellationToken)))
    .Produces<EmploymentPeriodDto>().WithTags("Organization");
api.MapPost("/delegations", async (CreateDelegationRequest request, OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.DelegateAsync(request.DelegateEmployeeId, request.SubtreePositionId,
        request.Actions, request.BudgetLimitMinor, request.Minutes, cancellationToken)))
    .Produces<OrgDelegationDto>().WithTags("Organization");
api.MapDelete("/delegations/{id:guid}", async (Guid id, OrganizationService service, CancellationToken cancellationToken) =>
{
    await service.RevokeDelegationAsync(id, cancellationToken);
    return Results.NoContent();
}).WithTags("Organization");
api.MapPost("/employees/{employeeId:guid}/offboard", async (Guid employeeId, OffboardRequest request,
    OrganizationService service, CancellationToken cancellationToken) =>
{
    await service.OffboardAsync(employeeId, request.ApprovalId, cancellationToken);
    return Results.NoContent();
}).WithTags("Organization");
api.MapPost("/candidates", async (CreateCandidateRequest request, OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateCandidateAsync(request.PositionId, request.DisplayName, request.ModelVersion, cancellationToken)))
    .Produces<CandidateDto>().WithTags("Recruitment");
api.MapPost("/candidates/{candidateId:guid}/assessment", async (Guid candidateId, CandidateAssessmentDto assessment,
    OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AssessCandidateAsync(candidateId, assessment, cancellationToken)))
    .Produces<CandidateDto>().WithTags("Recruitment");
api.MapPost("/candidates/{candidateId:guid}/hire", async (Guid candidateId, HireCandidateRequest request,
    OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.HireCandidateAsync(candidateId, request.ApprovalId, cancellationToken)))
    .Produces<EmployeeSummary>().WithTags("Recruitment");

api.MapGet("/dashboard/summary", async (DashboardService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(cancellationToken)))
    .Produces<DashboardSummary>(StatusCodes.Status200OK).WithTags("Dashboard");

api.MapGet("/projects", async (ProjectService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAsync(cancellationToken)))
    .Produces<IReadOnlyList<ProjectSummary>>(StatusCodes.Status200OK).WithTags("Projects");

api.MapGet("/projects/{id:guid}", async (Guid id, ProjectService service, CancellationToken cancellationToken) =>
{
    var project = await service.GetAsync(new ProjectId(id), cancellationToken);
    return project is null ? Results.NotFound() : Results.Ok(project);
}).Produces<ProjectDetail>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).WithTags("Projects");

api.MapGet("/employees", async (EmployeeService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAsync(cancellationToken)))
    .Produces<IReadOnlyList<EmployeeSummary>>(StatusCodes.Status200OK).WithTags("Organization");

api.MapGet("/employees/{id:guid}/timeline", async (Guid id, EmployeeService service, CancellationToken cancellationToken) =>
{
    var timeline = await service.GetTimelineAsync(new EmployeeId(id), cancellationToken);
    return timeline is null ? Results.NotFound() : Results.Ok(timeline);
}).Produces<EmployeeTimeline>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).WithTags("Organization");

api.MapGet("/approvals/{id:guid}", async (Guid id, ApprovalService service, CancellationToken cancellationToken) =>
{
    var approval = await service.GetAsync(new ApprovalId(id), cancellationToken);
    return approval is null ? Results.NotFound() : Results.Ok(approval);
}).Produces<ApprovalDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).WithTags("Governance");

api.MapPost("/approvals/{id:guid}/decision", async (Guid id, ApprovalDecision decision,
    ApprovalService service, IHubContext<TenantHub> hub, HttpContext context, CancellationToken cancellationToken) =>
{
    if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var key) || string.IsNullOrWhiteSpace(key))
    {
        return ApiResults.Problem(RegentAgent.Contracts.Results.Error.Validation("Idempotency-Key is required."), context.TraceIdentifier);
    }

    var result = await service.DecideAsync(new ApprovalId(id), decision, cancellationToken);
    if (!result.IsSuccess)
    {
        return ApiResults.Problem(result.Error!, context.TraceIdentifier);
    }

    var tenantId = context.User.FindFirstValue("tenant_id");
    if (!string.IsNullOrWhiteSpace(tenantId))
    {
        await hub.Clients.Group(tenantId).SendAsync("approval.changed", result.Value, cancellationToken);
    }
    return Results.Ok(result.Value);
}).Produces<ApprovalDto>(StatusCodes.Status200OK).WithTags("Governance");

api.MapGet("/notifications", async (HttpContext context, RegentDbContext db,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(CurrentTenant(context));
    var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var items = await db.Notifications.AsNoTracking()
        .Where(x => x.TenantId == tenantId && x.RecipientId == userId)
        .ToListAsync(cancellationToken);
    return Results.Ok(items.OrderByDescending(x => x.CreatedAt).ToArray());
}).WithTags("Notifications");
api.MapPost("/notifications/{id:guid}/read", async (Guid id, HttpContext context, RegentDbContext db,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(CurrentTenant(context));
    var item = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
    if (item is null) return Results.NotFound();
    item.Status = "Read";
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
}).WithTags("Notifications");
api.MapPost("/notifications/{id:guid}/acknowledge", async (Guid id, HttpContext context, RegentDbContext db,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(CurrentTenant(context));
    var item = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
    if (item is null) return Results.NotFound();
    item.Status = "Acknowledged";
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
}).WithTags("Notifications");
api.MapPost("/notifications/{id:guid}/archive", async (Guid id, HttpContext context, RegentDbContext db,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(CurrentTenant(context));
    var item = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
    if (item is null) return Results.NotFound();
    item.Status = "Archived";
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
}).WithTags("Notifications");

api.MapGet("/compliance/profile", async (HttpContext context, ComplianceService service,
    CancellationToken cancellationToken) => Results.Ok(await service.GetAsync(CurrentTenant(context), cancellationToken)))
    .WithTags("Compliance");
api.MapPut("/compliance/profile", async (ComplianceProfile profile, HttpContext context,
    ComplianceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.UpdateAsync(CurrentTenant(context), profile, cancellationToken))).WithTags("Compliance");

api.MapGet("/workflows", async (WorkflowService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAsync(cancellationToken))).WithTags("Workflows");
api.MapPost("/workflows/{id}/runs", async (string id, HttpContext context, WorkflowService service,
    CancellationToken cancellationToken) => Results.Ok(await service.StartAsync(id, CurrentTenant(context), cancellationToken)))
    .WithTags("Workflows");
api.MapPost("/workflow-runs/{id}/advance", async (string id, WorkflowService service,
    CancellationToken cancellationToken) => Results.Ok(await service.AdvanceAsync(id, cancellationToken))).WithTags("Workflows");

api.MapGet("/memories", async (string? query, HttpContext context, MemoryService service,
    CancellationToken cancellationToken) => Results.Ok(await service.SearchAsync(CurrentTenant(context), query, cancellationToken)))
    .WithTags("Memory");
api.MapPost("/memories/{id}/correct", async (string id, CorrectMemoryRequest request, HttpContext context,
    MemoryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CorrectAsync(id, CurrentTenant(context), request.Content, request.Reason, cancellationToken)))
    .WithTags("Memory");

api.MapGet("/kpis", async (HttpContext context, PerformanceService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(CurrentTenant(context), cancellationToken)))
    .WithTags("Performance");
api.MapPost("/performance-reviews", async (CreateReviewRequest request, HttpContext context,
    PerformanceService service, CancellationToken cancellationToken) => Results.Ok(await service.ReviewAsync(
        request.EmployeeId, CurrentTenant(context), request.Conclusion, request.EvidenceJson, cancellationToken)))
    .WithTags("Performance");

api.MapGet("/communication/routes", (CommunicationRouter router) =>
{
    var nodes = new[]
    {
        new RouteNode("controller", null, new[] { "management" }),
        new RouteNode("engineering", "controller", new[] { "code" }),
        new RouteNode("quality", "controller", new[] { "quality" })
    };
    return Results.Ok(new
    {
        management = router.ManagementRoute(nodes[1], nodes),
        professional = router.ProfessionalRoute(nodes[1], "quality", nodes)
    });
}).WithTags("Communication");

api.MapGet("/conversations", async (HttpContext context, ChatService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(CurrentTenant(context), cancellationToken)))
    .WithTags("Chat");
api.MapGet("/conversations/{id}/messages", async (string id, ChatService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListMessagesAsync(id, cancellationToken))).WithTags("Chat");
api.MapPost("/conversations/{id}/messages", async (string id, SendMessageRequest request, HttpContext context,
    ChatService service, IHubContext<TenantHub> hub, CancellationToken cancellationToken) =>
{
    var message = await service.SendAsync(id, CurrentTenant(context), CurrentUser(context), request.Body,
        request.Priority, cancellationToken);
    await hub.Clients.Group(CurrentTenant(context)).SendAsync("message.created", message, cancellationToken);
    return Results.Ok(message);
}).WithTags("Chat");

api.MapGet("/evaluation-suites", async (HttpContext context, EvaluationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListSuitesAsync(CurrentTenant(context), cancellationToken)))
    .WithTags("Evaluation");
api.MapGet("/evaluations/runs", async (HttpContext context, EvaluationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListRunsAsync(CurrentTenant(context), cancellationToken)))
    .WithTags("Evaluation");
api.MapPost("/evaluations/runs", async (RunEvaluationRequest request, HttpContext context,
    EvaluationService service, CancellationToken cancellationToken) => Results.Ok(await service.RunAsync(
        request.SuiteId, request.CandidateEmployeeId, CurrentTenant(context), request.Seed, cancellationToken)))
    .WithTags("Evaluation");

var runnerApi = app.MapGroup("/internal/runners").AllowAnonymous();
runnerApi.MapPost("/register", (RunnerRegistration registration) => Results.Ok(new { runner_id = registration.RunnerId, status = "registered" }));
runnerApi.MapPost("/heartbeat", (RunnerHeartbeat heartbeat) => Results.Ok(new { runner_id = heartbeat.RunnerId, status = heartbeat.Status }));

app.MapPost("/internal/tasks/claim", async (HttpContext context, TaskExecutionService service,
    CancellationToken cancellationToken) =>
{
    var runnerId = context.Request.Headers["X-Runner-Id"].ToString();
    if (string.IsNullOrWhiteSpace(runnerId)) return Results.BadRequest("X-Runner-Id is required.");
    var tenantId = Guid.Parse(context.Request.Headers["X-Regent-Tenant"].FirstOrDefault()
        ?? "11111111-1111-1111-1111-111111111111");
    var task = await service.ClaimAsync(runnerId, tenantId, cancellationToken);
    return task is null ? Results.NoContent() : Results.Ok(task);
}).AllowAnonymous();

app.MapPost("/internal/tasks/{id:guid}/checkpoint", async (Guid id, TaskCheckpoint checkpoint,
    HttpContext context, TaskExecutionService service, CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(context.Request.Headers["X-Regent-Tenant"].FirstOrDefault()
        ?? "11111111-1111-1111-1111-111111111111");
    return await service.CheckpointAsync(id, checkpoint, tenantId, cancellationToken) ? Results.NoContent() : Results.Conflict();
}).AllowAnonymous();

app.MapPost("/internal/tasks/{id:guid}/complete", async (Guid id, TaskCompletion completion,
    HttpContext context, TaskExecutionService service, CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(context.Request.Headers["X-Regent-Tenant"].FirstOrDefault()
        ?? "11111111-1111-1111-1111-111111111111");
    return await service.CompleteAsync(id, completion, tenantId, cancellationToken) ? Results.NoContent() : Results.Conflict();
}).AllowAnonymous();

app.MapHub<TenantHub>("/hubs/tenant").RequireAuthorization();
app.Run();

static string CurrentTenant(HttpContext context) =>
    context.User.FindFirstValue("tenant_id") ?? "11111111-1111-1111-1111-111111111111";

static string CurrentUser(HttpContext context) => context.User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

static async Task SignInTenantAsync(HttpContext context, Guid userId, Guid tenantId, string displayName, string role)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim("tenant_id", tenantId.ToString()),
        new Claim(ClaimTypes.Name, displayName),
        new Claim(ClaimTypes.Role, role)
    };
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
}

public sealed record DevLoginRequest(string Email, Guid? TenantId = null);
public sealed record SwitchTenantRequest(Guid TenantId);
public sealed record CreateTenantRequest(string Name, string CompanyName);
public sealed record AssignPositionRequest(Guid PositionId);
public sealed record CreateDelegationRequest(Guid DelegateEmployeeId, Guid SubtreePositionId, IReadOnlyList<string> Actions, long BudgetLimitMinor, int Minutes);
public sealed record OffboardRequest(string ApprovalId);
public sealed record CreateCandidateRequest(Guid PositionId, string DisplayName, string ModelVersion);
public sealed record HireCandidateRequest(string ApprovalId);
public sealed record CorrectMemoryRequest(string Content, string Reason);
public sealed record CreateReviewRequest(string EmployeeId, string Conclusion, string EvidenceJson);
public sealed record SendMessageRequest(string Body, string Priority);
public sealed record RunEvaluationRequest(string SuiteId, string CandidateEmployeeId, int Seed);
