using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using RegentAgent.Contracts.Identity;
using RegentAgent.Contracts.Operations;
using RegentAgent.Observability;
using RegentAgent.Operations;
using RegentAgent.Storage;

var builder = WebApplication.CreateBuilder(args);
var allowDemoLogin = builder.Configuration.GetValue<bool>("Operations:AllowDemoLogin");
builder.Services.AddRegentObservability(builder.Configuration);
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Servers = [];
        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails();
var operationsDataPath = builder.Configuration["Data:OperationsPath"]
    ?? (builder.Environment.IsDevelopment()
        ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data", "ops"))
        : "data/ops");
builder.Services.AddSingleton(new JsonDocumentStore<TenantAccount>(Path.Combine(operationsDataPath, "tenants.json")));
builder.Services.AddSingleton(new JsonDocumentStore<SupportGrantRecord>(Path.Combine(operationsDataPath, "grants.json")));
builder.Services.AddSingleton(new JsonDocumentStore<SupportGrantApprovalRecord>(Path.Combine(operationsDataPath, "support-approvals.json")));
builder.Services.AddSingleton(new JsonDocumentStore<BackupProfileRecord>(Path.Combine(operationsDataPath, "backups.json")));
builder.Services.AddSingleton(new JsonDocumentStore<OpsAuditRecord>(Path.Combine(operationsDataPath, "audit.json")));
builder.Services.AddSingleton(new BackupSourceOptions(builder.Configuration["Backup:SourceRoot"] ?? "data"));
builder.Services.AddSingleton<OperationsService>();
builder.Services.AddSingleton(new JsonDocumentStore<PlatformComponentStatus>("data/ops/status-components.json"));
builder.Services.AddSingleton(new JsonDocumentStore<PlatformIncident>("data/ops/status-incidents.json"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PlatformStatusService>();
builder.Services.AddSingleton(new JsonDocumentStore<ObservabilityProfile>("data/ops/observability-profile.json"));
builder.Services.AddSingleton(new JsonDocumentStore<SloDefinition>("data/ops/slos.json"));
builder.Services.AddSingleton(new JsonDocumentStore<TelemetrySample>("data/ops/telemetry-samples.json"));
builder.Services.AddSingleton(new JsonDocumentStore<AlertRecord>(Path.Combine(operationsDataPath, "alerts.json")));
builder.Services.AddSingleton(new JsonDocumentStore<AlertSilenceRecord>(Path.Combine(operationsDataPath, "alert-silences.json")));
builder.Services.AddSingleton(new JsonDocumentStore<BackupJobRecord>(Path.Combine(operationsDataPath, "backup-jobs.json")));
builder.Services.AddSingleton(new JsonDocumentStore<RestoreJobRecord>(Path.Combine(operationsDataPath, "restore-jobs.json")));
builder.Services.AddSingleton<IObjectStore>(new LocalFileObjectStore(Path.Combine(operationsDataPath, "objects")));
builder.Services.AddSingleton<ObservabilityService>();
builder.Services.AddSingleton<ElasticsearchStatusService>();
var telemetryIngestToken = builder.Configuration["Telemetry:IngestToken"];
if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(telemetryIngestToken))
    throw new InvalidOperationException("Production Operations requires Telemetry:IngestToken.");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "regent.ops";
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
builder.Services.AddAuthorization(OperatorAuthorization.Configure);
builder.Services.AddCors(options => options.AddPolicy("ops", policy => policy
    .WithOrigins("http://localhost:4173", "http://localhost:5173").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var (status, code, message) = exception switch
    {
        KeyNotFoundException => (StatusCodes.Status404NotFound, "NOT_FOUND", "Resource not found."),
        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "FORBIDDEN", "Access denied."),
        InvalidOperationException => (StatusCodes.Status409Conflict, "INVALID_STATE", exception.Message),
        _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Operations request failed.")
    };
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { request_id = context.TraceIdentifier, error = new { code, message, retryable = false } });
}));

if (app.Environment.IsDevelopment())
{
    await app.Services.GetRequiredService<OperationsService>().SeedAsync(CancellationToken.None);
    var observability = app.Services.GetRequiredService<ObservabilityService>();
    await observability.SaveSloAsync(new SloDefinition { Name = "browser_api_availability", Metric = "api.request", Target = 0.995m }, CancellationToken.None);
    await observability.SaveSloAsync(new SloDefinition { Name = "browser_lcp", Metric = "web.vital.lcp", Target = 0.95m }, CancellationToken.None);
    await observability.SaveSloAsync(new SloDefinition { Name = "browser_cls", Metric = "web.vital.cls", Target = 0.95m }, CancellationToken.None);
    await observability.SaveSloAsync(new SloDefinition { Name = "browser_inp", Metric = "web.vital.inp", Target = 0.95m }, CancellationToken.None);
}

app.UseCors("ops");
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" })).AllowAnonymous();
app.MapGet("/public-status", async (PlatformStatusService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(cancellationToken))).AllowAnonymous();
app.MapPost("/auth/dev-login", async (HttpContext context) =>
{
    if (!app.Environment.IsDevelopment() || !allowDemoLogin) return Results.NotFound();
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, "operator-demo"),
        new Claim(ClaimTypes.Name, "Demo Operator"),
        new Claim(ClaimTypes.Email, "operator@example.test"),
        new Claim(ClaimTypes.Role, "SRE"),
        new Claim("auth_method", "Development"),
        new Claim("session_kind", "Operator")
    };
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new { email = "operator@example.test", display_name = "Demo Operator" });
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

var ops = app.MapGroup("/ops").RequireAuthorization(OperatorAuthorization.ReaderPolicyName);
ops.MapGet("/auth/me", (HttpContext context) =>
{
    var capabilities = context.User.FindAll("capability").Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
    return Results.Ok(new OperatorSessionSummaryDto(
        context.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
        context.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
        context.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
        context.User.FindFirstValue("auth_method") ?? "Cookie",
        context.User.FindFirstValue("session_kind") ?? "Operator",
        capabilities));
}).Produces<OperatorSessionSummaryDto>().Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);
ops.MapPost("/status/refresh", async (PlatformStatusService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RefreshAsync(cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapPost("/status/incidents", async (PlatformIncident incident, PlatformStatusService service,
    CancellationToken cancellationToken) => Results.Ok(await service.CreateIncidentAsync(incident, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapPost("/status/incidents/{id:guid}/resolve", async (Guid id, PlatformStatusService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ResolveIncidentAsync(id, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapPut("/tenants/{id}/status", async (string id, TenantStatusRequest request, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) => Results.Ok(await service.SetTenantStatusAsync(
        id, request.Status, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapGet("/tenants", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.TenantsAsync(cancellationToken)));
ops.MapGet("/support-grants", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GrantsAsync(cancellationToken)));
ops.MapPost("/support-grants", async (SupportGrantRequest request, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) => Results.Ok(await service.CreateGrantAsync(
        request.TenantId, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, request.Scope, request.Minutes,
        request.ApprovalRef, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapPost("/support-grants/{id}/approve", async (string id, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) => Results.Ok(await service.ApproveGrantAsync(
        id, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapPost("/support-grants/{id}/revoke", async (string id, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) =>
{
    await service.RevokeGrantAsync(id, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapGet("/backups", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.BackupsAsync(cancellationToken)));
ops.MapPut("/backup-profile", async (BackupProfileRecord profile, OperationsService service, HttpContext context,
    CancellationToken cancellationToken) => Results.Ok(await service.ConfigureBackupAsync(profile,
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapGet("/backup-coverage/{profileId}", async (string profileId, OperationsService service,
    CancellationToken cancellationToken) => Results.Ok(await service.GetCoverageAsync(profileId, cancellationToken)));
ops.MapPost("/backups/run", async (BackupRunRequest request, OperationsService service, HttpContext context,
    CancellationToken cancellationToken) => Results.Ok(await service.RunBackupAsync(request.ProfileId,
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapGet("/backup-jobs", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.BackupJobsAsync(cancellationToken)));
ops.MapPost("/restores", async (RestoreRequest request, OperationsService service, HttpContext context,
    CancellationToken cancellationToken) => Results.Ok(await service.RequestRestoreAsync(request,
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapPost("/restores/{id:guid}/approve", async (Guid id, RestoreApprovalRequest request,
    OperationsService service, HttpContext context, CancellationToken cancellationToken) =>
    Results.Ok(await service.ApproveRestoreAsync(id, request,
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapGet("/restores", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RestoreJobsAsync(cancellationToken)));
ops.MapPut("/backups", async (BackupRequest request, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) =>
{
    await service.SetBackupAsync(request.Enabled, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);
    return Results.Ok(await service.BackupsAsync(cancellationToken));
}).RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapGet("/audit", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AuditsAsync(cancellationToken)));
ops.MapGet("/observability/profile", async (ObservabilityService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetProfileAsync(cancellationToken)));
ops.MapGet("/observability/elasticsearch", async (ElasticsearchStatusService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(cancellationToken)));
ops.MapPut("/observability/profile", async (ObservabilityProfile profile, ObservabilityService service,
    CancellationToken cancellationToken) => Results.Ok(await service.UpdateProfileAsync(profile, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapPost("/observability/slos", async (SloDefinition slo, ObservabilityService service,
    CancellationToken cancellationToken) => Results.Ok(await service.SaveSloAsync(slo, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapPost("/observability/samples", async (TelemetrySample sample, ObservabilityService service,
    CancellationToken cancellationToken) =>
{
    await service.RecordSampleAsync(sample, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapGet("/observability/evaluation", async (ObservabilityService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.EvaluateAsync(cancellationToken)));
ops.MapGet("/observability/alerts", async (ObservabilityService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAlertsAsync(cancellationToken)));
ops.MapPost("/observability/alerts/{id:guid}/acknowledge", async (Guid id,
    ObservabilityService service, HttpContext context, CancellationToken cancellationToken) =>
{
    var request = new AlertAcknowledgementRequest(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    await service.AcknowledgeAlertAsync(id, request, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapPost("/observability/alerts/{id:guid}/resolve", async (Guid id, HttpContext context,
    ObservabilityService service, CancellationToken cancellationToken) =>
{
    var resolution = context.Request.Headers["X-Resolution"].ToString();
    await service.ResolveAlertAsync(id, resolution, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(OperatorAuthorization.WriterPolicyName);
ops.MapPost("/observability/alerts/silence", async (AlertSilenceRequest request,
    ObservabilityService service, HttpContext context, CancellationToken cancellationToken) =>
    Results.Ok(await service.SilenceAlertAsync(request,
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)))
    .RequireAuthorization(OperatorAuthorization.WriterPolicyName);
app.MapPost("/telemetry/browser", async (BrowserTelemetryBatch batch, HttpContext context,
    ObservabilityService service, CancellationToken cancellationToken) =>
{
    if (!string.IsNullOrWhiteSpace(telemetryIngestToken)
        && !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(context.Request.Headers["X-Regent-Telemetry-Key"].ToString()),
            Encoding.UTF8.GetBytes(telemetryIngestToken)))
        return Results.Unauthorized();
    await service.RecordBrowserBatchAsync(batch, cancellationToken);
    return Results.Accepted();
}).AllowAnonymous();
app.Run();

public sealed record SupportGrantRequest(string TenantId, string Scope = "read-only", int Minutes = 30, string? ApprovalRef = null);
public sealed record BackupRequest(bool Enabled);

public partial class Program { }
