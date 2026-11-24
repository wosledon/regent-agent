using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RegentAgent.AgentIdentity;
using RegentAgent.Agents;
using RegentAgent.ControlPlane.Api;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Application.AgentTools;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.ControlPlane.Realtime;
using RegentAgent.Chat;
using RegentAgent.Communication;
using RegentAgent.Evaluation;
using RegentAgent.Memory;
using RegentAgent.Observability;
using RegentAgent.Performance;
using RegentAgent.Projects;
using RegentAgent.Providers;
using RegentAgent.Workflows;
using RegentAgent.Contracts.Agents;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Artifacts;
using RegentAgent.Contracts.Collaboration;
using RegentAgent.Contracts.Dashboard;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Evaluation;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Identity;
using RegentAgent.Contracts.Memory;
using RegentAgent.Contracts.Models;
using RegentAgent.Contracts.Operations;
using RegentAgent.Contracts.Organization;
using RegentAgent.Contracts.Pagination;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Contracts.Projects;
using RegentAgent.Contracts.Runtime;
using RegentAgent.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRegentObservability(builder.Configuration);
builder.Services.AddAgentIdentity(builder.Configuration);
var runnerToken = builder.Configuration["Runner:Token"];
if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(runnerToken))
    throw new InvalidOperationException("Production ControlPlane requires Runner:Token.");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

builder.Services.AddOpenApi("v1");
builder.Services.AddSignalR();
var httpContextAccessor = new HttpContextAccessor();
builder.Services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
builder.Services.AddScoped<ITenantContext, TenantContext>();
var operationsDataPath = builder.Configuration["Data:OperationsPath"]
    ?? (builder.Environment.IsDevelopment()
        ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data", "ops"))
        : "data/ops");
builder.Services.AddSingleton(new JsonDocumentStore<AlertRecord>(Path.Combine(operationsDataPath, "alerts.json")));
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<CollaborationVisualizationService>();
var collaborationOptions = new CollaborationOptions();
builder.Configuration.GetSection("Collaboration").Bind(collaborationOptions);
if (collaborationOptions.DefaultWipLimit <= 0 || collaborationOptions.MaxInterruptsPerHour <= 0
    || collaborationOptions.MaxCoordinationPayloadChars <= 0)
    throw new InvalidOperationException("Collaboration limits must be positive.");
builder.Services.AddSingleton(collaborationOptions);
builder.Services.AddScoped<CollaborationCoordinationService>();
builder.Services.AddScoped<AgentToolCatalog>();
builder.Services.AddScoped<AgentToolInvocationService>();
builder.Services.AddScoped<IAgentToolHandler, ChatToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, TaskToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, CollaborationToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, MemoryToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, WorkflowToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, ProjectToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, OrganizationToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, ImprovementToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, RecruitmentToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, PerformanceToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, EvaluationToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, AgentEvolutionToolHandler>();
builder.Services.AddScoped<IAgentToolHandler, TaskGovernanceToolHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, CapabilityAuthorizationHandler>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<ApprovalExpiryService>();
builder.Services.AddScoped<ArtifactService>();
builder.Services.AddScoped<TaskExecutionService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<AuditIntegrityService>();
builder.Services.AddScoped<MemoryEventService>();
builder.Services.AddSingleton(new JsonDocumentStore<LocalizationSettings>("data/modules/localization.json"));
builder.Services.AddSingleton<LocalizationService>();
builder.Services.AddSingleton(sp => new DeliveryService(
    new JsonDocumentStore<ProjectDeliveryState>("data/modules/project-delivery.json"), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new LocalArtifactSigner("data/security/tool-signing-key.pem"));
builder.Services.AddSingleton(sp => new GovernanceService(
    new JsonDocumentStore<GovernanceState>("data/modules/governance.json"),
    sp.GetRequiredService<LocalArtifactSigner>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(new JsonDocumentStore<DataLifecycleState>("data/modules/lifecycle.json"));
builder.Services.AddScoped<DataLifecycleService>();
var modelCatalogMode = builder.Configuration["ModelCatalog:Mode"] ?? "local";
var modelsDevOptions = new ModelsDevCatalogOptions
{
    Url = builder.Configuration["ModelCatalog:ModelsDevUrl"] ?? "https://models.dev/api.json",
    TimeoutSeconds = builder.Configuration.GetValue("ModelCatalog:TimeoutSeconds", 30),
    MaxResponseBytes = builder.Configuration.GetValue("ModelCatalog:MaxResponseBytes", 16 * 1024 * 1024),
    MaxRetries = builder.Configuration.GetValue("ModelCatalog:MaxRetries", 2),
    BaseDelayMilliseconds = builder.Configuration.GetValue("ModelCatalog:BaseDelayMilliseconds", 200)
};
if (modelCatalogMode.Equals("models-dev", StringComparison.OrdinalIgnoreCase)) modelsDevOptions.Validate();
builder.Services.AddSingleton(modelsDevOptions);
builder.Services.AddHttpClient("models-dev", (provider, client) =>
    client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<ModelsDevCatalogOptions>().TimeoutSeconds));
builder.Services.AddSingleton<IModelCatalogSource>(provider => modelCatalogMode.Trim().ToLowerInvariant() switch
{
    "local" => new LocalModelCatalogSource(provider.GetRequiredService<TimeProvider>()),
    "models-dev" => new ModelsDevCatalogSource(provider.GetRequiredService<IHttpClientFactory>().CreateClient("models-dev"),
        provider.GetRequiredService<ModelsDevCatalogOptions>(), provider.GetRequiredService<TimeProvider>()),
    _ => throw new InvalidOperationException("Model catalog mode must be local or models-dev.")
});
builder.Services.AddSingleton(new JsonDocumentStore<ModelPoolState>("data/modules/model-pool.json"));
builder.Services.AddSingleton(sp => new ModelOperationsService(
    sp.GetRequiredService<JsonDocumentStore<ModelPoolState>>(), sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<IModelCatalogSource>()));
builder.Services.AddSingleton(sp => new RuntimePolicyService(
    new JsonDocumentStore<RuntimePolicyState>("data/modules/runtime-policy.json"), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new WorkflowService(
    new JsonDocumentStore<WorkflowDefinition>("data/modules/workflows.json"),
    new JsonDocumentStore<WorkflowRun>("data/modules/workflow-runs.json")));
builder.Services.AddSingleton(new JsonDocumentStore<MemoryClaim>("data/modules/memory.json"));
builder.Services.AddSingleton(new JsonDocumentStore<CorrectionTask>("data/modules/corrections.json"));
builder.Services.AddSingleton(new JsonDocumentStore<MemoryVectorRecord>("data/modules/memory-vectors.json"));
builder.Services.AddSingleton(new JsonDocumentStore<MemoryFinding>("data/modules/memory-findings.json"));
builder.Services.AddSingleton(new JsonDocumentStore<MemoryImpactRecord>("data/modules/memory-impacts.json"));
builder.Services.AddSingleton(new JsonDocumentStore<MemoryContextBinding>("data/modules/memory-contexts.json"));
builder.Services.AddSingleton(new JsonDocumentStore<MemoryGraphNode>("data/modules/memory-graph-nodes.json"));
builder.Services.AddSingleton(new JsonDocumentStore<MemoryGraphEdge>("data/modules/memory-graph-edges.json"));
builder.Services.AddSingleton<MemoryGraphService>();
builder.Services.AddSingleton<MemoryIndexService>();
builder.Services.AddSingleton<MemorySafetyService>();
builder.Services.AddSingleton<MemoryService>();
builder.Services.AddSingleton(sp => new PerformanceService(
    new JsonDocumentStore<KpiSnapshot>("data/modules/kpis.json"),
    new JsonDocumentStore<PerformanceReview>("data/modules/reviews.json")));
builder.Services.AddSingleton(sp => new ImprovementService(
    new JsonDocumentStore<ImprovementProposal>("data/modules/improvements.json")));
builder.Services.AddSingleton(sp => new PerformanceActionService(
    new JsonDocumentStore<PerformanceActionRecord>("data/modules/performance-actions.json")));
builder.Services.AddSingleton(sp => new AgentEvolutionService(
    new JsonDocumentStore<AgentEvolutionState>("data/modules/agent-evolution.json"), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new AgentRuntimeService(
    new JsonDocumentStore<AgentContextSnapshot>("data/modules/agent-contexts.json"), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new TaskGovernanceService(
    new JsonDocumentStore<TaskGovernanceState>("data/modules/task-governance.json"), sp.GetRequiredService<TimeProvider>()));
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
builder.Services.AddSingleton(sp => new EvaluationQaService(
    new JsonDocumentStore<EvaluationQaState>("data/modules/evaluation-qa.json")));
var objectProvider = builder.Configuration["Storage:ObjectProvider"] ?? "Local";
if (objectProvider.Equals("Local", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IObjectStore>(provider =>
        new LocalFileObjectStore(provider.GetRequiredService<IConfiguration>()["Storage:ObjectRoot"] ?? "data/objects"));
}
else if (objectProvider.Equals("S3", StringComparison.OrdinalIgnoreCase))
{
    var s3Options = new S3ObjectStoreOptions
    {
        ServiceUrl = builder.Configuration["Storage:S3:ServiceUrl"] ?? string.Empty,
        Region = builder.Configuration["Storage:S3:Region"] ?? "us-east-1",
        Bucket = builder.Configuration["Storage:S3:Bucket"] ?? string.Empty,
        AccessKey = builder.Configuration["Storage:S3:AccessKey"] ?? string.Empty,
        SecretKey = builder.Configuration["Storage:S3:SecretKey"] ?? string.Empty,
        ForcePathStyle = builder.Configuration.GetValue("Storage:S3:ForcePathStyle", true),
        TimeoutSeconds = builder.Configuration.GetValue("Storage:S3:TimeoutSeconds", 120),
        MaxRetries = builder.Configuration.GetValue("Storage:S3:MaxRetries", 2)
    };
    s3Options.Validate();
    builder.Services.AddSingleton<IObjectStore>(S3ObjectStore.Create(s3Options));
}
else throw new InvalidOperationException("Object storage provider must be Local or S3.");
var auditAnchorProvider = builder.Configuration["Audit:AnchorProvider"] ?? "Local";
if (auditAnchorProvider.Equals("Local", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IAuditAnchorStore>(new LocalFileAuditAnchorStore("data/audit-anchors"));
else if (auditAnchorProvider.Equals("ObjectStore", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IAuditAnchorStore>(provider =>
        new ObjectStoreAuditAnchorStore(provider.GetRequiredService<IObjectStore>()));
else throw new InvalidOperationException("Audit anchor provider must be Local or ObjectStore.");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginRateLimiter>();
builder.Services.AddSingleton(new JsonDocumentStore<CredentialRecord>("data/modules/credentials.json"));
builder.Services.AddSingleton(new JsonDocumentStore<AuthSessionRecord>("data/modules/sessions.json"));
builder.Services.AddSingleton(new JsonDocumentStore<RecoveryCodeRecord>("data/modules/recovery-codes.json"));
builder.Services.AddSingleton(new JsonDocumentStore<UserTenantProfileRecord>("data/modules/profiles.json"));
builder.Services.AddSingleton(new JsonDocumentStore<IdentityProviderRecord>("data/modules/identity-providers.json"));
builder.Services.AddSingleton(new JsonDocumentStore<InvitationRecord>("data/modules/invitations.json"));
builder.Services.AddSingleton(new JsonDocumentStore<OwnershipDelegationRecord>("data/modules/ownership-delegations.json"));
builder.Services.AddSingleton(new JsonDocumentStore<ExternalIdentityBindingRecord>("data/modules/external-identity-bindings.json"));
builder.Services.AddSingleton(new JsonDocumentStore<ExternalLoginTransactionRecord>("data/modules/external-login-transactions.json"));
builder.Services.AddSingleton(new JsonDocumentStore<AuthenticationPolicyRecord>("data/modules/authentication-policies.json"));
builder.Services.AddSingleton(new JsonDocumentStore<EmergencyAccessCredentialRecord>("data/modules/emergency-access-credentials.json"));
var externalIdentityOptions = new ExternalIdentityOptions
{
    Mode = builder.Configuration["Identity:Mode"] ?? "local",
    TimeoutSeconds = builder.Configuration.GetValue("Identity:TimeoutSeconds", 30),
    MaxResponseBytes = builder.Configuration.GetValue("Identity:MaxResponseBytes", 1024 * 1024),
    GitHub = new GitHubIdentityOptions
    {
        AuthorizationUrl = builder.Configuration["Identity:GitHub:AuthorizationUrl"] ?? "https://github.com/login/oauth/authorize",
        TokenUrl = builder.Configuration["Identity:GitHub:TokenUrl"] ?? "https://github.com/login/oauth/access_token",
        UserUrl = builder.Configuration["Identity:GitHub:UserUrl"] ?? "https://api.github.com/user",
        EmailsUrl = builder.Configuration["Identity:GitHub:EmailsUrl"] ?? "https://api.github.com/user/emails"
    },
    Oidc = new OidcIdentityOptions
    {
        AllowedAlgorithms = builder.Configuration.GetSection("Identity:Oidc:AllowedAlgorithms").Get<string[]>() ?? ["RS256"],
        ClockSkewSeconds = builder.Configuration.GetValue("Identity:Oidc:ClockSkewSeconds", 60),
        RequireNonce = builder.Configuration.GetValue("Identity:Oidc:RequireNonce", true)
    }
};
if (externalIdentityOptions.Mode.Trim().ToLowerInvariant() is not ("local" or "remote")
    || externalIdentityOptions.TimeoutSeconds is < 1 or > 300
    || externalIdentityOptions.MaxResponseBytes is < 1024 or > 16 * 1024 * 1024)
    throw new InvalidOperationException("External identity mode or HTTP limits are invalid.");
builder.Services.AddSingleton(externalIdentityOptions);
builder.Services.AddSingleton<IExternalIdentitySecretProvider, ConfigurationIdentitySecretProvider>();
builder.Services.AddHttpClient("github-identity", (_, client) =>
    client.Timeout = TimeSpan.FromSeconds(externalIdentityOptions.TimeoutSeconds));
builder.Services.AddHttpClient("oidc-discovery", (_, client) =>
    client.Timeout = TimeSpan.FromSeconds(externalIdentityOptions.TimeoutSeconds));
builder.Services.AddSingleton<IExternalIdentityProvider, LocalExternalIdentityProvider>();
builder.Services.AddSingleton<IExternalIdentityProvider>(provider => new GitHubExternalIdentityProvider(
    provider.GetRequiredService<IHttpClientFactory>().CreateClient("github-identity"),
    provider.GetRequiredService<ExternalIdentityOptions>(), provider.GetRequiredService<IExternalIdentitySecretProvider>()));
builder.Services.AddSingleton<IExternalIdentityProvider>(provider => new OidcExternalIdentityProvider(
    provider.GetRequiredService<IHttpClientFactory>().CreateClient("oidc-discovery"),
    provider.GetRequiredService<ExternalIdentityOptions>(), provider.GetRequiredService<IExternalIdentitySecretProvider>()));
builder.Services.AddSingleton<IExternalIdentityProviderResolver, ExternalIdentityProviderResolver>();
builder.Services.AddScoped<IdentityService>();
var billingProvider = builder.Configuration["Billing:Provider"] ?? "local";
if (!billingProvider.Equals("local", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("External payment adapter is not configured; use local mode for self-hosted billing.");
builder.Services.AddSingleton<ISubscriptionPaymentAdapter, LocalSubscriptionPaymentAdapter>();
builder.Services.AddSingleton(new JsonDocumentStore<BillingStateRecord>("data/modules/billing.json"));
builder.Services.AddSingleton(new JsonDocumentStore<DataExportDto>("data/modules/exports.json"));
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<DataExportService>();
builder.Services.AddSingleton(new JsonDocumentStore<ButlerReport>("data/modules/butler-reports.json"));
builder.Services.AddSingleton(new JsonDocumentStore<ReportSchedule>("data/modules/report-schedules.json"));
builder.Services.AddScoped<ButlerService>();
builder.Services.AddHostedService<ButlerReportWorker>();
builder.Services.AddHostedService<DataLifecycleWorker>();

builder.Services.AddDbContext<RegentDbContext>(options =>
{
    var provider = builder.Configuration["Storage:Provider"] ?? "Sqlite";
    if (provider.Trim().ToLowerInvariant() is not ("sqlite" or "postgres" or "inmemory"))
        throw new InvalidOperationException("Storage provider must be Sqlite, Postgres, or InMemory.");
    if (builder.Configuration.GetValue<bool>("Storage:UseInMemory") || provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
    {
        options.UseInMemoryDatabase("regent-controlplane");
    }
    else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        Directory.CreateDirectory("data");
        options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=data/regent.db");
    }
    else
    {
        var connection = builder.Configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Postgres connection string is required.");
        options.UseNpgsql(connection).AddInterceptors(new TenantRlsConnectionInterceptor(httpContextAccessor));
    }
});

RegisterTenantStore<WorkflowDefinition>(builder.Services, "data/modules/workflows.json");
RegisterTenantStore<WorkflowRun>(builder.Services, "data/modules/workflow-runs.json");
RegisterTenantStore<MemoryClaim>(builder.Services, "data/modules/memory.json");
RegisterTenantStore<CorrectionTask>(builder.Services, "data/modules/corrections.json");
RegisterTenantStore<MemoryVectorRecord>(builder.Services, "data/modules/memory-vectors.json");
RegisterTenantStore<MemoryFinding>(builder.Services, "data/modules/memory-findings.json");
RegisterTenantStore<MemoryImpactRecord>(builder.Services, "data/modules/memory-impacts.json");
RegisterTenantStore<MemoryContextBinding>(builder.Services, "data/modules/memory-contexts.json");
RegisterTenantStore<MemoryGraphNode>(builder.Services, "data/modules/memory-graph-nodes.json");
RegisterTenantStore<MemoryGraphEdge>(builder.Services, "data/modules/memory-graph-edges.json");
RegisterTenantStore<KpiSnapshot>(builder.Services, "data/modules/kpis.json");
RegisterTenantStore<PerformanceReview>(builder.Services, "data/modules/reviews.json");
RegisterTenantStore<ImprovementProposal>(builder.Services, "data/modules/improvements.json");
RegisterTenantStore<PerformanceActionRecord>(builder.Services, "data/modules/performance-actions.json");
RegisterTenantStore<Conversation>(builder.Services, "data/modules/conversations.json");
RegisterTenantStore<ChatMessage>(builder.Services, "data/modules/messages.json");
RegisterTenantStore<BenchmarkSuite>(builder.Services, "data/modules/suites.json");
RegisterTenantStore<EvaluationRun>(builder.Services, "data/modules/evaluations.json");
RegisterTenantStore<JudgeAssignment>(builder.Services, "data/modules/judges.json");
RegisterTenantStore<EvaluationQaState>(builder.Services, "data/modules/evaluation-qa.json");
RegisterTenantStore<ComplianceProfile>(builder.Services, "data/modules/compliance.json");
RegisterTenantStore<LocalizationSettings>(builder.Services, "data/modules/localization.json");
RegisterTenantStore<ProjectDeliveryState>(builder.Services, "data/modules/project-delivery.json");
RegisterTenantStore<GovernanceState>(builder.Services, "data/modules/governance.json");
RegisterTenantStore<DataLifecycleState>(builder.Services, "data/modules/lifecycle.json");
RegisterTenantStore<ModelPoolState>(builder.Services, "data/modules/model-pool.json");
RegisterTenantStore<RuntimePolicyState>(builder.Services, "data/modules/runtime-policy.json");
RegisterTenantStore<AuthSessionRecord>(builder.Services, "data/modules/sessions.json");
RegisterTenantStore<IdentityProviderRecord>(builder.Services, "data/modules/identity-providers.json");
RegisterTenantStore<InvitationRecord>(builder.Services, "data/modules/invitations.json");
RegisterTenantStore<OwnershipDelegationRecord>(builder.Services, "data/modules/ownership-delegations.json");
RegisterTenantStore<UserTenantProfileRecord>(builder.Services, "data/modules/profiles.json");
RegisterTenantStore<ButlerReport>(builder.Services, "data/modules/butler-reports.json");
RegisterTenantStore<ReportSchedule>(builder.Services, "data/modules/report-schedules.json");
RegisterTenantStore<AgentEvolutionState>(builder.Services, "data/modules/agent-evolution.json");
RegisterTenantStore<TaskGovernanceState>(builder.Services, "data/modules/task-governance.json");
builder.Services.AddScoped<TenantPurgeService>();
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddSingleton(new CursorCodec(builder.Configuration["Api:CursorKey"] ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
builder.Services.AddScoped<OutboxService>();
builder.Services.AddScoped<SchemaVersionService>();
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<ApprovalExpiryWorker>();
builder.Services.AddHostedService<EvaluationRegressionWorker>();

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
    options.AddPolicy("MemoryGovernor", policy => policy.RequireRole("Owner", "CoOwner"));
    options.AddPolicy("Agent", policy => policy.RequireAuthenticatedUser().RequireClaim("employee_id"));
    options.AddPolicy("DashboardRead", policy => policy.AddRequirements(new CapabilityRequirement("dashboard.read")));
    options.AddPolicy("ReportsRead", policy => policy.AddRequirements(new CapabilityRequirement("reports.read")));
    options.AddPolicy("EmployeesRead", policy => policy.AddRequirements(new CapabilityRequirement("employees.read")));
    options.AddPolicy("CollaborationRead", policy => policy.AddRequirements(new CapabilityRequirement("collaboration.read")));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Frontend:Origins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var (status, code, message) = exception switch
    {
        KeyNotFoundException => (StatusCodes.Status404NotFound, "NOT_FOUND", "Resource not found."),
        LoginRateExceededException => (StatusCodes.Status429TooManyRequests, "RATE_LIMITED", exception.Message),
        Microsoft.AspNetCore.Http.BadHttpRequestException => (StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Request body is invalid."),
        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "FORBIDDEN", "Access denied."),
        InvalidOperationException => (StatusCodes.Status409Conflict, "INVALID_STATE", exception.Message),
        _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.")
    };
    context.Response.StatusCode = status;
    context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;
    await context.Response.WriteAsJsonAsync(new { request_id = context.TraceIdentifier, error = new { code, message, retryable = false } });
}));

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
    await db.Database.EnsureCreatedAsync();
    await LocalSchemaUpgrade.ApplyAsync(db, CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<SchemaVersionService>().ValidateAsync(CancellationToken.None);
    await DbInitializer.InitializeAsync(db, CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<AuditIntegrityService>().BackfillAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<WorkflowService>().SeedAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<MemoryService>().SeedAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<PerformanceService>().SeedAsync(CancellationToken.None);
    var chatEmployees = (await db.Employees.AsNoTracking().Where(x => x.TenantId == Guid.Parse("11111111-1111-1111-1111-111111111111"))
        .Select(x => new { x.Id, x.DisplayName, x.Position }).ToListAsync(CancellationToken.None))
        .Select(x => new ChatEmployee(x.Id.ToString(), x.DisplayName, x.Position)).ToArray();
    await scope.ServiceProvider.GetRequiredService<ChatService>().SeedAsync(chatEmployees, CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<EvaluationService>().SeedAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<ComplianceService>().SeedAsync(CancellationToken.None);
    foreach (var tenantId in await db.Tenants.Select(x => x.Id).ToListAsync(CancellationToken.None))
        await scope.ServiceProvider.GetRequiredService<ModelOperationsService>().SeedAsync(tenantId, CancellationToken.None);
}
else
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
    await scope.ServiceProvider.GetRequiredService<SchemaVersionService>().ValidateAsync(CancellationToken.None, false);
    await PostgresRlsValidator.ValidateAsync(db, CancellationToken.None);
}

app.UseCors("frontend");
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/agent/v1"))
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        try
        {
            var identity = context.RequestServices.GetRequiredService<AgentAssignmentTokenValidator>()
                .Validate(authorization["Bearer ".Length..].Trim());
            context.Items[AgentIdentityContext.Key] = identity;
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, identity.EmployeeId.ToString()),
                new(ClaimTypes.Name, identity.EmployeeId.ToString()),
                new("tenant_id", identity.TenantId.ToString()),
                new("employee_id", identity.EmployeeId.ToString()),
                new("actor_type", "Agent"),
                new("scopes", string.Join(' ', identity.Scopes))
            };
            if (identity.TaskId is { } taskId) claims.Add(new Claim("task_id", taskId.ToString()));
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Agent"));
        }
        catch (SecurityTokenException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    if (app.Environment.IsProduction() && context.Request.Path.StartsWithSegments("/internal"))
    {
        var provided = context.Request.Headers["X-Regent-Runner-Token"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(runnerToken ?? string.Empty)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    var sessionId = context.User.FindFirstValue("session_id");
    if (context.User.Identity?.IsAuthenticated == true && Guid.TryParse(sessionId, out var id))
    {
        var store = context.RequestServices.GetRequiredService<JsonDocumentStore<AuthSessionRecord>>();
        var session = await store.GetAsync(id.ToString(), context.RequestAborted);
        var clock = context.RequestServices.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow();
        var lastActivity = session?.LastActivityAt ?? session?.CreatedAt ?? now;
        if (session is null || session.Status != "Active" || session.ExpiresAt <= now
            || session.UserId != CurrentUserId(context) || session.TenantId != Guid.Parse(CurrentTenant(context))
            || now - lastActivity > TimeSpan.FromMinutes(session.IdleTimeoutMinutes))
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        if (session.SessionKind == "Emergency" && !EmergencyRequestAllowed(context.Request.Path,
            context.Request.Method, session.TenantId, session.AllowedActionsJson))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        session.LastActivityAt = now;
        await store.PutAsync(session.Id.ToString(), session, context.RequestAborted);
    }
    await next();
});
app.UseMiddleware<ApiContractMiddleware>();
app.UseAuthorization();
app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (RegentDbContext db, CancellationToken cancellationToken) =>
{
    var ready = await db.Database.CanConnectAsync(cancellationToken);
    return ready ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
}).AllowAnonymous();

app.MapPost("/auth/dev-login", async (DevLoginRequest request, RegentDbContext db, IdentityService identity,
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

    var session = await identity.StartTenantSessionAsync(membership.Id, membership.TenantId, cancellationToken, "Development");
    var displayName = await identity.DisplayNameAsync(membership.Id, membership.TenantId, membership.DisplayName, cancellationToken);
    await SignInTenantAsync(context, membership.Id, membership.TenantId, displayName, membership.Role, session.Id);
    return Results.Ok(new { email = membership.Email, display_name = displayName, tenant_id = membership.TenantId, role = membership.Role });
}).AllowAnonymous();

app.MapPost("/auth/register", async (RegisterRequest request, IdentityService identity,
    HttpContext context, CancellationToken cancellationToken) =>
    await CompleteAuthAsync(context, await identity.RegisterAsync(request, cancellationToken))).AllowAnonymous();
app.MapPost("/auth/login", async (LoginRequest request, IdentityService identity,
    HttpContext context, CancellationToken cancellationToken) =>
    await CompleteAuthAsync(context, await identity.LoginAsync(request, cancellationToken))).AllowAnonymous();
app.MapPost("/auth/recover", async (RecoveryRequest request, IdentityService identity,
    CancellationToken cancellationToken) =>
{
    await identity.RecoverAsync(request, cancellationToken);
    return Results.NoContent();
}).AllowAnonymous();
app.MapPost("/auth/emergency-access", async (EmergencyAccessRequest request, IdentityService identity,
    HttpContext context, CancellationToken cancellationToken) =>
{
    var result = await identity.UseEmergencyAccessAsync(request, cancellationToken);
    await SignInTenantAsync(context, result.UserId, result.TenantId, result.DisplayName, result.Role,
        result.SessionId, cancellationToken);
    return Results.Ok(result);
}).Produces<EmergencyAccessResult>().AllowAnonymous();
app.MapPost("/auth/github/login", async (ExternalLoginRequest request, IdentityService identity,
    HttpContext context, CancellationToken cancellationToken) =>
    await CompleteAuthAsync(context, await identity.ExternalLoginAsync(request, "GitHub", cancellationToken))).AllowAnonymous();
app.MapPost("/auth/oidc/login", async (ExternalLoginRequest request, IdentityService identity,
    HttpContext context, CancellationToken cancellationToken) =>
    await CompleteAuthAsync(context, await identity.ExternalLoginAsync(request, "Oidc", cancellationToken))).AllowAnonymous();
app.MapGet("/auth/{provider}/start", async (string provider, Guid tenantId, string? redirectUri,
    IdentityService identity, HttpContext context, CancellationToken cancellationToken) =>
{
    var authorization = await identity.StartExternalLoginAsync(tenantId, provider, redirectUri, cancellationToken);
    context.Response.Cookies.Append("regent.external.tx", authorization.TransactionId.ToString(), new CookieOptions
    {
        HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps,
        Expires = authorization.ExpiresAt
    });
    return Results.Ok(authorization);
}).AllowAnonymous();
app.MapGet("/auth/{provider}/callback", async (string provider, string code, string state,
    IdentityService identity, HttpContext context, CancellationToken cancellationToken) =>
{
    if (!context.Request.Cookies.TryGetValue("regent.external.tx", out var transactionId)
        || !Guid.TryParse(transactionId, out var id))
        throw new UnauthorizedAccessException("External login transaction cookie is missing.");
    var result = await identity.CompleteExternalLoginAsync(provider, id, new ExternalCallbackRequest(code, state), cancellationToken);
    context.Response.Cookies.Delete("regent.external.tx");
    await SignInTenantAsync(context, result.UserId, result.TenantId, result.DisplayName, result.Role, result.SessionId);
    return Results.Redirect(context.RequestServices.GetRequiredService<IConfiguration>()["Frontend:TenantUrl"]
        ?? "http://localhost:5173");
}).AllowAnonymous();

app.MapGet("/auth/sessions", async (HttpContext context, IdentityService identity,
    CancellationToken cancellationToken) => Results.Ok(await identity.ListSessionsAsync(
        Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken)))
    .Produces<IReadOnlyList<SessionDto>>().RequireAuthorization();
app.MapPost("/auth/sessions/{sessionId:guid}/revoke", async (Guid sessionId, HttpContext context,
    IdentityService identity, CancellationToken cancellationToken) =>
{
    await identity.RevokeSessionAsync(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), sessionId, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization();
app.MapPost("/auth/invitations/accept", async (AcceptInvitationRequest request, HttpContext context,
    IdentityService identity, CancellationToken cancellationToken) =>
{
    await identity.AcceptInvitationAsync(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), request.Token, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/auth/logout", async (HttpContext context, IdentityService identity) =>
{
    if (Guid.TryParse(context.User.FindFirstValue("session_id"), out var sessionId))
        await identity.RevokeSessionAsync(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), sessionId, CancellationToken.None);
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/auth/dev-agent-token", async (DevAgentTokenRequest request, RegentDbContext db,
    AgentAssignmentTokenService tokens, CancellationToken cancellationToken) =>
{
    if (!app.Environment.IsDevelopment()) return Results.NotFound();
    if (!await db.Employees.AnyAsync(x => x.TenantId == request.TenantId && x.Id == request.EmployeeId, cancellationToken))
        return Results.NotFound();
    if (request.TaskId is { } taskId && !await db.Tasks.AnyAsync(x => x.TenantId == request.TenantId && x.Id == taskId, cancellationToken))
        return Results.NotFound();
    var scopes = request.Scopes is { Count: > 0 } ? request.Scopes : AgentScopes.Baseline;
    var issued = tokens.Issue(new AgentAssignmentRequest(request.TenantId, request.EmployeeId, request.TaskId,
        request.RunnerId ?? "dev-runner", scopes));
    return Results.Ok(new { token = issued.Token, identity = issued.Identity });
}).AllowAnonymous();

app.MapPost("/auth/switch-tenant", async (SwitchTenantRequest request, RegentDbContext db, IdentityService identity,
    HttpContext context, CancellationToken cancellationToken) =>
{
    var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var membership = await db.Memberships.AsNoTracking()
        .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { m.TenantId, m.Role, u.Id, u.DisplayName })
        .SingleOrDefaultAsync(x => x.TenantId == request.TenantId && x.Id == userId, cancellationToken);
    if (membership is null) return Results.NotFound();
    if (Guid.TryParse(context.User.FindFirstValue("session_id"), out var previousSessionId))
        await identity.RevokeSessionAsync(userId, previousSessionId, cancellationToken);
    var authMethod = context.User.FindFirstValue("auth_method") ?? "Password";
    var session = await identity.StartTenantSessionAsync(userId, membership.TenantId, cancellationToken, authMethod);
    var displayName = await identity.DisplayNameAsync(userId, membership.TenantId, membership.DisplayName, cancellationToken);
    await SignInTenantAsync(context, userId, membership.TenantId, displayName, membership.Role, session.Id);
    return Results.Ok(new { tenant_id = membership.TenantId, display_name = displayName });
}).RequireAuthorization();

var api = app.MapGroup("/v1").RequireAuthorization();

api.MapPost("/butler/ask", async (ButlerQuestionRequest request, HttpContext context,
    ButlerService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AskAsync(Guid.Parse(CurrentTenant(context)), request.Question, cancellationToken)))
    .Produces<ButlerAnswer>().WithTags("Butler");
api.MapGet("/butler/reports", async (HttpContext context, ButlerService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListReportsAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<IReadOnlyList<ButlerReport>>().RequireAuthorization("ReportsRead").WithTags("Butler");
api.MapPost("/butler/reports", async (GenerateReportRequest request, HttpContext context,
    ButlerService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GenerateReportAsync(Guid.Parse(CurrentTenant(context)), request.Kind, cancellationToken)))
    .Produces<ButlerReport>().WithTags("Butler");
api.MapPost("/butler/reports/{reportId:guid}/retry", async (Guid reportId, HttpContext context,
    ButlerService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RetryReportAsync(Guid.Parse(CurrentTenant(context)), reportId, cancellationToken)))
    .Produces<ButlerReport>().WithTags("Butler");
api.MapGet("/butler/schedules", async (HttpContext context, ButlerService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListSchedulesAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<IReadOnlyList<ReportSchedule>>().WithTags("Butler");
api.MapPost("/butler/schedules", async (CreateReportScheduleRequest request, HttpContext context,
    ButlerService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.SaveScheduleAsync(Guid.Parse(CurrentTenant(context)), request, cancellationToken)))
    .Produces<ReportSchedule>().WithTags("Butler");

api.MapGet("/profile/display-suffix", async (HttpContext context, IdentityService identity,
    CancellationToken cancellationToken) => Results.Ok(new DisplaySuffixDto(await identity.GetDisplaySuffixAsync(
        CurrentUserId(context), Guid.Parse(CurrentTenant(context)), cancellationToken)))).WithTags("Identity");
api.MapPut("/profile/display-suffix", async (DisplaySuffixDto request, HttpContext context,
    IdentityService identity, CancellationToken cancellationToken) =>
{
    await identity.SetDisplaySuffixAsync(CurrentUserId(context), Guid.Parse(CurrentTenant(context)), request.Suffix, cancellationToken);
    return Results.Ok(request);
}).WithTags("Identity");
api.MapGet("/tenants/{tenantId:guid}/invitations", async (Guid tenantId, HttpContext context,
    IdentityService identity, CancellationToken cancellationToken) =>
    Results.Ok(await identity.ListInvitationsAsync(CurrentUserId(context), tenantId, cancellationToken)))
    .Produces<IReadOnlyList<InvitationDto>>().WithTags("Identity");
api.MapPost("/tenants/{tenantId:guid}/invitations", async (Guid tenantId, InvitationRequest request,
    HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
    Results.Ok(await identity.CreateInvitationAsync(CurrentUserId(context), tenantId, request, cancellationToken)))
    .Produces<InvitationDto>().WithTags("Identity");
api.MapGet("/tenants/{tenantId:guid}/ownership-delegations", async (Guid tenantId, HttpContext context,
    IdentityService identity, CancellationToken cancellationToken) =>
    Results.Ok(await identity.ListOwnershipDelegationsAsync(CurrentUserId(context), tenantId, cancellationToken)))
    .Produces<IReadOnlyList<OwnershipDelegationDto>>().WithTags("Identity");
api.MapPost("/tenants/{tenantId:guid}/ownership-delegations", async (Guid tenantId,
    CreateOwnershipDelegationRequest request, HttpContext context, IdentityService identity,
    CancellationToken cancellationToken) => Results.Ok(await identity.CreateOwnershipDelegationAsync(
        CurrentUserId(context), tenantId, request, cancellationToken)))
    .Produces<OwnershipDelegationDto>().WithTags("Identity");
api.MapDelete("/tenants/{tenantId:guid}/ownership-delegations/{delegationId:guid}", async (Guid tenantId,
    Guid delegationId, HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
{
    await identity.RevokeOwnershipDelegationAsync(CurrentUserId(context), tenantId, delegationId, cancellationToken);
    return Results.NoContent();
}).WithTags("Identity");
api.MapPut("/tenants/{tenantId:guid}/identity-providers/{provider}", async (Guid tenantId, string provider,
    IdentityProviderDto request, HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
    Results.Ok(await identity.ConfigureProviderAsync(CurrentUserId(context), tenantId, request with { Provider = provider }, cancellationToken)))
    .Produces<IdentityProviderDto>().WithTags("Identity");
api.MapGet("/tenants/{tenantId:guid}/identity-providers/{provider}", async (Guid tenantId, string provider,
    HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
    Results.Ok(await identity.GetIdentityProviderAsync(CurrentUserId(context), tenantId, provider, cancellationToken)))
    .Produces<IdentityProviderDto>().WithTags("Identity");
api.MapPost("/tenants/{tenantId:guid}/identity-providers/{provider}/activate", async (Guid tenantId, string provider,
    HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
    Results.Ok(await identity.ActivateProviderAsync(CurrentUserId(context), tenantId, provider, cancellationToken)))
    .Produces<IdentityProviderDto>().WithTags("Identity");
api.MapGet("/tenants/{tenantId:guid}/authentication-policy", async (Guid tenantId,
    HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
    Results.Ok(await identity.GetAuthenticationPolicyAsync(CurrentUserId(context), tenantId, cancellationToken)))
    .Produces<AuthenticationPolicyDto>().WithTags("Identity");
api.MapPut("/tenants/{tenantId:guid}/authentication-policy", async (Guid tenantId, AuthenticationPolicyDto request,
    HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
    Results.Ok(await identity.UpdateAuthenticationPolicyAsync(CurrentUserId(context), tenantId, request, cancellationToken)))
    .Produces<AuthenticationPolicyDto>().WithTags("Identity");
api.MapPut("/tenants/{tenantId:guid}/oidc/group-mapping", async (Guid tenantId, UpdateGroupMappingRequest request,
    HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
{
    await identity.UpdateGroupMappingAsync(CurrentUserId(context), tenantId, request.GroupMappingJson, cancellationToken);
    return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent).WithTags("Identity");
api.MapPost("/tenants/{tenantId:guid}/oidc/group-mapping/refresh", async (Guid tenantId,
    HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
    Results.Ok(await identity.RefreshGroupMappingsAsync(CurrentUserId(context), tenantId, cancellationToken)))
    .Produces<GroupMappingRefreshResult>().WithTags("Identity");
api.MapPost("/tenants/{tenantId:guid}/emergency-access", async (Guid tenantId,
    HttpContext context, IdentityService identity, CancellationToken cancellationToken) =>
    Results.Ok(await identity.ConfigureEmergencyAccessAsync(CurrentUserId(context), tenantId, cancellationToken)))
    .Produces<EmergencyAccessDto>().WithTags("Identity");

api.MapGet("/billing/plans", () => Results.Ok(BillingPlans.All)).Produces<IReadOnlyList<PlanDefinition>>().WithTags("Billing");
api.MapGet("/billing", async (HttpContext context, BillingService billing, CancellationToken cancellationToken) =>
    Results.Ok(await billing.GetSummaryAsync(CurrentUserId(context), Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<BillingSummaryDto>().WithTags("Billing");
api.MapPost("/billing/checkout", async (CheckoutRequest request, HttpContext context, BillingService billing,
    CancellationToken cancellationToken) => Results.Ok(await billing.CheckoutAsync(CurrentUserId(context),
        Guid.Parse(CurrentTenant(context)), request, cancellationToken))).Produces<BillingInvoiceDto>().WithTags("Billing");
api.MapPost("/billing/invoices/{invoiceId:guid}/pay-local", async (Guid invoiceId, LocalPaymentRequest request,
    HttpContext context, BillingService billing, CancellationToken cancellationToken) =>
    Results.Ok(await billing.PayLocalAsync(CurrentUserId(context), Guid.Parse(CurrentTenant(context)),
        invoiceId, request, cancellationToken))).Produces<BillingInvoiceDto>().WithTags("Billing");
api.MapPost("/exports", async (HttpContext context, DataExportService exports, CancellationToken cancellationToken) =>
    Results.Ok(await exports.CreateAsync(CurrentUserId(context), Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<DataExportDto>().WithTags("Data Export");
api.MapGet("/exports", async (HttpContext context, DataExportService exports, CancellationToken cancellationToken) =>
    Results.Ok(await exports.ListAsync(CurrentUserId(context), Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<IReadOnlyList<DataExportDto>>().WithTags("Data Export");
api.MapGet("/exports/{exportId:guid}/content", async (Guid exportId, HttpContext context, DataExportService exports,
    CancellationToken cancellationToken) => Results.File(await exports.DownloadAsync(CurrentUserId(context),
        Guid.Parse(CurrentTenant(context)), exportId, cancellationToken), "application/zip", $"regent-export-{exportId:N}.zip"))
    .Produces(StatusCodes.Status200OK, contentType: "application/zip").WithTags("Data Export");
api.MapPost("/exports/{exportId:guid}/validate", async (Guid exportId, HttpContext context, DataExportService exports,
    CancellationToken cancellationToken) => Results.Ok(await exports.ValidateAsync(CurrentUserId(context),
        Guid.Parse(CurrentTenant(context)), exportId, cancellationToken)))
    .Produces<DataExportValidationDto>().WithTags("Data Export");

api.MapGet("/tenants", async (TenantService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListForUserAsync(cancellationToken))).Produces<IReadOnlyList<TenantDto>>().WithTags("Tenancy");
api.MapPost("/tenants", async (CreateTenantRequest request, TenantService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateAsync(request.Name, request.CompanyName, cancellationToken)))
    .Produces<TenantDto>().WithTags("Tenancy");
api.MapGet("/charter/templates", async (OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListCharterTemplatesAsync(cancellationToken))).Produces<IReadOnlyList<CharterTemplateDto>>().WithTags("Organization");
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
api.MapPut("/positions/{positionId:guid}/status", async (Guid positionId, PositionStatusRequest request,
    OrganizationService service, CancellationToken cancellationToken) => Results.Ok(await service.SetPositionStatusAsync(
        positionId, request.Status, cancellationToken))).Produces<PositionDto>().WithTags("Organization");
api.MapPost("/positions/{sourceId:guid}/merge/{targetId:guid}", async (Guid sourceId, Guid targetId,
    OrganizationService service, CancellationToken cancellationToken) => Results.Ok(await service.MergePositionsAsync(
        sourceId, targetId, cancellationToken))).Produces<PositionDto>().WithTags("Organization");
api.MapPost("/positions/{positionId:guid}/split", async (Guid positionId, SplitPositionRequest request,
    OrganizationService service, CancellationToken cancellationToken) => Results.Ok(await service.SplitPositionAsync(
        positionId, request.Name, request.Responsibilities, cancellationToken))).Produces<PositionDto>().WithTags("Organization");
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
    OrganizationService service, MemoryService memory, RegentDbContext db, HttpContext context,
    CancellationToken cancellationToken) =>
{
    await service.OffboardAsync(employeeId, request.ApprovalId, cancellationToken);
    var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == employeeId, cancellationToken);
    await memory.IsolatePersonalClaimsAsync(CurrentTenant(context), employee.DisplayName, cancellationToken);
    return Results.NoContent();
}).WithTags("Organization");
api.MapPost("/employees/{employeeId:guid}/rehire", async (Guid employeeId, RehireRequest request,
    OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RehireAsync(employeeId, request.PositionId, request.ApprovalId, cancellationToken)))
    .Produces<EmployeeSummary>().WithTags("Organization");
api.MapGet("/candidates", async (OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListCandidatesAsync(cancellationToken))).Produces<IReadOnlyList<CandidateDto>>().WithTags("Recruitment");
api.MapPost("/candidates", async (CreateCandidateRequest request, OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateCandidateAsync(request.PositionId, request.DisplayName, request.ModelVersion,
        request.JobDescription, cancellationToken))).Produces<CandidateDto>().WithTags("Recruitment");
api.MapPost("/candidates/{candidateId:guid}/reject", async (Guid candidateId, RejectCandidateRequest request,
    OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RejectCandidateAsync(candidateId, request.Reason, cancellationToken)))
    .Produces<CandidateDto>().WithTags("Recruitment");
api.MapPost("/candidates/{candidateId:guid}/retry", async (Guid candidateId, OrganizationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.RetryCandidateAsync(candidateId, cancellationToken)))
    .Produces<CandidateDto>().WithTags("Recruitment");
api.MapPost("/candidates/{candidateId:guid}/assessment", async (Guid candidateId, CandidateAssessmentDto assessment,
    OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AssessCandidateAsync(candidateId, assessment, cancellationToken)))
    .Produces<CandidateDto>().WithTags("Recruitment");
api.MapPost("/candidates/{candidateId:guid}/hire", async (Guid candidateId, HireCandidateRequest request,
    OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.HireCandidateAsync(candidateId, request.ApprovalId, cancellationToken)))
    .Produces<EmployeeSummary>().WithTags("Recruitment");
api.MapGet("/recruitment/interviews", async (Guid? candidateId, OrganizationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListInterviewStagesAsync(candidateId, cancellationToken)))
    .Produces<IReadOnlyList<InterviewStageDto>>().WithTags("Recruitment");
api.MapPost("/recruitment/interviews", async (InterviewStageDto stage, OrganizationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.RecordInterviewStageAsync(stage, cancellationToken)))
    .Produces<InterviewStageDto>().WithTags("Recruitment");
api.MapGet("/recruitment/appeals", async (OrganizationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListCandidateAppealsAsync(cancellationToken)))
    .Produces<IReadOnlyList<CandidateAppealDto>>().WithTags("Recruitment");
api.MapPost("/recruitment/appeals", async (CandidateAppealDto appeal, OrganizationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.AppealCandidateAsync(appeal, cancellationToken)))
    .Produces<CandidateAppealDto>().WithTags("Recruitment");
api.MapPost("/recruitment/appeals/{appealId:guid}/resolve", async (Guid appealId, ResolveAppealRequest request,
    OrganizationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ResolveCandidateAppealAsync(appealId, request.Resolution, cancellationToken)))
    .Produces<CandidateAppealDto>().WithTags("Recruitment");
api.MapPost("/recruitment/fairness-audits", async (FairnessAuditRequest request, OrganizationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.RunFairnessAuditAsync(request.PositionId,
        request.Period, request.SampleSize, request.SelectionRateRatio, cancellationToken)))
    .Produces<FairnessAuditDto>().WithTags("Recruitment");
api.MapGet("/recruitment/fairness-audits", async (OrganizationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListFairnessAuditsAsync(cancellationToken)))
    .Produces<IReadOnlyList<FairnessAuditDto>>().WithTags("Recruitment");

api.MapGet("/dashboard/summary", async (DashboardService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(cancellationToken)))
    .Produces<DashboardSummary>(StatusCodes.Status200OK).RequireAuthorization("DashboardRead").WithTags("Dashboard");

api.MapPost("/projects", async (CreateProjectDto input, ProjectService service, BillingService billing,
    ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    await billing.EnsureQuotaAsync(tenantContext.TenantId.Value, "projects", 1, cancellationToken);
    return Results.Ok(await service.CreateAsync(input, cancellationToken));
}).Produces<ProjectDetail>().WithTags("Projects");
api.MapGet("/projects", async (ProjectService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAsync(cancellationToken)))
    .Produces<IReadOnlyList<ProjectSummary>>(StatusCodes.Status200OK).WithTags("Projects");

api.MapGet("/projects/{id:guid}", async (Guid id, ProjectService service, CancellationToken cancellationToken) =>
{
    var project = await service.GetAsync(new ProjectId(id), cancellationToken);
    return project is null ? Results.NotFound() : Results.Ok(project);
}).Produces<ProjectDetail>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).WithTags("Projects");

var delivery = api.MapGroup("/projects/{projectId:guid}/delivery");
delivery.AddEndpointFilter(async (context, next) =>
{
    var projectId = Guid.Parse(context.HttpContext.GetRouteValue("projectId")!.ToString()!);
    var projects = context.HttpContext.RequestServices.GetRequiredService<ProjectService>();
    return await projects.ExistsAsync(new ProjectId(projectId), context.HttpContext.RequestAborted)
        ? await next(context) : Results.NotFound();
});
delivery.MapGet("", async (Guid projectId, HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(Guid.Parse(CurrentTenant(context)), projectId, cancellationToken)))
    .Produces<ProjectDeliveryState>().WithTags("Delivery");
delivery.MapPost("/findings", async (Guid projectId, ResearchFinding finding, HttpContext context,
    DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AddFindingAsync(Guid.Parse(CurrentTenant(context)), projectId, finding, cancellationToken)))
    .Produces<ResearchFinding>().WithTags("Delivery");
delivery.MapPost("/briefs", async (Guid projectId, ProductBrief brief, HttpContext context,
    DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateBriefAsync(Guid.Parse(CurrentTenant(context)), projectId, brief, cancellationToken)))
    .Produces<ProductBrief>().WithTags("Delivery");
delivery.MapPost("/briefs/{briefId:guid}/decision", async (Guid projectId, Guid briefId, DecisionRequest request,
    HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.DecideAsync(Guid.Parse(CurrentTenant(context)), projectId, briefId, request.Decision,
        request.Rationale, request.EvidenceIds, request.Reround, request.Decider, cancellationToken)))
    .Produces<GoNoGoDecision>().WithTags("Delivery");
delivery.MapPost("/requirements", async (Guid projectId, DeliveryRequirement requirement, HttpContext context,
    DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateRequirementAsync(Guid.Parse(CurrentTenant(context)), projectId, requirement, cancellationToken)))
    .Produces<DeliveryRequirement>().WithTags("Delivery");
delivery.MapPost("/requirements/{requirementId:guid}/ready", async (Guid projectId, Guid requirementId,
    ReadyRequirementRequest request, HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ReadyRequirementAsync(Guid.Parse(CurrentTenant(context)), projectId, requirementId,
        request.TotalWeight, cancellationToken))).Produces<DeliveryRequirement>().WithTags("Delivery");
delivery.MapPost("/plans", async (Guid projectId, DecompositionPlan plan, HttpContext context,
    DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ProposePlanAsync(Guid.Parse(CurrentTenant(context)), projectId, plan, cancellationToken)))
    .Produces<DecompositionPlan>().WithTags("Delivery");
delivery.MapPost("/plans/{planId:guid}/approve", async (Guid projectId, Guid planId, ApprovePlanRequest request,
    HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ApprovePlanAsync(Guid.Parse(CurrentTenant(context)), projectId, planId,
        request.Approver, request.ApprovalId, cancellationToken)))
    .Produces<DecompositionPlan>().WithTags("Delivery");
delivery.MapPost("/tasks/{taskId:guid}/claim", async (Guid projectId, Guid taskId, ClaimTaskRequest request,
    HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ClaimTaskAsync(Guid.Parse(CurrentTenant(context)), projectId, taskId, request.Assignee, cancellationToken)))
    .Produces<DeliveryTask>().WithTags("Delivery");
delivery.MapPost("/tasks/{taskId:guid}/submit", async (Guid projectId, Guid taskId, SubmitTaskRequest request,
    HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.SubmitTaskAsync(Guid.Parse(CurrentTenant(context)), projectId, taskId, request.Author, cancellationToken)))
    .Produces<DeliveryTask>().WithTags("Delivery");
delivery.MapPost("/tasks/{taskId:guid}/complete", async (Guid projectId, Guid taskId, DoneEvidence evidence,
    HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CompleteTaskAsync(Guid.Parse(CurrentTenant(context)), projectId, taskId, evidence, cancellationToken)))
    .Produces<DoneEvidence>().WithTags("Delivery");
delivery.MapPost("/defects", async (Guid projectId, Defect defect, HttpContext context,
    DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ReportDefectAsync(Guid.Parse(CurrentTenant(context)), projectId, defect, cancellationToken)))
    .Produces<Defect>().WithTags("Delivery");
delivery.MapPost("/defects/{defectId:guid}/fix", async (Guid projectId, Guid defectId, FixDefectRequest request,
    HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.FixDefectAsync(Guid.Parse(CurrentTenant(context)), projectId, defectId,
        request.Fixer, request.Evidence, cancellationToken))).Produces<Defect>().WithTags("Delivery");
delivery.MapPost("/defects/{defectId:guid}/retest", async (Guid projectId, Guid defectId, RetestDefectRequest request,
    HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RetestDefectAsync(Guid.Parse(CurrentTenant(context)), projectId, defectId,
        request.Passed, request.Retester, cancellationToken))).Produces<Defect>().WithTags("Delivery");
delivery.MapPost("/reviews", async (Guid projectId, Review review, HttpContext context,
    DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AddReviewAsync(Guid.Parse(CurrentTenant(context)), projectId, review, cancellationToken)))
    .Produces<Review>().WithTags("Delivery");
delivery.MapPost("/acceptance-reports", async (Guid projectId, AcceptanceReport report, HttpContext context,
    DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AddAcceptanceReportAsync(Guid.Parse(CurrentTenant(context)), projectId, report, cancellationToken)))
    .Produces<AcceptanceReport>().WithTags("Delivery");
delivery.MapPost("/releases", async (Guid projectId, ReleaseRequest request, HttpContext context,
    DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ReleaseAsync(Guid.Parse(CurrentTenant(context)), projectId, request.Version,
        request.ArtifactRef, cancellationToken))).Produces<ReleaseRecord>().WithTags("Delivery");
delivery.MapPost("/releases/{releaseId:guid}/observe", async (Guid projectId, Guid releaseId, ObserveReleaseRequest request,
    HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ObserveReleaseAsync(Guid.Parse(CurrentTenant(context)), projectId, releaseId,
        request.Healthy, request.MetricsJson, cancellationToken))).Produces<ReleaseRecord>().WithTags("Delivery");
delivery.MapPost("/terminate", async (Guid projectId, Retrospective retrospective, HttpContext context,
    DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.TerminateProjectAsync(Guid.Parse(CurrentTenant(context)), projectId, retrospective, cancellationToken)))
    .Produces<Retrospective>().WithTags("Delivery");
delivery.MapPost("/releases/{releaseId:guid}/rollback", async (Guid projectId, Guid releaseId,
    RollbackRequest request, HttpContext context, DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RollbackAsync(Guid.Parse(CurrentTenant(context)), projectId, releaseId,
        request.TargetVersion, cancellationToken))).Produces<ReleaseRecord>().WithTags("Delivery");
delivery.MapPost("/retrospectives", async (Guid projectId, Retrospective retrospective, HttpContext context,
    DeliveryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AddRetrospectiveAsync(Guid.Parse(CurrentTenant(context)), projectId, retrospective, cancellationToken)))
    .Produces<Retrospective>().WithTags("Delivery");

var governance = api.MapGroup("/governance");
governance.MapGet("", async (HttpContext context, GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<GovernanceState>().WithTags("Governance");
governance.MapPost("/tools", async (ToolManifest tool, HttpContext context, GovernanceService service,
    CancellationToken cancellationToken) => Results.Ok(await service.RegisterToolAsync(Guid.Parse(CurrentTenant(context)), tool, cancellationToken)))
    .Produces<ToolManifest>().WithTags("Governance");
governance.MapPost("/tools/{toolId:guid}/admit", async (Guid toolId, AdmitToolRequest request,
    HttpContext context, GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AdmitToolAsync(Guid.Parse(CurrentTenant(context)), toolId, request.Reviewer, cancellationToken)))
    .Produces<ToolAdmissionReview>().WithTags("Governance");
governance.MapDelete("/tools/{toolId:guid}", async (Guid toolId, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RevokeToolAsync(Guid.Parse(CurrentTenant(context)), toolId, cancellationToken)))
    .Produces<ToolManifest>().WithTags("Governance");
governance.MapPost("/tool-calls/authorize", async (ToolAuthorizationRequest request, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(CurrentTenant(context));
    var result = await service.AuthorizeToolCallAsync(tenantId, request, cancellationToken);
    await service.RecordToolUsageAsync(tenantId, request, result, cancellationToken);
    return Results.Ok(result);
}).Produces<ToolAuthorizationResult>().WithTags("Governance");
governance.MapGet("/tools/{toolId:guid}/impact", async (Guid toolId, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.GetToolImpactAsync(
        Guid.Parse(CurrentTenant(context)), toolId, cancellationToken))).Produces<ToolRevocationImpact>().WithTags("Governance");
governance.MapPost("/injections/scan", async (ScanContentRequest request, GovernanceService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ScanInjectionAsync(request.Content, cancellationToken)))
    .Produces<InjectionScanResult>().WithTags("Governance");
governance.MapPost("/dlp/redact", async (RedactContentRequest request, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RedactAsync(Guid.Parse(CurrentTenant(context)), request.Content, cancellationToken)))
    .Produces<DlpRedactionResult>().WithTags("Governance");
governance.MapPost("/dlp/delete", async (DeleteDlpTokensRequest request, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) =>
{
    await service.DeleteDlpTokensAsync(Guid.Parse(CurrentTenant(context)), request.Tokens, cancellationToken);
    return Results.NoContent();
}).WithTags("Governance");
governance.MapPost("/content-safety/review", async (ReviewContentRequest request, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ReviewContentAsync(Guid.Parse(CurrentTenant(context)), request.ContentRef, request.Content, cancellationToken)))
    .Produces<ContentSafetyFinding>().WithTags("Governance");
governance.MapPost("/content-safety/appeals", async (ContentAppealRequest request, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AppealAsync(Guid.Parse(CurrentTenant(context)), request, cancellationToken)))
    .Produces<ContentSafetyFinding>().WithTags("Governance");
governance.MapPost("/content-safety/findings/{findingId:guid}/resolve", async (Guid findingId, bool upheld,
    HttpContext context, GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ResolveAppealAsync(Guid.Parse(CurrentTenant(context)), findingId, upheld, cancellationToken)))
    .Produces<ContentSafetyFinding>().WithTags("Governance");
governance.MapGet("/incidents", async (HttpContext context, GovernanceService service,
    CancellationToken cancellationToken) => Results.Ok((await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)).Incidents))
    .Produces<IReadOnlyList<SecurityIncident>>().WithTags("Governance");
governance.MapPost("/incidents", async (SecurityIncident incident, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateIncidentAsync(Guid.Parse(CurrentTenant(context)), incident, cancellationToken)))
    .Produces<SecurityIncident>().WithTags("Governance");
governance.MapPost("/incidents/{incidentId:guid}/contain", async (Guid incidentId, ContainIncidentRequest request,
    HttpContext context, GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ContainIncidentAsync(Guid.Parse(CurrentTenant(context)), incidentId, request.Action, cancellationToken)))
    .Produces<SecurityIncident>().WithTags("Governance");
governance.MapPost("/incidents/{incidentId:guid}/recover", async (Guid incidentId, RecoverIncidentRequest request,
    HttpContext context, GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RecoverIncidentAsync(Guid.Parse(CurrentTenant(context)), incidentId, request.Summary, cancellationToken)))
    .Produces<SecurityIncident>().WithTags("Governance");
governance.MapPost("/incidents/{incidentId:guid}/evidence", async (Guid incidentId, SealEvidenceRequest request,
    HttpContext context, GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.SealIncidentEvidenceAsync(Guid.Parse(CurrentTenant(context)), incidentId,
        request.Evidence, cancellationToken))).Produces<SecurityIncident>().WithTags("Governance");
governance.MapGet("/incidents/{incidentId:guid}/evidence/verify", async (Guid incidentId, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.VerifyIncidentEvidenceAsync(
        Guid.Parse(CurrentTenant(context)), incidentId, cancellationToken))).Produces<EvidenceVerificationDto>().WithTags("Governance");
governance.MapPost("/incidents/{incidentId:guid}/close", async (Guid incidentId, CloseIncidentRequest request,
    HttpContext context, GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CloseIncidentAsync(Guid.Parse(CurrentTenant(context)), incidentId, request.Retrospective, cancellationToken)))
    .Produces<SecurityIncident>().WithTags("Governance");
governance.MapGet("/approval-delegations", async (HttpContext context, GovernanceService service,
    CancellationToken cancellationToken) => Results.Ok((await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)).ApprovalDelegations))
    .Produces<IReadOnlyList<ApprovalDelegation>>().WithTags("Governance");
governance.MapPost("/approval-delegations", async (ApprovalDelegation delegation, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateApprovalDelegationAsync(Guid.Parse(CurrentTenant(context)), delegation, cancellationToken)))
    .Produces<ApprovalDelegation>().WithTags("Governance");
governance.MapDelete("/approval-delegations/{id:guid}", async (Guid id, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) =>
    {
        await service.RevokeApprovalDelegationAsync(Guid.Parse(CurrentTenant(context)), id, cancellationToken);
        return Results.NoContent();
    }).WithTags("Governance");
governance.MapGet("/approval-delegations/chain", async (string primary, string action, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.ResolveApprovalChainAsync(
        Guid.Parse(CurrentTenant(context)), primary, action, cancellationToken))).Produces<ApprovalChainDto>().WithTags("Governance");
governance.MapGet("/approval-delegations/resolve", async (string primary, string action, HttpContext context,
    GovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(new { approver = await service.ResolveApproverAsync(Guid.Parse(CurrentTenant(context)), primary, action, cancellationToken) }))
    .Produces<ApprovalResolutionDto>().WithTags("Governance");

var lifecycle = api.MapGroup("/lifecycle");
lifecycle.MapGet("", async (HttpContext context, DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<DataLifecycleState>().WithTags("Lifecycle");
lifecycle.MapPost("/deletions", async (SoftDeleteRequest request, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.SoftDeleteAsync(Guid.Parse(CurrentTenant(context)), request.ResourceType,
        request.ResourceId, request.Reason, cancellationToken))).Produces<DeletionRequestRecord>().WithTags("Lifecycle");
lifecycle.MapPost("/deletions/{requestId:guid}/restore", async (Guid requestId, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RestoreAsync(Guid.Parse(CurrentTenant(context)), requestId, cancellationToken)))
    .Produces<DeletionRequestRecord>().WithTags("Lifecycle");
lifecycle.MapPost("/deletions/{requestId:guid}/purge", async (Guid requestId, PurgeRequest request,
    HttpContext context, DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.PurgeAsync(Guid.Parse(CurrentTenant(context)), requestId, request.Confirmation, cancellationToken)))
    .Produces<DeletionProof>().WithTags("Lifecycle");
lifecycle.MapGet("/legal-holds", async (HttpContext context, DataLifecycleService service,
    CancellationToken cancellationToken) => Results.Ok((await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)).LegalHolds))
    .Produces<IReadOnlyList<LegalHoldRecord>>().WithTags("Lifecycle");
lifecycle.MapPost("/legal-holds", async (LegalHoldRecord hold, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateLegalHoldAsync(Guid.Parse(CurrentTenant(context)), hold, cancellationToken)))
    .Produces<LegalHoldRecord>().WithTags("Lifecycle");
lifecycle.MapPost("/legal-holds/{holdId:guid}/release", async (Guid holdId, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ReleaseLegalHoldAsync(Guid.Parse(CurrentTenant(context)), holdId, cancellationToken)))
    .Produces<LegalHoldRecord>().WithTags("Lifecycle");
lifecycle.MapGet("/retention-policies", async (HttpContext context, DataLifecycleService service,
    CancellationToken cancellationToken) => Results.Ok((await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)).RetentionPolicies))
    .Produces<IReadOnlyList<RetentionPolicyRecord>>().WithTags("Lifecycle");
lifecycle.MapPost("/retention-policies", async (RetentionPolicyRecord policy, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateRetentionPolicyAsync(Guid.Parse(CurrentTenant(context)), policy, cancellationToken)))
    .Produces<RetentionPolicyRecord>().WithTags("Lifecycle");
lifecycle.MapPost("/archives", async (ArchiveRequest request, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ArchiveAsync(Guid.Parse(CurrentTenant(context)), request.Period, request.Tier,
        request.RecordCount, cancellationToken))).Produces<ArchiveManifest>().WithTags("Lifecycle");
lifecycle.MapPost("/archives/{archiveId:guid}/restore", async (Guid archiveId, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RestoreArchiveAsync(Guid.Parse(CurrentTenant(context)), archiveId, cancellationToken)))
    .Produces<ArchiveManifest>().WithTags("Lifecycle");
lifecycle.MapPost("/backups", async (BackupRequest request, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.BackupAsync(Guid.Parse(CurrentTenant(context)), request.Profile,
        request.RpoMinutes, request.RtoMinutes, cancellationToken))).Produces<BackupRunRecord>().WithTags("Lifecycle");
lifecycle.MapPost("/backups/{backupId:guid}/verify-restore", async (Guid backupId, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.VerifyRestoreAsync(Guid.Parse(CurrentTenant(context)), backupId, cancellationToken)))
    .Produces<BackupRunRecord>().WithTags("Lifecycle");
lifecycle.MapPost("/rights", async (RightsRecord rights, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RecordRightsAsync(Guid.Parse(CurrentTenant(context)), rights, cancellationToken)))
    .Produces<RightsRecord>().WithTags("Lifecycle");
lifecycle.MapPost("/licenses", async (LicenseRecord license, HttpContext context,
    DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RecordLicenseAsync(Guid.Parse(CurrentTenant(context)), license, cancellationToken)))
    .Produces<LicenseRecord>().WithTags("Lifecycle");
lifecycle.MapPost("/run-due", async (DataLifecycleService service, CancellationToken cancellationToken) =>
    Results.Ok(new { purged = await service.RunDuePurgeAsync(cancellationToken) })).WithTags("Lifecycle");
lifecycle.MapGet("/evaluation", async (HttpContext context, DataLifecycleService service,
    CancellationToken cancellationToken) => Results.Ok(await service.EvaluateAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<LifecycleEvaluation>().WithTags("Lifecycle");

var modelApi = api.MapGroup("/models");
modelApi.MapGet("/pool", async (HttpContext context, ModelOperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken))).Produces<ModelPoolState>().WithTags("Models");
modelApi.MapPost("/catalog", async (ModelCatalogEntry entry, HttpContext context,
    ModelOperationsService service, CancellationToken cancellationToken) => Results.Ok(await service.AddModelAsync(
        Guid.Parse(CurrentTenant(context)), entry, cancellationToken))).Produces<ModelCatalogEntry>().WithTags("Models");
modelApi.MapPut("/catalog/{provider}/{modelId}/rotate", async (string provider, string modelId, RotateModelRequest request,
    HttpContext context, ModelOperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RotateModelAsync(Guid.Parse(CurrentTenant(context)), provider, modelId,
        request.Version, request.Protocol, cancellationToken))).Produces<ModelCatalogEntry>().WithTags("Models");
modelApi.MapDelete("/catalog/{provider}/{modelId}", async (string provider, string modelId,
    HttpContext context, ModelOperationsService service, CancellationToken cancellationToken) =>
{
    await service.DeleteModelAsync(Guid.Parse(CurrentTenant(context)), provider, modelId, cancellationToken);
    return Results.NoContent();
}).WithTags("Models");
modelApi.MapPost("/snapshots/refresh", async (RefreshSnapshotRequest request, HttpContext context,
    ModelOperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RefreshSnapshotAsync(Guid.Parse(CurrentTenant(context)), request.Date, cancellationToken)))
    .Produces<ModelPoolState>().WithTags("Models");
modelApi.MapPost("/prices", async (ManualModelPrice price, HttpContext context, ModelOperationsService service,
    CancellationToken cancellationToken) => Results.Ok(await service.SetManualPriceAsync(Guid.Parse(CurrentTenant(context)), price, cancellationToken)))
    .Produces<ManualModelPrice>().WithTags("Models");
modelApi.MapPost("/tiers", async (ModelTierAssignment assignment, HttpContext context, ModelOperationsService service,
    CancellationToken cancellationToken) => Results.Ok(await service.AssignTierAsync(Guid.Parse(CurrentTenant(context)), assignment, cancellationToken)))
    .Produces<ModelTierAssignment>().WithTags("Models");
modelApi.MapPost("/bindings", async (EmployeeModelBinding binding, HttpContext context, ModelOperationsService service,
    CancellationToken cancellationToken) => Results.Ok(await service.BindEmployeeAsync(Guid.Parse(CurrentTenant(context)), binding, cancellationToken)))
    .Produces<EmployeeModelBinding>().WithTags("Models");
modelApi.MapPost("/bindings/{employeeId:guid}/restore", async (Guid employeeId, EmployeeModelBinding checkpoint,
    HttpContext context, ModelOperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RestoreBindingAsync(Guid.Parse(CurrentTenant(context)), employeeId, checkpoint, cancellationToken)))
    .Produces<EmployeeModelBinding>().WithTags("Models");
modelApi.MapPost("/unavailable", async (ModelUnavailableRequest request, HttpContext context, ModelOperationsService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ReportUnavailableAsync(Guid.Parse(CurrentTenant(context)),
        request.EmployeeId, request.Provider, request.ModelId, request.ConsecutiveFailures, request.UnavailableMinutes, cancellationToken)))
    .Produces<ModelUnavailableEvent>().WithTags("Models");
modelApi.MapPost("/test", async (TestModelRequest request, HttpContext context, ModelOperationsService service,
    CancellationToken cancellationToken) => Results.Ok(await service.TestModelAsync(Guid.Parse(CurrentTenant(context)),
        request.Provider, request.ModelId, cancellationToken))).Produces<ModelTestResult>().WithTags("Models");

var policyApi = api.MapGroup("/runtime-policy");
policyApi.MapGet("", async (HttpContext context, RuntimePolicyService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken))).Produces<RuntimePolicyState>().WithTags("Runtime Policy");
policyApi.MapPost("/costs", async (CostEntry cost, HttpContext context, RuntimePolicyService service,
    CancellationToken cancellationToken) => Results.Ok(await service.RecordCostAsync(Guid.Parse(CurrentTenant(context)), cost, cancellationToken)))
    .Produces<CostEntry>().WithTags("Runtime Policy");
policyApi.MapPost("/costs/{costId:guid}/corrections", async (Guid costId, CorrectCostRequest request,
    HttpContext context, RuntimePolicyService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CorrectCostAsync(Guid.Parse(CurrentTenant(context)), costId, request.Delta, request.Reason, cancellationToken)))
    .Produces<CostEntry>().WithTags("Runtime Policy");
policyApi.MapGet("/costs/summary", async (Guid? projectId, HttpContext context, RuntimePolicyService service,
    CancellationToken cancellationToken) => Results.Ok(await service.SummaryAsync(Guid.Parse(CurrentTenant(context)), projectId, cancellationToken)))
    .Produces<CostSummaryDto>().WithTags("Runtime Policy");
policyApi.MapPost("/budgets", async (BudgetPolicy budget, HttpContext context, RuntimePolicyService service,
    CancellationToken cancellationToken) => Results.Ok(await service.SetBudgetAsync(Guid.Parse(CurrentTenant(context)), budget, cancellationToken)))
    .Produces<BudgetPolicy>().WithTags("Runtime Policy");
policyApi.MapPost("/allocation-rules", async (AllocationRule rule, HttpContext context, RuntimePolicyService service,
    CancellationToken cancellationToken) => Results.Ok(await service.AddAllocationRuleAsync(Guid.Parse(CurrentTenant(context)), rule, cancellationToken)))
    .Produces<AllocationRule>().WithTags("Runtime Policy");
policyApi.MapPut("/schedule", async (WorkSchedule schedule, HttpContext context, RuntimePolicyService service,
    CancellationToken cancellationToken) => Results.Ok(await service.UpdateScheduleAsync(Guid.Parse(CurrentTenant(context)), schedule, cancellationToken)))
    .Produces<WorkSchedule>().WithTags("Runtime Policy");
policyApi.MapPost("/dispatch", async (DispatchRequest request, HttpContext context, RuntimePolicyService service,
    CancellationToken cancellationToken) => Results.Ok(await service.DecideDispatchAsync(Guid.Parse(CurrentTenant(context)), request.Now, request.Emergency, cancellationToken)))
    .Produces<DispatchDecision>().WithTags("Runtime Policy");
policyApi.MapPut("/capacity", async (CapacityPolicy capacity, HttpContext context, RuntimePolicyService service,
    CancellationToken cancellationToken) => Results.Ok(await service.UpdateCapacityAsync(Guid.Parse(CurrentTenant(context)), capacity, cancellationToken)))
    .Produces<CapacityPolicy>().WithTags("Runtime Policy");
policyApi.MapGet("/capacity/admission", async (int queueDepth, HttpContext context, RuntimePolicyService service,
    CancellationToken cancellationToken) => Results.Ok(await service.AdmitQueueItemAsync(Guid.Parse(CurrentTenant(context)), queueDepth, cancellationToken)))
    .Produces<CapacityDecision>().WithTags("Runtime Policy");

api.MapPost("/projects/{id:guid}/transitions", async (Guid id, ProjectTransitionDto transition,
    ProjectService service, CancellationToken cancellationToken) =>
{
    var project = await service.TransitionAsync(new ProjectId(id), transition, cancellationToken);
    return project is null ? Results.NotFound() : Results.Ok(project);
}).Produces<ProjectDetail>().WithTags("Projects");

api.MapGet("/employees", async (EmployeeService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAsync(cancellationToken)))
    .Produces<IReadOnlyList<EmployeeSummary>>(StatusCodes.Status200OK).RequireAuthorization("EmployeesRead").WithTags("Organization");

api.MapGet("/employees/{id:guid}/timeline", async (Guid id, EmployeeService service, CancellationToken cancellationToken) =>
{
    var timeline = await service.GetTimelineAsync(new EmployeeId(id), cancellationToken);
    return timeline is null ? Results.NotFound() : Results.Ok(timeline);
}).Produces<EmployeeTimeline>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).WithTags("Organization");

api.MapGet("/outbox", async (HttpContext context, OutboxService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<IReadOnlyList<OutboxRecord>>().WithTags("Messaging");
api.MapPost("/outbox", async (PublishEventRequest request, HttpContext context,
    OutboxService service, CancellationToken cancellationToken) => Results.Ok(await service.PublishAsync(
        Guid.Parse(CurrentTenant(context)), request.EventType, request.Payload, cancellationToken)))
    .Produces<OutboxRecord>().WithTags("Messaging");
api.MapPost("/outbox/process", async (OutboxService service, CancellationToken cancellationToken) =>
    Results.Ok(new { processed = await service.ProcessAsync(cancellationToken) })).WithTags("Messaging");
api.MapGet("/schema/version", async (SchemaVersionService service, CancellationToken cancellationToken) =>
    Results.Ok(new { version = SchemaVersionService.CurrentVersion })).WithTags("System");

api.MapPost("/approvals", async (CreateApprovalDto input, ApprovalService service,
    CancellationToken cancellationToken) => Results.Ok(await service.CreateAsync(input, cancellationToken)))
    .Produces<ApprovalDto>().WithTags("Approvals");
api.MapPost("/approvals/expire-due", async (ApprovalExpiryService service, CancellationToken cancellationToken) =>
    Results.Ok(new { expired = await service.ExpireDueAsync(cancellationToken) })).WithTags("Approvals");
api.MapGet("/audit/verify", async (HttpContext context, AuditIntegrityService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.VerifyAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<AuditVerificationDto>().WithTags("Audit");
api.MapGet("/audit/anchors/verify", async (HttpContext context, AuditIntegrityService service,
    CancellationToken cancellationToken) => Results.Ok(await service.VerifyAnchorsAsync(
        Guid.Parse(CurrentTenant(context)), cancellationToken))).Produces<AuditVerificationDto>().WithTags("Audit");
api.MapPost("/audit/checkpoints", async (AuditCheckpointRequest request, HttpContext context,
    AuditIntegrityService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CheckpointAsync(Guid.Parse(CurrentTenant(context)), request.Profile, cancellationToken)))
    .Produces<AuditCheckpointDto>().WithTags("Audit");

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

    var result = await service.DecideAsync(new ApprovalId(id), decision, cancellationToken, key.ToString());
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

api.MapGet("/localization", async (HttpContext context, LocalizationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.GetAsync(CurrentTenant(context), cancellationToken)))
    .Produces<LocalizationSettings>().WithTags("Localization");
api.MapPut("/localization", async (LocalizationSettings settings, HttpContext context,
    LocalizationService service, CancellationToken cancellationToken) => Results.Ok(await service.UpdateAsync(
        CurrentTenant(context), settings, cancellationToken))).Produces<LocalizationSettings>().WithTags("Localization");
api.MapGet("/compliance/profile", async (HttpContext context, ComplianceService service,
    CancellationToken cancellationToken) => Results.Ok(await service.GetAsync(CurrentTenant(context), cancellationToken)))
    .WithTags("Compliance");
api.MapPut("/compliance/profile", async (ComplianceProfile profile, HttpContext context,
    ComplianceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.UpdateAsync(CurrentTenant(context), profile, cancellationToken))).WithTags("Compliance");

api.MapGet("/workflows", async (HttpContext context, WorkflowService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAsync(CurrentTenant(context), cancellationToken))).Produces<IReadOnlyList<WorkflowDefinition>>().WithTags("Workflows");
api.MapPost("/workflows", async (CreateWorkflowRequest request, HttpContext context, WorkflowService service,
    AuditIntegrityService audit, CancellationToken cancellationToken) =>
{
    var workflow = await service.CaptureAsync(CurrentTenant(context), request.Name, request.Steps,
        CurrentUser(context), request.ScopePositionId, cancellationToken);
    await audit.AppendAsync(Guid.Parse(CurrentTenant(context)), "workflow.captured", CurrentUser(context),
        new { workflow_id = workflow.Id, version = workflow.Version, scope_position_id = workflow.ScopePositionId }, cancellationToken);
    return Results.Ok(workflow);
}).Produces<WorkflowDefinition>().WithTags("Workflows");
api.MapPost("/workflows/{id}/nominate", async (string id, HttpContext context, WorkflowService service,
    AuditIntegrityService audit, CancellationToken cancellationToken) =>
{
    var workflow = await service.NominateAsync(id, CurrentTenant(context), cancellationToken);
    await audit.AppendAsync(Guid.Parse(CurrentTenant(context)), "workflow.nominated", CurrentUser(context),
        new { workflow_id = workflow.Id, parent_workflow_id = workflow.ParentWorkflowId, version = workflow.Version }, cancellationToken);
    return Results.Ok(workflow);
}).Produces<WorkflowDefinition>().WithTags("Workflows");
api.MapPost("/workflows/{id}/review", async (string id, ReviewWorkflowRequest request, HttpContext context,
    WorkflowService service, AuditIntegrityService audit, CancellationToken cancellationToken) =>
{
    var workflow = await service.ReviewAsync(id, CurrentTenant(context), request.Decision, request.Reviewer,
        request.Note, cancellationToken);
    await audit.AppendAsync(Guid.Parse(CurrentTenant(context)), "workflow.reviewed", CurrentUser(context),
        new { workflow_id = workflow.Id, decision = request.Decision, reviewer_id = request.Reviewer }, cancellationToken);
    return Results.Ok(workflow);
}).Produces<WorkflowDefinition>().WithTags("Workflows");
api.MapPost("/workflows/{id}/runs", async (string id, HttpContext context, WorkflowService service,
    CancellationToken cancellationToken) => Results.Ok(await service.StartAsync(id, CurrentTenant(context), cancellationToken)))
    .WithTags("Workflows");
api.MapPost("/workflow-runs/{id}/advance", async (string id, WorkflowService service,
    CancellationToken cancellationToken) => Results.Ok(await service.AdvanceAsync(id, cancellationToken))).WithTags("Workflows");

api.MapGet("/memories", async (string? query, HttpContext context, MemoryService service,
    CancellationToken cancellationToken) => Results.Ok(await service.SearchAsync(CurrentTenant(context), query, CurrentUser(context), cancellationToken)))
    .Produces<IReadOnlyList<MemoryClaim>>().WithTags("Memory");
api.MapPost("/memories", async (MemoryCaptureRequest request, HttpContext context, MemoryService service,
    MemorySafetyService safety, MemoryEventService events, CancellationToken cancellationToken) =>
{
    var tenantId = CurrentTenant(context);
    var claim = await service.CaptureAsync(tenantId, request.Scope, request.Content, request.SourceRef,
        request.Confidence, request.Taint, CurrentUser(context), cancellationToken, requireReview: request.Scope != "personal");
    var findings = await safety.ListClaimFindingsAsync(claim.Id, tenantId, cancellationToken);
    await events.RecordAsync(Guid.Parse(tenantId), CurrentUserId(context), CurrentUser(context), "memory.claim.captured",
        new { claim_id = claim.Id, scope = claim.Scope, version = claim.Version, taint = claim.Taint, finding_count = findings.Count },
        cancellationToken: cancellationToken);
    if (findings.Any(x => x.RiskLevel == "High"))
        await events.RecordAsync(Guid.Parse(tenantId), CurrentUserId(context), CurrentUser(context), "memory.finding.detected",
            new { claim_id = claim.Id, finding_ids = findings.Where(x => x.RiskLevel == "High").Select(x => x.Id).ToArray() },
            "High-risk memory claim quarantined", "High", cancellationToken);
    return Results.Ok(claim);
}).Produces<MemoryClaim>().WithTags("Memory");
api.MapGet("/memories/search", async (string query, string? scope, int? maxResults, HttpContext context,
    MemoryIndexService index, CancellationToken cancellationToken) => Results.Ok(await index.SearchHybridAsync(
        CurrentTenant(context), query, scope, maxResults ?? 10, CurrentUser(context), cancellationToken)))
    .Produces<MemorySearchResult>().WithTags("Memory");
api.MapGet("/memories/{id}", async (string id, HttpContext context, MemoryService service,
    CancellationToken cancellationToken) =>
    {
        var claim = await service.GetAsync(id, CurrentTenant(context), CurrentUser(context), cancellationToken);
        return claim is null ? Results.NotFound() : Results.Ok(claim);
    })
    .Produces<MemoryClaim>().Produces(StatusCodes.Status404NotFound).WithTags("Memory");
api.MapPost("/memory/contexts", async (MemoryContextRequest request, HttpContext context,
    MemorySafetyService safety, CancellationToken cancellationToken) =>
    Results.Ok(await safety.BindContextAsync(request, CurrentTenant(context), cancellationToken)))
    .Produces<MemoryContextBinding>().WithTags("Memory");
api.MapGet("/memory/tasks/{taskId}/context", async (string taskId, HttpContext context,
    MemorySafetyService safety, CancellationToken cancellationToken) =>
    await safety.GetContextAsync(taskId, CurrentTenant(context), cancellationToken) is { } binding
        ? Results.Ok(binding) : Results.NotFound()).Produces<MemoryContextBinding>().WithTags("Memory");
api.MapGet("/memory/contexts/stale", async (HttpContext context, MemorySafetyService safety,
    CancellationToken cancellationToken) => Results.Ok(await safety.ListStaleContextsAsync(CurrentTenant(context), cancellationToken)))
    .Produces<IReadOnlyList<MemoryContextBinding>>().WithTags("Memory");
api.MapGet("/memory/review-queue", async (HttpContext context, MemoryService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListReviewQueueAsync(CurrentTenant(context), cancellationToken)))
    .Produces<IReadOnlyList<MemoryClaim>>().WithTags("Memory");
api.MapPost("/memory/claims/{id}/nominate", async (string id, MemoryNominationRequest request,
    HttpContext context, MemoryService memory, MemorySafetyService safety, MemoryEventService events,
    CancellationToken cancellationToken) =>
{
    if (await memory.GetAsync(id, CurrentTenant(context), CurrentUser(context), cancellationToken) is null)
        return Results.NotFound();
    var candidate = await safety.NominateAsync(id, CurrentTenant(context), request, cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.claim.nominated", new { source_claim_id = id, candidate_claim_id = candidate.Id, scope_position_id = request.ScopePositionId },
        cancellationToken: cancellationToken);
    return Results.Ok(candidate);
}).Produces<MemoryClaim>().WithTags("Memory");
api.MapPost("/memory/claims/{id}/publish", async (string id, MemoryPublishRequest request,
    HttpContext context, MemorySafetyService safety, MemoryEventService events, CancellationToken cancellationToken) =>
{
    var claim = await safety.PublishAsync(id, CurrentTenant(context), request, cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.claim.publication_reviewed", new { claim_id = id, decision = request.Decision, reviewer_id = request.ReviewerId },
        cancellationToken: cancellationToken);
    return Results.Ok(claim);
}).Produces<MemoryClaim>().RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapPost("/memories/{id}/archive", async (string id, HttpContext context, MemoryService service,
    MemoryEventService events, CancellationToken cancellationToken) =>
{
    await service.ArchiveAsync(id, CurrentTenant(context), cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.claim.archived", new { claim_id = id }, cancellationToken: cancellationToken);
    return Results.NoContent();
}).RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapPost("/memory/index/rebuild", async (HttpContext context, MemoryIndexService index,
    MemoryEventService events, CancellationToken cancellationToken) =>
{
    var indexed = await index.RebuildAsync(CurrentTenant(context), cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.index.rebuilt", new { indexed }, cancellationToken: cancellationToken);
    return Results.Ok(new { indexed });
}).RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapGet("/memory/findings", async (HttpContext context, MemorySafetyService safety,
    CancellationToken cancellationToken) => Results.Ok(await safety.ListFindingsAsync(CurrentTenant(context), cancellationToken)))
    .Produces<IReadOnlyList<MemoryFinding>>().WithTags("Memory");
api.MapPost("/memory/findings", async (MemoryDetectRequest request, HttpContext context,
    MemorySafetyService safety, MemoryEventService events, CancellationToken cancellationToken) =>
{
    var detected = await safety.DetectAsync(request.ClaimId, CurrentTenant(context), cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.finding.detected", new { claim_id = request.ClaimId, finding_ids = detected.Select(x => x.Id).ToArray() },
        detected.Any(x => x.RiskLevel == "High") ? "High-risk memory claim quarantined" : null,
        "High", cancellationToken);
    return Results.Ok(detected);
}).Produces<IReadOnlyList<MemoryFinding>>().WithTags("Memory");
api.MapPost("/memory/findings/{id}/quarantine", async (string id, MemoryQuarantineRequest request,
    HttpContext context, MemorySafetyService safety, MemoryEventService events, CancellationToken cancellationToken) =>
{
    var finding = await safety.QuarantineAsync(id, CurrentTenant(context), request, cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.claim.quarantined", new { finding_id = id, claim_id = finding.ClaimId, reviewer_id = request.ReviewerId, scope = request.Scope },
        "Memory claim quarantined for independent review", "High", cancellationToken);
    return Results.Ok(finding);
}).Produces<MemoryFinding>().RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapPost("/memory/findings/{id}/appeal", async (string id, MemoryAppealRequest request,
    HttpContext context, MemorySafetyService safety, MemoryEventService events, CancellationToken cancellationToken) =>
{
    var finding = await safety.AppealAsync(id, CurrentTenant(context), request, cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.finding.appealed", new { finding_id = id, claim_id = finding.ClaimId, submitted_by = request.SubmittedBy },
        cancellationToken: cancellationToken);
    return Results.Ok(finding);
}).Produces<MemoryFinding>().WithTags("Memory");
api.MapPost("/memory/findings/{id}/review", async (string id, MemoryReviewRequest request,
    HttpContext context, MemorySafetyService safety, MemoryEventService events, CancellationToken cancellationToken) =>
{
    var finding = await safety.ReviewAppealAsync(id, CurrentTenant(context), request, cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.finding.reviewed", new { finding_id = id, claim_id = finding.ClaimId, reviewer_id = request.ReviewerId, released = request.Release },
        "Memory quarantine appeal reviewed", "Medium", cancellationToken);
    return Results.Ok(finding);
}).Produces<MemoryFinding>().RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapGet("/memory/corrections", async (HttpContext context, MemorySafetyService safety,
    CancellationToken cancellationToken) => Results.Ok(await safety.ListCorrectionsAsync(CurrentTenant(context), cancellationToken)))
    .Produces<IReadOnlyList<CorrectionTask>>().WithTags("Memory");
api.MapPost("/memory/corrections", async (MemoryCorrectionRequest request, HttpContext context,
    MemorySafetyService safety, MemoryEventService events, CancellationToken cancellationToken) =>
{
    var task = await safety.CreateCorrectionAsync(request.ClaimId, CurrentTenant(context), request, cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.correction.created", new { correction_id = task.Id, claim_id = task.ClaimId, reviewer_id = task.ReviewerId },
        cancellationToken: cancellationToken);
    return Results.Ok(task);
}).Produces<CorrectionTask>().RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapPost("/memory/corrections/{id}/approve", async (string id, MemoryCorrectionApprovalRequest request,
    HttpContext context, MemorySafetyService safety, MemoryEventService events, GovernanceService governance,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(CurrentTenant(context));
    var corrected = await safety.ApproveCorrectionAsync(id, CurrentTenant(context), request, cancellationToken);
    var externalImpacts = corrected.SupersedesId is { } oldClaimId
        ? (await safety.GetImpactAsync(oldClaimId, CurrentTenant(context), cancellationToken))
            .Where(x => x.TargetType == "ExternalAction").ToArray() : [];
    Guid? incidentId = null;
    if (externalImpacts.Length > 0)
    {
        var incident = await governance.CreateIncidentAsync(tenantId, new SecurityIncident
        {
            Severity = "SEV2", Title = "Memory correction affected an external action",
            Commander = CurrentUser(context), Status = "Open",
            EvidenceRefs = externalImpacts.Select(x => $"memory-impact:{x.Id}").ToArray(),
            ContainmentActions = ["Pause affected external action pending independent review"]
        }, cancellationToken);
        incidentId = incident.Id;
    }
    await events.RecordAsync(tenantId, CurrentUserId(context), CurrentUser(context),
        "memory.correction.published", new { correction_id = id, old_claim_id = corrected.SupersedesId, new_claim_id = corrected.Id, reviewer_id = request.ReviewerId, incident_id = incidentId },
        "Memory correction published and recall index updated", "Medium", cancellationToken);
    return Results.Ok(corrected);
}).Produces<MemoryClaim>().RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapGet("/memory/claims/{id}/impact", async (string id, HttpContext context, MemorySafetyService safety,
    MemoryService memory, CancellationToken cancellationToken) =>
    await memory.GetAsync(id, CurrentTenant(context), CurrentUser(context), cancellationToken) is null
        ? Results.NotFound() : Results.Ok(await safety.GetImpactAsync(id, CurrentTenant(context), cancellationToken)))
    .Produces<IReadOnlyList<MemoryImpactRecord>>().WithTags("Memory");
api.MapPost("/memory/claims/{id}/impact", async (string id, MemoryImpactRequest request,
    HttpContext context, MemorySafetyService safety, MemoryService memory, MemoryEventService events,
    CancellationToken cancellationToken) =>
{
    if (await memory.GetAsync(id, CurrentTenant(context), CurrentUser(context), cancellationToken) is null)
        return Results.NotFound();
    var impact = await safety.RecordImpactAsync(id, CurrentTenant(context), request, cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.impact.recorded", new { impact_id = impact.Id, claim_id = id, target_type = impact.TargetType, target_id = impact.TargetId },
        cancellationToken: cancellationToken);
    return Results.Ok(impact);
}).Produces<MemoryImpactRecord>().WithTags("Memory");
api.MapPost("/memories/{id}/forget", async (string id, HttpContext context, MemoryService service,
    MemoryEventService events, CancellationToken cancellationToken) =>
{
    await service.ForgetAsync(id, CurrentTenant(context), cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.claim.forgotten", new { claim_id = id }, cancellationToken: cancellationToken);
    return Results.NoContent();
}).RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapDelete("/memories/{id}", async (string id, HttpContext context, MemoryService service,
    MemoryEventService events, CancellationToken cancellationToken) =>
{
    await service.DeleteAsync(id, CurrentTenant(context), cancellationToken);
    await events.RecordAsync(Guid.Parse(CurrentTenant(context)), CurrentUserId(context), CurrentUser(context),
        "memory.claim.purged", new { claim_id = id }, cancellationToken: cancellationToken);
    return Results.NoContent();
}).RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapPost("/memory-graph/nodes", async (MemoryGraphNode node, HttpContext context, MemoryGraphService service,
    CancellationToken cancellationToken) => Results.Ok(await service.AddNodeAsync(node with { TenantId = CurrentTenant(context) }, cancellationToken)))
    .Produces<MemoryGraphNode>().RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapPost("/memory-graph/edges", async (MemoryGraphEdge edge, HttpContext context, MemoryGraphService service,
    CancellationToken cancellationToken) => Results.Ok(await service.AddEdgeAsync(edge with { TenantId = CurrentTenant(context) }, cancellationToken)))
    .Produces<MemoryGraphEdge>().RequireAuthorization("MemoryGovernor").WithTags("Memory");
api.MapGet("/memory-graph/query", async (string startClaimId, int maxDepth, int maxNodes, HttpContext context,
    MemoryGraphService service, MemoryService memory, CancellationToken cancellationToken) =>
{
    var tenantId = CurrentTenant(context);
    var result = await service.QueryAsync(tenantId, startClaimId, maxDepth, maxNodes, cancellationToken);
    var visible = new List<MemoryGraphNode>();
    foreach (var node in result.Nodes)
        if (await memory.GetAsync(node.ClaimId, tenantId, CurrentUser(context), cancellationToken) is not null)
            visible.Add(node);
    var visibleIds = visible.Select(x => x.ClaimId).ToHashSet(StringComparer.Ordinal);
    var filtered = new MemoryGraphQueryResult(visible,
        result.Edges.Where(x => visibleIds.Contains(x.FromClaimId) && visibleIds.Contains(x.ToClaimId)).ToArray());
    return visible.Any(x => x.ClaimId == startClaimId) ? Results.Ok(filtered) : Results.NotFound();
}).Produces<MemoryGraphQueryResult>().WithTags("Memory");
api.MapPost("/memories/decay", async (HttpContext context, MemoryService service,
    CancellationToken cancellationToken) =>
{
    await service.DecayAsync(CurrentTenant(context), cancellationToken);
    return Results.NoContent();
}).WithTags("Memory");

api.MapGet("/improvements", async (HttpContext context, ImprovementService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(CurrentTenant(context), cancellationToken)))
    .Produces<IReadOnlyList<ImprovementProposal>>().WithTags("Performance");
api.MapPost("/improvements", async (CreateImprovementRequest request, HttpContext context,
    ImprovementService service, AuditIntegrityService audit, CancellationToken cancellationToken) =>
{
    var proposal = await service.ProposeAsync(CurrentTenant(context), request.EmployeeId, request.Category,
        request.Summary, request.ExpectedBenefit, cancellationToken);
    await audit.AppendAsync(Guid.Parse(CurrentTenant(context)), "improvement.proposed", CurrentUser(context),
        new { proposal_id = proposal.Id, employee_id = proposal.EmployeeId, category = proposal.Category }, cancellationToken);
    return Results.Ok(proposal);
}).Produces<ImprovementProposal>().WithTags("Performance");
api.MapPost("/improvements/{id}/review", async (string id, ReviewImprovementRequest request,
    HttpContext context, ImprovementService service, AuditIntegrityService audit, CancellationToken cancellationToken) =>
{
    var proposal = await service.ReviewAsync(id, CurrentTenant(context), request.Approved, request.Reviewer,
        request.Note, cancellationToken);
    await audit.AppendAsync(Guid.Parse(CurrentTenant(context)), "improvement.reviewed", CurrentUser(context),
        new { proposal_id = proposal.Id, approved = request.Approved, reviewer_id = request.Reviewer }, cancellationToken);
    return Results.Ok(proposal);
}).Produces<ImprovementProposal>().WithTags("Performance");

api.MapPost("/agent-runtime/context", async (BuildContextRequest request, AgentRuntimeService service,
    CancellationToken cancellationToken) => Results.Ok(await service.BuildContextAsync(request.TaskId,
        request.SystemPrefix, request.TenantMemory, request.TaskGrantJson, request.ToolResultRefs,
        request.CheckpointJson, request.Compressed, cancellationToken))).Produces<AgentContextSnapshot>().WithTags("Agent Runtime");
api.MapPost("/agent-runtime/tool-loop", async (ToolLoopRequest request, AgentRuntimeService service,
    CancellationToken cancellationToken) => Results.Ok(await service.RunToolLoopAsync(request, cancellationToken)))
    .Produces<ToolLoopResult>().WithTags("Agent Runtime");
api.MapGet("/task-governance", async (HttpContext context, TaskGovernanceService service,
    CancellationToken cancellationToken) => Results.Ok(await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<TaskGovernanceState>().WithTags("Task Governance");
api.MapPost("/task-governance/task-types", async (TaskTypeDefinition definition, HttpContext context,
    TaskGovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.RegisterTaskTypeAsync(
        Guid.Parse(CurrentTenant(context)), definition, cancellationToken))).Produces<TaskTypeDefinition>().WithTags("Task Governance");
api.MapPost("/task-governance/capabilities", async (CapabilityAssessment assessment, HttpContext context,
    TaskGovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.AssessAsync(
        Guid.Parse(CurrentTenant(context)), assessment, cancellationToken))).Produces<CapabilityAssessment>().WithTags("Task Governance");
api.MapPost("/task-governance/claim-score", async (ClaimScoreRequest request, HttpContext context,
    TaskGovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.ScoreClaimAsync(
        Guid.Parse(CurrentTenant(context)), request.EmployeeId, request.TaskType, request.CurrentWip, request.WipLimit,
        request.HistorySuccess, request.ToolFit, request.CostEfficiency, request.Independent, cancellationToken)))
    .Produces<ClaimScoreDto>().WithTags("Task Governance");
api.MapPost("/task-governance/auto-assign", async (AutoAssignRequest request, HttpContext context,
    TaskGovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.AutoAssignAsync(
        Guid.Parse(CurrentTenant(context)), request, cancellationToken))).Produces<AutoAssignResult>().WithTags("Task Governance");
api.MapPost("/task-governance/tasks/{taskId:guid}/reassign", async (Guid taskId, ReassignTaskRequest request,
    HttpContext context, TaskGovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ReassignTaskAsync(Guid.Parse(CurrentTenant(context)), taskId, request.EmployeeId,
        request.Reason, request.ApprovalId, cancellationToken))).Produces<TaskAssignmentRecord>().WithTags("Task Governance");
api.MapPost("/task-governance/ledger", async (WorkLedgerEntry entry, HttpContext context,
    TaskGovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.RecordWorkAsync(
        Guid.Parse(CurrentTenant(context)), entry, cancellationToken))).Produces<WorkLedgerEntry>().WithTags("Task Governance");
api.MapPost("/task-governance/reviews", async (ReviewTaskRecord review, HttpContext context,
    TaskGovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.CreateReviewAsync(
        Guid.Parse(CurrentTenant(context)), review, cancellationToken))).Produces<ReviewTaskRecord>().WithTags("Task Governance");
api.MapPost("/task-governance/reviews/{reviewId:guid}/approve", async (Guid reviewId, ApproveReviewRequest request,
    HttpContext context, TaskGovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ApproveReviewAsync(Guid.Parse(CurrentTenant(context)), reviewId, request.Approver, cancellationToken)))
    .Produces<ReviewTaskRecord>().WithTags("Task Governance");
api.MapPost("/task-governance/debates", async (DebateTaskRecord debate, HttpContext context,
    TaskGovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.CreateDebateAsync(
        Guid.Parse(CurrentTenant(context)), debate, cancellationToken))).Produces<DebateTaskRecord>().WithTags("Task Governance");
api.MapPost("/task-governance/debates/{debateId:guid}/decide", async (Guid debateId, DecideDebateRequest request,
    HttpContext context, TaskGovernanceService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.DecideDebateAsync(Guid.Parse(CurrentTenant(context)), debateId, request.Decision, cancellationToken)))
    .Produces<DebateTaskRecord>().WithTags("Task Governance");
api.MapPost("/task-governance/adjudications", async (AdjudicationTaskRecord adjudication, HttpContext context,
    TaskGovernanceService service, CancellationToken cancellationToken) => Results.Ok(await service.AdjudicateAsync(
        Guid.Parse(CurrentTenant(context)), adjudication, cancellationToken))).Produces<AdjudicationTaskRecord>().WithTags("Task Governance");

api.MapGet("/agent-evolution", async (HttpContext context, AgentEvolutionService service,
    CancellationToken cancellationToken) => Results.Ok(await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<AgentEvolutionState>().WithTags("Agent Evolution");
api.MapPost("/agent-evolution/prompts", async (CreatePromptRequest request, HttpContext context,
    AgentEvolutionService service, CancellationToken cancellationToken) => Results.Ok(await service.CreatePromptAsync(
        Guid.Parse(CurrentTenant(context)), request.Name, request.Content, request.Kind, cancellationToken)))
    .Produces<PromptBundle>().WithTags("Agent Evolution");
api.MapPost("/agent-evolution/prompts/{id:guid}/activate", async (Guid id, HttpContext context,
    AgentEvolutionService service, CancellationToken cancellationToken) => Results.Ok(await service.ActivatePromptAsync(
        Guid.Parse(CurrentTenant(context)), id, cancellationToken)))
    .Produces<PromptBundle>().WithTags("Agent Evolution");
api.MapPost("/agent-evolution/skills", async (CreateSkillRequest request, HttpContext context,
    AgentEvolutionService service, CancellationToken cancellationToken) => Results.Ok(await service.CreateSkillAsync(
        Guid.Parse(CurrentTenant(context)), request.Name, request.PromptBundleId, request.ConfigJson, cancellationToken)))
    .Produces<SkillDefinition>().WithTags("Agent Evolution");
api.MapPost("/agent-evolution/skills/{id:guid}/activate", async (Guid id, HttpContext context,
    AgentEvolutionService service, CancellationToken cancellationToken) => Results.Ok(await service.ActivateSkillAsync(
        Guid.Parse(CurrentTenant(context)), id, cancellationToken)))
    .Produces<SkillDefinition>().WithTags("Agent Evolution");
api.MapPost("/agent-evolution/onboarding", async (OnboardingRequest request, HttpContext context,
    AgentEvolutionService service, CancellationToken cancellationToken) => Results.Ok(await service.CreateOnboardingBaselineAsync(
        Guid.Parse(CurrentTenant(context)), request.EmployeeId, request.PromptBundleId, cancellationToken)))
    .Produces<OnboardingBaseline>().WithTags("Agent Evolution");
api.MapPost("/agent-evolution/assets", async (PublishAssetRequest request, HttpContext context,
    AgentEvolutionService service, CancellationToken cancellationToken) => Results.Ok(await service.PublishAssetAsync(
        Guid.Parse(CurrentTenant(context)), request.AssetType, request.AssetId, request.Publisher, cancellationToken)))
    .Produces<PublicAsset>().WithTags("Agent Evolution");
api.MapPost("/agent-evolution/tool-policies", async (ToolDisclosurePolicy policy, HttpContext context,
    AgentEvolutionService service, CancellationToken cancellationToken) => Results.Ok(await service.SetToolPolicyAsync(
        Guid.Parse(CurrentTenant(context)), policy, cancellationToken)))
    .Produces<ToolDisclosurePolicy>().WithTags("Agent Evolution");
api.MapPost("/agent-evolution/tools/disclose", async (ToolDisclosureRequest request, HttpContext context,
    AgentEvolutionService service, CancellationToken cancellationToken) => Results.Ok(await service.DiscloseAsync(
        Guid.Parse(CurrentTenant(context)), request, cancellationToken)))
    .Produces<ToolDisclosureResult>().WithTags("Agent Evolution");

api.MapGet("/kpis", async (HttpContext context, PerformanceService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(CurrentTenant(context), cancellationToken)))
    .WithTags("Performance");
api.MapPost("/performance-reviews", async (CreateReviewRequest request, HttpContext context,
    PerformanceService service, CancellationToken cancellationToken) => Results.Ok(await service.ReviewAsync(
        request.EmployeeId, CurrentTenant(context), request.Conclusion, request.EvidenceJson, cancellationToken)))
    .WithTags("Performance");

api.MapGet("/performance-actions", async (HttpContext context, PerformanceActionService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(CurrentTenant(context), cancellationToken)))
    .Produces<IReadOnlyList<PerformanceActionRecord>>().WithTags("Performance");
api.MapPost("/performance-actions", async (PerformActionRequest request, HttpContext context,
    PerformanceActionService service, RegentDbContext db, AuditIntegrityService audit, CancellationToken cancellationToken) =>
{
    if (request.ActionType == "Terminate")
    {
        var tenantId = Guid.Parse(CurrentTenant(context));
        var approvalId = Guid.TryParse(request.ApprovalId, out var parsedApprovalId) ? parsedApprovalId : Guid.Empty;
        var approved = await db.Approvals.AnyAsync(x => x.TenantId == tenantId
            && x.Id == approvalId && x.State == ApprovalState.Approved
            && x.ActionType == "employee.terminate", cancellationToken);
        if (!approved) throw new InvalidOperationException("Human Boss approval is required for termination.");
    }
    var action = await service.PerformAsync(CurrentTenant(context), request.EmployeeId, request.ActionType,
        request.ApprovalId, request.Reason, cancellationToken);
    await audit.AppendAsync(Guid.Parse(CurrentTenant(context)), "performance.action.recorded", CurrentUser(context),
        new { action_id = action.Id, employee_id = action.EmployeeId, action_type = action.ActionType, approval_id = action.ApprovalId }, cancellationToken);
    return Results.Ok(action);
}).Produces<PerformanceActionRecord>().WithTags("Performance");
api.MapPost("/performance-actions/{id}/appeal", async (string id, AppealActionRequest request,
    HttpContext context, PerformanceActionService service, AuditIntegrityService audit, CancellationToken cancellationToken) =>
{
    var action = await service.AppealAsync(id, CurrentTenant(context), request.Reason, cancellationToken);
    await audit.AppendAsync(Guid.Parse(CurrentTenant(context)), "performance.action.appealed", CurrentUser(context),
        new { action_id = action.Id, employee_id = action.EmployeeId }, cancellationToken);
    return Results.Ok(action);
}).Produces<PerformanceActionRecord>().WithTags("Performance");
api.MapPost("/performance-actions/{id}/appeal/resolve", async (string id, ResolvePerformanceAppealRequest request,
    HttpContext context, PerformanceActionService service, AuditIntegrityService audit, CancellationToken cancellationToken) =>
{
    var action = await service.ResolveAppealAsync(id, CurrentTenant(context), request.Reviewer, request.Resolution,
        request.Upheld, cancellationToken);
    await audit.AppendAsync(Guid.Parse(CurrentTenant(context)), "performance.appeal.resolved", CurrentUser(context),
        new { action_id = action.Id, reviewer_id = request.Reviewer, upheld = request.Upheld }, cancellationToken);
    return Results.Ok(action);
}).Produces<PerformanceActionRecord>().WithTags("Performance");

api.MapGet("/collaboration/graph", async (HttpContext context, CollaborationVisualizationService service,
    CancellationToken cancellationToken) => Results.Ok(await service.GetGraphAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)))
    .Produces<CollaborationGraphDto>().RequireAuthorization("CollaborationRead").WithTags("Collaboration");
api.MapGet("/collaboration/trace", async (HttpContext context, CollaborationVisualizationService service,
    string entityType, string entityId, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetTraceAsync(Guid.Parse(CurrentTenant(context)), entityType, entityId, cancellationToken)))
    .Produces<CollaborationTraceDto>().WithTags("Collaboration");

api.MapGet("/collaboration/requests", async ([AsParameters] CoordinationPageQuery query,
    HttpContext context, CollaborationCoordinationService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAsync(Guid.Parse(CurrentTenant(context)), query, cancellationToken)))
    .Produces<Page<TaskCoordinationDto>>().RequireAuthorization("CollaborationRead").WithTags("Collaboration");
api.MapGet("/collaboration/requests/{id:guid}", async (Guid id, HttpContext context,
    CollaborationCoordinationService service, CancellationToken cancellationToken) =>
    await service.GetAsync(Guid.Parse(CurrentTenant(context)), id, cancellationToken) is { } request
        ? Results.Ok(request) : Results.NotFound())
    .Produces<TaskCoordinationDto>().WithTags("Collaboration");

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
api.MapGet("/conversations/{id}/messages", async (string id, HttpContext context, ChatService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ListMessagesAsync(id, CurrentTenant(context), cancellationToken))).WithTags("Chat");
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

var evaluationQa = api.MapGroup("/evaluation/qa");
evaluationQa.MapGet("", async (HttpContext context, EvaluationQaService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken))).Produces<EvaluationQaState>().WithTags("Evaluation QA");
evaluationQa.MapPost("/cases", async (EvaluationCaseRecord record, HttpContext context, EvaluationQaService service,
    CancellationToken cancellationToken) => Results.Ok(await service.RegisterCaseAsync(Guid.Parse(CurrentTenant(context)), record, cancellationToken)))
    .Produces<EvaluationCaseRecord>().WithTags("Evaluation QA");
evaluationQa.MapPost("/cases/{caseId:guid}/seal", async (Guid caseId, HttpContext context, EvaluationQaService service,
    CancellationToken cancellationToken) => Results.Ok(await service.SealCaseAsync(Guid.Parse(CurrentTenant(context)), caseId, cancellationToken)))
    .Produces<EvaluationCaseRecord>().WithTags("Evaluation QA");
evaluationQa.MapPost("/cases/{caseId:guid}/contamination", async (Guid caseId, ContaminationRequest request,
    HttpContext context, EvaluationQaService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AssessContaminationAsync(Guid.Parse(CurrentTenant(context)), caseId, request.Similarity, cancellationToken)))
    .Produces<EvaluationCaseRecord>().WithTags("Evaluation QA");
evaluationQa.MapPost("/judges/assign", async (JudgeAssignmentRequest request, HttpContext context, EvaluationQaService service,
    CancellationToken cancellationToken) => Results.Ok(await service.AssignJudgeAsync(Guid.Parse(CurrentTenant(context)),
        request.CandidateEmployeeId, request.CandidateModel, request.CandidateRuntime, request.Judge, cancellationToken)))
    .Produces<JudgeAssignmentResult>().WithTags("Evaluation QA");
evaluationQa.MapPost("/calibration", async (CalibrationRequest request, HttpContext context, EvaluationQaService service,
    CancellationToken cancellationToken) => Results.Ok(await service.CalibrateAsync(Guid.Parse(CurrentTenant(context)),
        request.SuiteId, request.SuiteSize, request.Samples, cancellationToken))).Produces<CalibrationReport>().WithTags("Evaluation QA");
evaluationQa.MapGet("/schedules", async (HttpContext context, EvaluationQaService service,
    CancellationToken cancellationToken) => Results.Ok((await service.GetAsync(Guid.Parse(CurrentTenant(context)), cancellationToken)).Schedules))
    .Produces<IReadOnlyList<EvaluationScheduleRecord>>().WithTags("Evaluation QA");
evaluationQa.MapPost("/schedules", async (EvaluationScheduleRecord schedule, HttpContext context,
    EvaluationQaService service, CancellationToken cancellationToken) => Results.Ok(await service.SaveScheduleAsync(
        Guid.Parse(CurrentTenant(context)), schedule, cancellationToken))).Produces<EvaluationScheduleRecord>().WithTags("Evaluation QA");
evaluationQa.MapPost("/schedules/run-due", async (EvaluationQaService service, CancellationToken cancellationToken) =>
    Results.Ok(new { executed = await service.RunDueAsync(cancellationToken) })).WithTags("Evaluation QA");
evaluationQa.MapPost("/gate", async (EvaluationGateRequest request, HttpContext context, EvaluationQaService service,
    CancellationToken cancellationToken) => Results.Ok(await service.EvaluateGateAsync(Guid.Parse(CurrentTenant(context)),
        request.SuiteId, request.CandidateEmployeeId, request.Repeats, request.SampleCount, request.Successes,
        request.BaselineSuccesses, request.DiscordantA, request.DiscordantB, request.CalibrationPassed, cancellationToken)))
    .Produces<EvaluationQaGate>().WithTags("Evaluation QA");

var agentApi = app.MapGroup("/agent/v1").RequireAuthorization("Agent");
var coordinationApi = agentApi.MapGroup("/collaboration").WithTags("Agent Collaboration");
coordinationApi.MapGet("/requests", async ([AsParameters] CoordinationPageQuery query, HttpContext context,
    CollaborationCoordinationService service, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    return Results.Ok(await service.ListAsync(identity.TenantId,
        query with { EmployeeId = identity.EmployeeId }, cancellationToken));
}).Produces<Page<TaskCoordinationDto>>();
coordinationApi.MapGet("/requests/{id:guid}", async (Guid id, HttpContext context,
    CollaborationCoordinationService service, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var request = await service.GetAsync(identity.TenantId, id, cancellationToken);
    if (request is null) return Results.NotFound();
    if (request.RequesterEmployeeId != identity.EmployeeId && request.TargetEmployeeId != identity.EmployeeId)
        throw new UnauthorizedAccessException("Agent is not a participant in this coordination request.");
    return Results.Ok(request);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/delegations", async (CreateTaskDelegationRequest request, HttpContext context,
    CollaborationCoordinationService service, IHubContext<TenantHub> hub, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.CreateDelegationAsync(identity, request, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/delegations/{id:guid}/decision", async (Guid id, CoordinationDecisionRequest decision,
    HttpContext context, CollaborationCoordinationService service, IHubContext<TenantHub> hub,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.DecideDelegationAsync(identity, id, decision, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/delegations/{id:guid}/cancel", async (Guid id, CoordinationCancelRequest request,
    HttpContext context, CollaborationCoordinationService service, IHubContext<TenantHub> hub,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.CancelDelegationAsync(identity, id, request.Reason, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/handoffs", async (CreateHandoffRequest request, HttpContext context,
    CollaborationCoordinationService service, IHubContext<TenantHub> hub, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.CreateHandoffAsync(identity, request, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/handoffs/{id:guid}/decision", async (Guid id, CoordinationDecisionRequest decision,
    HttpContext context, CollaborationCoordinationService service, IHubContext<TenantHub> hub,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.DecideHandoffAsync(identity, id, decision, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/help-requests", async (CreateHelpRequest request, HttpContext context,
    CollaborationCoordinationService service, IHubContext<TenantHub> hub, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.CreateHelpAsync(identity, request, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/help-requests/{id:guid}/responses", async (Guid id, HelpResponseRequest request,
    HttpContext context, CollaborationCoordinationService service, IHubContext<TenantHub> hub,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.RespondHelpAsync(identity, id, request, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/help-requests/{id:guid}/context", async (Guid id, HelpContextRequest request,
    HttpContext context, CollaborationCoordinationService service, IHubContext<TenantHub> hub,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.AddHelpContextAsync(identity, id, request, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/help-requests/{id:guid}/resolve", async (Guid id, CoordinationResolutionRequest request,
    HttpContext context, CollaborationCoordinationService service, IHubContext<TenantHub> hub,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.ResolveHelpAsync(identity, id, request.Resolution, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/interrupts", async (CreateInterruptRequest request, HttpContext context,
    CollaborationCoordinationService service, IHubContext<TenantHub> hub, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.CreateInterruptAsync(identity, request, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/interrupts/{id:guid}/checkpoint", async (Guid id, InterruptCheckpointRequest request,
    HttpContext context, CollaborationCoordinationService service, IHubContext<TenantHub> hub,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.AcknowledgeInterruptAsync(identity, id, request, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/interrupts/{id:guid}/resolve", async (Guid id, CoordinationResolutionRequest request,
    HttpContext context, CollaborationCoordinationService service, IHubContext<TenantHub> hub,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.ResolveInterruptAsync(identity, id, request, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();
coordinationApi.MapPost("/interrupts/{id:guid}/resume", async (Guid id, InterruptResumeRequest request,
    HttpContext context, CollaborationCoordinationService service, IHubContext<TenantHub> hub,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.ResumeInterruptAsync(identity, id, request, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result.Coordination, cancellationToken);
    return Results.Ok(result);
}).Produces<InterruptResumeResult>();
coordinationApi.MapPost("/interrupts/{id:guid}/cancel", async (Guid id, CoordinationCancelRequest request,
    HttpContext context, CollaborationCoordinationService service, IHubContext<TenantHub> hub,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    var result = await service.CancelInterruptAsync(identity, id, request, RequiredIdempotencyKey(context), cancellationToken);
    await NotifyCoordinationAsync(context, hub, result, cancellationToken);
    return Results.Ok(result);
}).Produces<TaskCoordinationDto>();

agentApi.MapGet("/me", (HttpContext context) => Results.Ok(CurrentAgent(context))).WithTags("Agent");
agentApi.MapGet("/tools/discover", async (HttpContext context, AgentToolInvocationService tools,
    CancellationToken cancellationToken) => Results.Ok(await tools.DiscoverAsync(CurrentAgent(context), cancellationToken)))
    .WithTags("Agent Tools");
agentApi.MapPost("/tools/invoke", async (AgentToolInvocationRequest request, HttpContext context,
    AgentToolInvocationService tools, CancellationToken cancellationToken) =>
    Results.Ok(await tools.InvokeAsync(CurrentAgent(context), request, cancellationToken)))
    .Produces<AgentToolInvocationResult>().WithTags("Agent Tools");
agentApi.MapPost("/tasks/{id:guid}/checkpoint", async (Guid id, TaskCheckpoint checkpoint, HttpContext context,
    TaskExecutionService service, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    if (identity.TaskId != id) throw new UnauthorizedAccessException("Agent token is not scoped to this task.");
    return await service.CheckpointAsync(id, checkpoint, identity.TenantId, cancellationToken)
        ? Results.NoContent() : Results.Conflict();
}).WithTags("Agent");
agentApi.MapPost("/tasks/{id:guid}/complete", async (Guid id, TaskCompletion completion, HttpContext context,
    TaskExecutionService service, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    if (identity.TaskId != id) throw new UnauthorizedAccessException("Agent token is not scoped to this task.");
    return await service.CompleteAsync(id, completion, identity.TenantId, cancellationToken)
        ? Results.NoContent() : Results.Conflict();
}).WithTags("Agent");
agentApi.MapPost("/agent-runtime/context", async (BuildContextRequest request, HttpContext context,
    AgentRuntimeService service, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    if (identity.TaskId != request.TaskId) throw new UnauthorizedAccessException("Agent token is not scoped to this task.");
    return Results.Ok(await service.BuildContextAsync(request.TaskId, request.SystemPrefix, request.TenantMemory,
        request.TaskGrantJson, request.ToolResultRefs, request.CheckpointJson, request.Compressed, cancellationToken));
}).Produces<AgentContextSnapshot>().WithTags("Agent");
agentApi.MapPost("/agent-runtime/tool-loop", async (ToolLoopRequest request, HttpContext context,
    AgentRuntimeService service, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "tasks:execute");
    if (identity.TaskId != request.TaskId) throw new UnauthorizedAccessException("Agent token is not scoped to this task.");
    return Results.Ok(await service.RunToolLoopAsync(request, cancellationToken));
}).Produces<ToolLoopResult>().WithTags("Agent");

agentApi.MapGet("/conversations", async (HttpContext context, ChatService service,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "chat:write");
    return Results.Ok(await service.ListAsync(identity.TenantId.ToString(), cancellationToken, identity.EmployeeId.ToString()));
}).WithTags("Agent Chat");
agentApi.MapGet("/conversations/{id}/messages", async (string id, HttpContext context, ChatService service,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "chat:write");
    return Results.Ok(await service.ListMessagesAsync(id, identity.TenantId.ToString(), cancellationToken,
        identity.EmployeeId.ToString()));
}).WithTags("Agent Chat");
agentApi.MapPost("/conversations", async (CreateConversationRequest request, HttpContext context,
    ChatService service, AuditIntegrityService audit, RegentDbContext db, CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "chat:write");
    var memberIds = request.MemberEmployeeIds.Distinct().ToArray();
    if (!memberIds.Contains(identity.EmployeeId.ToString(), StringComparer.Ordinal))
        throw new InvalidOperationException("The creating Agent must be a conversation member.");
    var tenantEmployeeIds = (await db.Employees.AsNoTracking().Where(x => x.TenantId == identity.TenantId)
        .Select(x => x.Id).ToListAsync(cancellationToken)).Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal);
    if (memberIds.Any(id => !Guid.TryParse(id, out _)) || memberIds.Any(id => !tenantEmployeeIds.Contains(id)))
        throw new InvalidOperationException("Conversation members must belong to the Agent tenant.");
    var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
    var conversation = await service.CreateConversationAsync(identity.TenantId.ToString(), request.Title, request.Kind,
        memberIds, employee.DisplayName, identity.EmployeeId.ToString(), request.ScopeType, request.ScopeId, cancellationToken);
    await audit.AppendAsync(identity.TenantId, "chat.conversation.created", employee.DisplayName,
        new
        {
            actor_type = "Agent", employee_id = identity.EmployeeId, task_id = identity.TaskId,
            conversation_id = conversation.Id, kind = conversation.Kind, member_count = conversation.MemberEmployeeIds.Count,
            scope_type = conversation.ScopeType
        }, cancellationToken);
    return Results.Ok(conversation);
}).WithTags("Agent Chat");
agentApi.MapPost("/conversations/{id}/members", async (string id, AddConversationMemberRequest request,
    HttpContext context, ChatService service, AuditIntegrityService audit, RegentDbContext db,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "chat:write");
    if (!Guid.TryParse(request.EmployeeId, out var employeeId)
        || !await db.Employees.AnyAsync(x => x.TenantId == identity.TenantId && x.Id == employeeId, cancellationToken))
        throw new InvalidOperationException("Conversation member must belong to the Agent tenant.");
    var visible = await service.ListAsync(identity.TenantId.ToString(), cancellationToken, identity.EmployeeId.ToString());
    if (!visible.Any(x => x.Id == id)) throw new UnauthorizedAccessException("Agent is not a conversation member.");
    var actor = await db.Employees.AsNoTracking().SingleAsync(x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
    var conversation = await service.AddMemberAsync(id, identity.TenantId.ToString(), request.EmployeeId, cancellationToken);
    await audit.AppendAsync(identity.TenantId, "chat.member.added", actor.DisplayName,
        new { actor_type = "Agent", employee_id = identity.EmployeeId, task_id = identity.TaskId, conversation_id = id, member_employee_id = employeeId }, cancellationToken);
    return Results.Ok(conversation);
}).WithTags("Agent Chat");
agentApi.MapPost("/conversations/{id}/messages", async (string id, SendMessageRequest request, HttpContext context,
    ChatService service, IHubContext<TenantHub> hub, AuditIntegrityService audit, RegentDbContext db,
    CancellationToken cancellationToken) =>
{
    var identity = CurrentAgent(context);
    RequireAgentScope(identity, "chat:write");
    var mentions = request.MentionedEmployeeIds?.Distinct().ToArray() ?? [];
    var tenantEmployeeIds = (await db.Employees.AsNoTracking().Where(x => x.TenantId == identity.TenantId)
        .Select(x => x.Id).ToListAsync(cancellationToken)).Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal);
    if (mentions.Any(value => !Guid.TryParse(value, out _)) || mentions.Any(value => !tenantEmployeeIds.Contains(value)))
        throw new InvalidOperationException("Mentioned employees must belong to the Agent tenant.");
    var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
    var message = await service.SendAsync(id, identity.TenantId.ToString(), employee.DisplayName, request.Body,
        request.Priority, cancellationToken, request.ReplyToMessageId, mentions, request.MessageType,
        request.EvidenceRefs, request.IdempotencyKey, identity.EmployeeId.ToString(), "Agent", true);
    await audit.AppendAsync(identity.TenantId, "chat.message.sent", employee.DisplayName,
        new
        {
            actor_type = "Agent", employee_id = identity.EmployeeId, task_id = identity.TaskId,
            message_id = message.Id, conversation_id = id, seq = message.Seq, message_type = message.MessageType,
            priority = message.Priority, body_length = message.Body.Length, mention_count = message.MentionedEmployeeIds.Count,
            evidence_count = message.EvidenceRefs.Count
        }, cancellationToken);
    await hub.Clients.Group(identity.TenantId.ToString()).SendAsync("message.created", message, cancellationToken);
    return Results.Ok(message);
}).WithTags("Agent Chat");

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

static AgentIdentity CurrentAgent(HttpContext context) => AgentIdentityContext.Required(context.Items);

static string RequiredIdempotencyKey(HttpContext context)
{
    var key = context.Request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(key)) throw new BadHttpRequestException("Idempotency-Key is required.");
    if (key.Length > 200) throw new BadHttpRequestException("Idempotency-Key is too long.");
    return key;
}

static async Task NotifyCoordinationAsync(HttpContext context, IHubContext<TenantHub> hub,
    TaskCoordinationDto coordination, CancellationToken cancellationToken) =>
    await hub.Clients.Group(CurrentTenant(context)).SendAsync("coordination.changed", coordination, cancellationToken);

static void RequireAgentScope(AgentIdentity identity, string scope)
{
    if (!identity.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
        throw new UnauthorizedAccessException("Agent scope is insufficient.");
}

static string CurrentTenant(HttpContext context) =>
    context.User.FindFirstValue("tenant_id") ?? "11111111-1111-1111-1111-111111111111";

static string CurrentUser(HttpContext context) => context.User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

static Guid CurrentUserId(HttpContext context) =>
    Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("Missing user identity."));

static void RegisterTenantStore<T>(IServiceCollection services, string path) where T : class
{
    if (services.Any(descriptor => descriptor.ServiceType == typeof(JsonDocumentStore<T>)))
        services.AddSingleton<ITenantJsonDocumentStore>(provider => provider.GetRequiredService<JsonDocumentStore<T>>());
    else
        services.AddSingleton<ITenantJsonDocumentStore>(new JsonDocumentStore<T>(path));
}

static async Task<IResult> CompleteAuthAsync(HttpContext context, AuthResult result)
{
    await SignInTenantAsync(context, result.UserId, result.TenantId, result.DisplayName, result.Role, result.SessionId);
    return Results.Ok(new
    {
        email = result.Email, display_name = result.DisplayName, user_id = result.UserId,
        tenant_id = result.TenantId, session_id = result.SessionId, recovery_codes = result.RecoveryCodes,
        role = result.Role, auth_method = result.AuthMethod, session_kind = result.SessionKind, capabilities = result.Capabilities
    });
}

static async Task SignInTenantAsync(HttpContext context, Guid userId, Guid tenantId, string displayName, string role,
    Guid? sessionId = null, CancellationToken cancellationToken = default)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, userId.ToString()),
        new("tenant_id", tenantId.ToString()),
        new(ClaimTypes.Name, displayName),
        new(ClaimTypes.Role, role)
    };
    if (sessionId is { } id)
    {
        claims.Add(new Claim("session_id", id.ToString()));
        var session = await context.RequestServices.GetRequiredService<JsonDocumentStore<AuthSessionRecord>>()
            .GetAsync(id.ToString(), cancellationToken);
        if (session is not null)
        {
            claims.Add(new Claim("auth_method", session.AuthMethod));
            claims.Add(new Claim("session_kind", session.SessionKind));
        }
    }
    var capabilities = await context.RequestServices.GetRequiredService<RegentDbContext>().Memberships.AsNoTracking()
        .Where(x => x.TenantId == tenantId && x.UserId == userId).Select(x => x.CapabilitiesJson)
        .SingleOrDefaultAsync(cancellationToken);
    if (!string.IsNullOrWhiteSpace(capabilities))
        foreach (var capability in JsonSerializer.Deserialize<string[]>(capabilities) ?? [])
            claims.Add(new Claim("capability", capability));
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
}

static bool EmergencyRequestAllowed(PathString path, string method, Guid tenantId, string allowedActionsJson)
{
    string[] allowedActions;
    try { allowedActions = JsonSerializer.Deserialize<string[]>(allowedActionsJson) ?? []; }
    catch (JsonException) { return false; }
    var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
    string? action = null;
    if (segments.Length == 2 && segments[0] == "auth" && segments[1] == "logout") action = "account.access";
    else if (segments.Length == 2 && segments[0] == "auth" && segments[1] == "sessions") action = "account.access";
    else if (segments.Length == 4 && segments[0] == "auth" && segments[1] == "sessions"
        && Guid.TryParse(segments[2], out _) && segments[3] == "revoke") action = "account.access";
    else if (segments.Length >= 4 && segments[0] == "v1" && segments[1] == "tenants"
        && Guid.TryParse(segments[2], out var requestedTenant) && requestedTenant == tenantId)
    {
        if (segments[3] == "authentication-policy") action = "sso.restore";
        else if (segments[3] == "identity-providers" || segments[3] == "oidc") action = "sso.restore";
    }
    return action is not null && allowedActions.Contains(action, StringComparer.Ordinal)
        && !HttpMethods.IsOptions(method) && !HttpMethods.IsHead(method);
}

public sealed record DevLoginRequest(string Email, Guid? TenantId = null);
public sealed record SwitchTenantRequest(Guid TenantId);
public sealed record DevAgentTokenRequest(Guid TenantId, Guid EmployeeId, Guid? TaskId = null,
    string? RunnerId = null, IReadOnlyList<string>? Scopes = null);
public sealed record CreateTenantRequest(string Name, string CompanyName);
public sealed record AssignPositionRequest(Guid PositionId);
public sealed record PositionStatusRequest(string Status);
public sealed record SplitPositionRequest(string Name, string Responsibilities);
public sealed record CreateDelegationRequest(Guid DelegateEmployeeId, Guid SubtreePositionId, IReadOnlyList<string> Actions, long BudgetLimitMinor, int Minutes);
public sealed record OffboardRequest(string ApprovalId);
public sealed record RehireRequest(Guid PositionId, string ApprovalId);
public sealed record CreateCandidateRequest(Guid PositionId, string DisplayName, string ModelVersion, string JobDescription);
public sealed record RejectCandidateRequest(string Reason);
public sealed record HireCandidateRequest(string ApprovalId);
public sealed record DecisionRequest(string Decision, string Rationale, IReadOnlyList<Guid> EvidenceIds, int Reround, string Decider);
public sealed record ApprovePlanRequest(string Approver, string? ApprovalId);
public sealed record ReadyRequirementRequest(int TotalWeight);
public sealed record ClaimTaskRequest(string Assignee);
public sealed record SubmitTaskRequest(string Author);
public sealed record FixDefectRequest(string Fixer, EvidenceRef Evidence);
public sealed record RetestDefectRequest(bool Passed, string Retester);
public sealed record ReleaseRequest(string Version, string ArtifactRef);
public sealed record RollbackRequest(string TargetVersion);
public sealed record AuditCheckpointRequest(string Profile);
public sealed record PublishEventRequest(string EventType, object Payload);
public sealed record AdmitToolRequest(string Reviewer);
public sealed record ScanContentRequest(string Content);
public sealed record RedactContentRequest(string Content);
public sealed record ReviewContentRequest(string ContentRef, string Content);
public sealed record ContainIncidentRequest(string Action);
public sealed record RecoverIncidentRequest(string Summary);
public sealed record CloseIncidentRequest(string Retrospective);
public sealed record DeleteDlpTokensRequest(IReadOnlyList<string> Tokens);
public sealed record SealEvidenceRequest(string Evidence);
public sealed record SoftDeleteRequest(string ResourceType, string ResourceId, string Reason);
public sealed record PurgeRequest(string Confirmation);
public sealed record ArchiveRequest(string Period, string Tier, int RecordCount);
public sealed record BackupRequest(string Profile, int RpoMinutes, int RtoMinutes);
public sealed record RefreshSnapshotRequest(DateOnly Date);
public sealed record RotateModelRequest(string Version, string Protocol);
public sealed record ModelUnavailableRequest(Guid EmployeeId, string Provider, string ModelId, int ConsecutiveFailures, int UnavailableMinutes);
public sealed record TestModelRequest(string Provider, string ModelId);
public sealed record CorrectCostRequest(decimal Delta, string Reason);
public sealed record DispatchRequest(DateTimeOffset Now, bool Emergency);
public sealed record ContaminationRequest(double Similarity);
public sealed record JudgeAssignmentRequest(Guid CandidateEmployeeId, string CandidateModel, string CandidateRuntime, JudgeProfile Judge);
public sealed record CalibrationRequest(string SuiteId, int SuiteSize, IReadOnlyList<CalibrationSample> Samples);
public sealed record EvaluationGateRequest(string SuiteId, Guid CandidateEmployeeId, int Repeats, int SampleCount,
    int Successes, int BaselineSuccesses, int DiscordantA, int DiscordantB, bool CalibrationPassed);
public sealed record CreateReviewRequest(string EmployeeId, string Conclusion, string EvidenceJson);
public sealed record CreateWorkflowRequest(string Name, IReadOnlyList<string> Steps, string ScopePositionId);
public sealed record ReviewWorkflowRequest(string Decision, string Reviewer, string Note);
public sealed record SendMessageRequest(string Body, string Priority, string? ReplyToMessageId = null,
    IReadOnlyList<string>? MentionedEmployeeIds = null, string MessageType = "Chat",
    IReadOnlyList<string>? EvidenceRefs = null, string? IdempotencyKey = null);
public sealed record CreateConversationRequest(string Title, string Kind, IReadOnlyList<string> MemberEmployeeIds,
    string ScopeType = "Topic", string? ScopeId = null);
public sealed record AddConversationMemberRequest(string EmployeeId);
public sealed record RunEvaluationRequest(string SuiteId, string CandidateEmployeeId, int Seed);
public sealed record CreateImprovementRequest(string EmployeeId, string Category, string Summary, string ExpectedBenefit);
public sealed record ReviewImprovementRequest(bool Approved, string Reviewer, string Note);
public sealed record ResolvePerformanceAppealRequest(string Reviewer, string Resolution, bool Upheld);
public sealed record CreatePromptRequest(string Name, string Content, string Kind = "Operations");
public sealed record OnboardingRequest(Guid EmployeeId, Guid PromptBundleId);
public sealed record PublishAssetRequest(string AssetType, Guid AssetId, string Publisher);
public sealed record CreateSkillRequest(string Name, Guid PromptBundleId, string ConfigJson);
public sealed record BuildContextRequest(Guid TaskId, string SystemPrefix, string TenantMemory, string TaskGrantJson,
    IReadOnlyList<string> ToolResultRefs, string CheckpointJson, bool Compressed);
public sealed record ClaimScoreRequest(Guid EmployeeId, string TaskType, int CurrentWip, int WipLimit,
    decimal HistorySuccess, decimal ToolFit, decimal CostEfficiency, bool Independent);
public sealed record ApproveReviewRequest(string Approver);
public sealed record DecideDebateRequest(string Decision);
public sealed record ResolveAppealRequest(string Resolution);
public sealed record FairnessAuditRequest(Guid PositionId, string Period, int SampleSize, decimal SelectionRateRatio);
public sealed record ObserveReleaseRequest(bool Healthy, string MetricsJson);
public sealed record PerformActionRequest(string EmployeeId, string ActionType, string? ApprovalId, string Reason);
public sealed record AppealActionRequest(string Reason);
