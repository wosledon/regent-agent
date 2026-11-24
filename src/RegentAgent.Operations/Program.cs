using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using RegentAgent.Contracts.Operations;
using RegentAgent.Observability;
using RegentAgent.Operations;
using RegentAgent.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRegentObservability(builder.Configuration);
builder.Services.AddOpenApi("v1");
builder.Services.AddProblemDetails();
var operationsDataPath = builder.Configuration["Data:OperationsPath"]
    ?? (builder.Environment.IsDevelopment()
        ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data", "ops"))
        : "data/ops");
builder.Services.AddSingleton(new JsonDocumentStore<TenantAccount>(Path.Combine(operationsDataPath, "tenants.json")));
builder.Services.AddSingleton(new JsonDocumentStore<SupportGrantRecord>(Path.Combine(operationsDataPath, "grants.json")));
builder.Services.AddSingleton(new JsonDocumentStore<BackupProfileRecord>(Path.Combine(operationsDataPath, "backups.json")));
builder.Services.AddSingleton(new JsonDocumentStore<OpsAuditRecord>(Path.Combine(operationsDataPath, "audit.json")));
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
    });
builder.Services.AddAuthorization();
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
    if (!app.Environment.IsDevelopment()) return Results.NotFound();
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, "operator-demo"),
        new Claim(ClaimTypes.Name, "Demo Operator"),
        new Claim(ClaimTypes.Role, "SRE")
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

var ops = app.MapGroup("/ops").RequireAuthorization();
ops.MapPost("/status/refresh", async (PlatformStatusService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RefreshAsync(cancellationToken)));
ops.MapPost("/status/incidents", async (PlatformIncident incident, PlatformStatusService service,
    CancellationToken cancellationToken) => Results.Ok(await service.CreateIncidentAsync(incident, cancellationToken)));
ops.MapPost("/status/incidents/{id:guid}/resolve", async (Guid id, PlatformStatusService service,
    CancellationToken cancellationToken) => Results.Ok(await service.ResolveIncidentAsync(id, cancellationToken)));
ops.MapPut("/tenants/{id}/status", async (string id, TenantStatusRequest request, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) => Results.Ok(await service.SetTenantStatusAsync(
        id, request.Status, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)));
ops.MapGet("/tenants", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.TenantsAsync(cancellationToken)));
ops.MapGet("/support-grants", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GrantsAsync(cancellationToken)));
ops.MapPost("/support-grants", async (SupportGrantRequest request, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) => Results.Ok(await service.CreateGrantAsync(
        request.TenantId, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, request.Scope, request.Minutes, cancellationToken)));
ops.MapPost("/support-grants/{id}/approve", async (string id, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) => Results.Ok(await service.ApproveGrantAsync(
        id, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)));
ops.MapPost("/support-grants/{id}/revoke", async (string id, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) =>
{
    await service.RevokeGrantAsync(id, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);
    return Results.NoContent();
});
ops.MapGet("/backups", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.BackupsAsync(cancellationToken)));
ops.MapPut("/backup-profile", async (BackupProfileRecord profile, OperationsService service, HttpContext context,
    CancellationToken cancellationToken) => Results.Ok(await service.ConfigureBackupAsync(profile,
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)));
ops.MapGet("/backup-coverage/{profileId}", async (string profileId, OperationsService service,
    CancellationToken cancellationToken) => Results.Ok(await service.GetCoverageAsync(profileId, cancellationToken)));
ops.MapPost("/backups/run", async (BackupRunRequest request, OperationsService service, HttpContext context,
    CancellationToken cancellationToken) => Results.Ok(await service.RunBackupAsync(request.ProfileId,
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken)));
ops.MapGet("/backup-jobs", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.BackupJobsAsync(cancellationToken)));
ops.MapPost("/restores", async (RestoreRequest request, OperationsService service,
    CancellationToken cancellationToken) => Results.Ok(await service.RequestRestoreAsync(request, cancellationToken)));
ops.MapPost("/restores/{id:guid}/approve", async (Guid id, RestoreApprovalRequest request,
    OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ApproveRestoreAsync(id, request, cancellationToken)));
ops.MapGet("/restores", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.RestoreJobsAsync(cancellationToken)));
ops.MapPut("/backups", async (BackupRequest request, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) =>
{
    await service.SetBackupAsync(request.Enabled, request.Components, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);
    return Results.Ok(await service.BackupsAsync(cancellationToken));
});
ops.MapGet("/audit", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AuditsAsync(cancellationToken)));
ops.MapGet("/observability/profile", async (ObservabilityService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetProfileAsync(cancellationToken)));
ops.MapPut("/observability/profile", async (ObservabilityProfile profile, ObservabilityService service,
    CancellationToken cancellationToken) => Results.Ok(await service.UpdateProfileAsync(profile, cancellationToken)));
ops.MapPost("/observability/slos", async (SloDefinition slo, ObservabilityService service,
    CancellationToken cancellationToken) => Results.Ok(await service.SaveSloAsync(slo, cancellationToken)));
ops.MapPost("/observability/samples", async (TelemetrySample sample, ObservabilityService service,
    CancellationToken cancellationToken) =>
{
    await service.RecordSampleAsync(sample, cancellationToken);
    return Results.NoContent();
});
ops.MapGet("/observability/evaluation", async (ObservabilityService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.EvaluateAsync(cancellationToken)));
ops.MapGet("/observability/alerts", async (ObservabilityService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAlertsAsync(cancellationToken)));
ops.MapPost("/observability/alerts/{id:guid}/acknowledge", async (Guid id, AlertAcknowledgementRequest request,
    ObservabilityService service, CancellationToken cancellationToken) =>
{
    await service.AcknowledgeAlertAsync(id, request, cancellationToken);
    return Results.NoContent();
});
ops.MapPost("/observability/alerts/{id:guid}/resolve", async (Guid id, HttpContext context,
    ObservabilityService service, CancellationToken cancellationToken) =>
{
    var resolution = context.Request.Headers["X-Resolution"].ToString();
    await service.ResolveAlertAsync(id, resolution, cancellationToken);
    return Results.NoContent();
});
ops.MapPost("/observability/alerts/silence", async (AlertSilenceRequest request,
    ObservabilityService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.SilenceAlertAsync(request, cancellationToken)));
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

public sealed record SupportGrantRequest(string TenantId, string Scope, int Minutes);
public sealed record BackupRequest(bool Enabled, string Components);
