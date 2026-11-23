using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using RegentAgent.Contracts.Operations;
using RegentAgent.Operations;
using RegentAgent.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi("v1");
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(sp => new OperationsService(
    new JsonDocumentStore<TenantAccount>("data/ops/tenants.json"),
    new JsonDocumentStore<SupportGrantRecord>("data/ops/grants.json"),
    new JsonDocumentStore<BackupProfileRecord>("data/ops/backups.json"),
    new JsonDocumentStore<OpsAuditRecord>("data/ops/audit.json")));
builder.Services.AddSingleton(new JsonDocumentStore<PlatformComponentStatus>("data/ops/status-components.json"));
builder.Services.AddSingleton(new JsonDocumentStore<PlatformIncident>("data/ops/status-incidents.json"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PlatformStatusService>();
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
    .WithOrigins("http://localhost:4173").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
var app = builder.Build();

if (app.Environment.IsDevelopment())
    await app.Services.GetRequiredService<OperationsService>().SeedAsync(CancellationToken.None);

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
ops.MapPut("/backups", async (BackupRequest request, OperationsService service,
    HttpContext context, CancellationToken cancellationToken) =>
{
    await service.SetBackupAsync(request.Enabled, request.Components, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);
    return Results.Ok(await service.BackupsAsync(cancellationToken));
});
ops.MapGet("/audit", async (OperationsService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.AuditsAsync(cancellationToken)));
app.Run();

public sealed record SupportGrantRequest(string TenantId, string Scope, int Minutes);
public sealed record BackupRequest(bool Enabled, string Components);
