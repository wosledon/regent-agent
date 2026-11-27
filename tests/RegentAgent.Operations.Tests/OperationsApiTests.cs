using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RegentAgent.Contracts.Operations;
using RegentAgent.Operations;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.Operations.Tests;

public sealed class OperationsApiTests
{
    public static TheoryData<string, string, string?> WriteRequests { get; } = new()
    {
        { "POST", "/ops/status/refresh", null },
        { "POST", "/ops/support-grants", """{"tenantId":"demo","scope":"read-only","minutes":30}""" },
        { "PUT", "/ops/tenants/demo/status", """{"status":"Active"}""" },
        { "PUT", "/ops/backups", """{"enabled":false}""" },
        { "POST", "/ops/observability/alerts/11111111-1111-1111-1111-111111111111/acknowledge", """{"operator":"Test SRE"}""" },
        { "POST", "/ops/observability/alerts/silence", """{"fingerprint":"viewer-test","reason":"Maintenance","minutes":15}""" },
        { "PUT", "/ops/observability/profile", """{"mode":"local","serviceName":"regent-operations","metricIntervalSeconds":30}""" },
        { "POST", "/ops/restores", """{"backupIds":["11111111-1111-1111-1111-111111111111"],"scope":"Full"}""" }
    };

    [Theory]
    [InlineData("Development", null, HttpStatusCode.NotFound)]
    [InlineData("Development", false, HttpStatusCode.NotFound)]
    [InlineData("Development", true, HttpStatusCode.OK)]
    [InlineData("Staging", true, HttpStatusCode.NotFound)]
    [InlineData("Production", true, HttpStatusCode.NotFound)]
    public async Task Demo_login_requires_development_and_explicit_opt_in(
        string environment, bool? allowDemoLogin, HttpStatusCode expectedStatus)
    {
        using var factory = CreateFactory(environment, allowDemoLogin);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/auth/dev-login", null);

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.NotFound)
        {
            Assert.False(response.Headers.Contains("Set-Cookie"));
            return;
        }

        var cookies = response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0].Split('=', 2)[0])
            .ToArray();
        Assert.Equal(["regent.ops"], cookies);
    }

    [Fact]
    public void Tracked_configuration_defaults_demo_login_to_false()
    {
        using var factory = CreateFactory("Development", null);

        var allowDemoLogin = factory.Services.GetRequiredService<IConfiguration>()
            .GetValue<bool>("Operations:AllowDemoLogin");

        Assert.False(allowDemoLogin);
    }

    [Fact]
    public void Every_ops_endpoint_has_reader_metadata_and_every_write_also_has_writer_metadata()
    {
        using var factory = CreateFactory("Development", true);

        var endpoints = GetOpsEndpoints(factory);

        Assert.Equal(30, endpoints.Length);
        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/ops/auth/me");
        Assert.Equal(12, endpoints.Count(endpoint => HasMethod(endpoint, HttpMethods.Get)));
        Assert.Equal(14, endpoints.Count(endpoint => HasMethod(endpoint, HttpMethods.Post)));
        Assert.Equal(4, endpoints.Count(endpoint => HasMethod(endpoint, HttpMethods.Put)));
        Assert.All(endpoints, endpoint =>
        {
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(authorization => authorization.Policy)
                .ToArray();
            var method = Assert.Single(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []);
            Assert.Contains(OperatorAuthorization.ReaderPolicyName, policies);

            if (HttpMethods.IsGet(method))
                Assert.DoesNotContain(OperatorAuthorization.WriterPolicyName, policies);
            else
            {
                Assert.True(HttpMethods.IsPost(method) || HttpMethods.IsPut(method), $"Unexpected method {method}.");
                Assert.Contains(OperatorAuthorization.WriterPolicyName, policies);
            }
        });
    }

    private static bool HasMethod(RouteEndpoint endpoint, string method) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true;

    [Theory]
    [InlineData("/ops/auth/me")]
    [InlineData("/ops/tenants")]
    [InlineData("/ops/backups")]
    [InlineData("/ops/audit")]
    public async Task Anonymous_principals_cannot_invoke_ops_endpoints(string path)
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/ops/auth/me")]
    [InlineData("/ops/tenants")]
    [InlineData("/ops/backups")]
    [InlineData("/ops/audit")]
    public async Task Authenticated_non_operators_cannot_invoke_ops_endpoints(string path)
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "Owner"));

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(WriteRequests))]
    public async Task Ops_viewer_can_read_but_cannot_invoke_representative_writes(string method, string path, string? json)
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "Ops Viewer"));

        using var readResponse = await client.GetAsync("/ops/auth/me");
        using var writeResponse = await SendWriteAsync(client, method, path, json);

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, writeResponse.StatusCode);
    }

    [Theory]
    [MemberData(nameof(WriteRequests))]
    public async Task Anonymous_principals_cannot_invoke_ops_writes(string method, string path, string? json)
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();

        using var response = await SendWriteAsync(client, method, path, json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(WriteRequests))]
    public async Task Authenticated_non_operators_cannot_invoke_ops_writes(string method, string path, string? json)
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "Owner"));

        using var response = await SendWriteAsync(client, method, path, json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Sre_can_read_and_write_ops_resources()
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "SRE"));

        using var sessionResponse = await client.GetAsync("/ops/auth/me");
        using var tenantsResponse = await client.GetAsync("/ops/tenants");
        var tenantApproval = await factory.Services.GetRequiredService<OperationsService>()
            .IssueTenantApprovalAsync("demo", "read-only", "tenant-owner", 30, CancellationToken.None);
        using var grantResponse = await SendWriteAsync(client, "POST", "/ops/support-grants",
            $$"""{"tenantId":"demo","scope":"read-only","minutes":30,"approvalRef":"{{tenantApproval.Reference}}"}""");
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tenantsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, grantResponse.StatusCode);
        using var grant = JsonDocument.Parse(await grantResponse.Content.ReadAsStringAsync());
        var grantId = grant.RootElement.GetProperty("id").GetString()!;

        var approverClient = factory.CreateClient();
        approverClient.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "SRE", operatorId: "second-operator"));
        using var approveResponse = await SendWriteAsync(approverClient, "POST", $"/ops/support-grants/{grantId}/approve", null);
        using var tenantResponse = await SendWriteAsync(client, "PUT", "/ops/tenants/demo/status", """{"status":"Active"}""");
        using var sloResponse = await SendWriteAsync(client, "POST", "/ops/observability/slos",
            """{"name":"sre_writer_test","metric":"api.request","target":0.99,"windowDays":30}""");

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tenantResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sloResponse.StatusCode);
        using var silence = await SendWriteAsync(client, "POST", "/ops/observability/alerts/silence",
            """{"fingerprint":"sre-writer-test","reason":"Maintenance","minutes":15}""");
        using var forgedSilence = await SendWriteAsync(client, "POST", "/ops/observability/alerts/silence",
            """{"fingerprint":"forged-authority-test","reason":"Maintenance","approvedBy":"forged-operator","minutes":15}""");
        Assert.Equal(HttpStatusCode.OK, silence.StatusCode);
        Assert.Equal(HttpStatusCode.OK, forgedSilence.StatusCode);
        var silences = await factory.Services.GetRequiredService<JsonDocumentStore<AlertSilenceRecord>>()
            .GetAllAsync(CancellationToken.None);
        Assert.Equal(2, silences.Count);
        Assert.All(silences, record => Assert.Equal("test-operator", record.ApprovedBy));
    }

    [Fact]
    public async Task A_valid_ticket_in_the_tenant_cookie_is_not_an_operator_session()
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "SRE", "regent.session"));

        using var response = await client.GetAsync("/ops/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("SRE")]
    [InlineData("Ops Viewer")]
    public async Task Operator_reader_roles_can_invoke_every_ops_get(string role)
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, role));
        var readPaths = GetOpsGetPaths(factory);
        Assert.Equal(12, readPaths.Length);

        foreach (var path in readPaths)
        {
            using var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"{role} GET {path} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            if (path != "/ops/auth/me") continue;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(role, document.RootElement.GetProperty("role").GetString());
            Assert.False(document.RootElement.TryGetProperty("tenant_id", out _));
        }
    }

    [Fact]
    public async Task Backup_toggle_patches_the_loaded_profile_without_replacing_configuration()
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "SRE"));
        using var configure = await SendWriteAsync(client, "PUT", "/ops/backup-profile", """
            {
              "id": "default", "deploymentId": "custom-deployment", "enabled": true,
              "components": "postgres,postgres_pitr,rustfs,keyvault,offsite,restore_drills",
              "retentionDays": 73, "postgresFull": true, "postgresPitr": true, "rustFs": true,
              "keyVault": true, "offsite": true, "restoreDrills": true,
              "schedule": "weekly", "targetRef": "offsite://backup-target", "version": 7
            }
            """);
        Assert.Equal(HttpStatusCode.OK, configure.StatusCode);
        int configuredVersion;
        using (var document = JsonDocument.Parse(await configure.Content.ReadAsStringAsync()))
            configuredVersion = document.RootElement.GetProperty("version").GetInt32();

        using var disable = await SendWriteAsync(client, "PUT", "/ops/backups", """{"enabled":false}""");

        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        using var disabledDocument = JsonDocument.Parse(await disable.Content.ReadAsStringAsync());
        var disabled = Assert.Single(disabledDocument.RootElement.EnumerateArray());
        Assert.Equal("default", disabled.GetProperty("id").GetString());
        Assert.Equal("custom-deployment", disabled.GetProperty("deploymentId").GetString());
        Assert.False(disabled.GetProperty("enabled").GetBoolean());
        Assert.Equal("postgres,postgres_pitr,rustfs,keyvault,offsite,restore_drills", disabled.GetProperty("components").GetString());
        Assert.Equal(73, disabled.GetProperty("retentionDays").GetInt32());
        Assert.True(disabled.GetProperty("postgresFull").GetBoolean());
        Assert.True(disabled.GetProperty("postgresPitr").GetBoolean());
        Assert.True(disabled.GetProperty("rustFs").GetBoolean());
        Assert.True(disabled.GetProperty("keyVault").GetBoolean());
        Assert.True(disabled.GetProperty("offsite").GetBoolean());
        Assert.True(disabled.GetProperty("restoreDrills").GetBoolean());
        Assert.Equal("weekly", disabled.GetProperty("schedule").GetString());
        Assert.Equal("offsite://backup-target", disabled.GetProperty("targetRef").GetString());
        Assert.Equal(configuredVersion + 1, disabled.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Support_grant_rejects_an_unknown_tenant()
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "SRE"));

        using var response = await SendWriteAsync(client, "POST", "/ops/support-grants",
            """{"tenantId":"missing-tenant","scope":"read-only","minutes":30}""");
        using var grants = await client.GetAsync("/ops/support-grants");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, grants.StatusCode);
        using var document = JsonDocument.Parse(await grants.Content.ReadAsStringAsync());
        Assert.Empty(document.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Support_grants_require_tenant_approval_and_reject_self_approval()
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "SRE"));
        var service = factory.Services.GetRequiredService<OperationsService>();
        var approval = await service.IssueTenantApprovalAsync("demo", "read-only", "tenant-owner", 30, CancellationToken.None);

        using var invalidScope = await SendWriteAsync(client, "POST", "/ops/support-grants",
            $$"""{"tenantId":"demo","scope":"*","minutes":30,"approvalRef":"{{approval.Reference}}"}""");
        using var invalidDuration = await SendWriteAsync(client, "POST", "/ops/support-grants",
            $$"""{"tenantId":"demo","scope":"read-only","minutes":525600,"approvalRef":"{{approval.Reference}}"}""");
        Assert.Equal(HttpStatusCode.Conflict, invalidScope.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, invalidDuration.StatusCode);

        using var created = await SendWriteAsync(client, "POST", "/ops/support-grants",
            $$"""{"tenantId":"demo","scope":"read-only","minutes":30,"approvalRef":"{{approval.Reference}}"}""");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var grantId = document.RootElement.GetProperty("id").GetString()!;
        using var selfApproval = await SendWriteAsync(client, "POST", $"/ops/support-grants/{grantId}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, selfApproval.StatusCode);

        using var approverClient = factory.CreateClient();
        approverClient.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "SRE", operatorId: "second-operator"));
        using var approved = await SendWriteAsync(approverClient, "POST", $"/ops/support-grants/{grantId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        using var approvedDocument = JsonDocument.Parse(await approved.Content.ReadAsStringAsync());
        Assert.Equal("second-operator", approvedDocument.RootElement.GetProperty("approvedBy").GetString());
    }

    [Fact]
    public async Task Full_restore_requires_every_component_in_the_profile()
    {
        using var factory = CreateFactory("Development", true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "SRE"));
        var backups = await CreateCompletedBackupSetAsync(factory, client, includeKeyVault: true);
        var rustfs = backups.Single(backup => backup.Component == "rustfs");

        var backupIds = string.Join(",", backups.Select(backup => $"\"{backup.Id}\""));
        using var incomplete = await SendWriteAsync(client, "POST", "/ops/restores",
            $$"""{"backupIds":["{{rustfs.Id}}"],"scope":"Full"}""");
        using var complete = await SendWriteAsync(client, "POST", "/ops/restores",
            $$"""{"backupIds":[{{backupIds}}],"scope":"Full","requester":"Forged Requester"}""");

        Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        using var document = JsonDocument.Parse(await complete.Content.ReadAsStringAsync());
        Assert.Equal("test-operator", document.RootElement.GetProperty("requester").GetString());
        Assert.Equal("PendingApproval", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Restore_approval_uses_authenticated_operators_and_rejects_self_approval()
    {
        using var factory = CreateFactory("Development", true);
        using var requesterClient = factory.CreateClient();
        requesterClient.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "SRE"));
        var backups = await CreateCompletedBackupSetAsync(factory, requesterClient, includeKeyVault: false);
        var backup = Assert.Single(backups);
        using var request = await SendWriteAsync(requesterClient, "POST", "/ops/restores",
            $$"""{"backupIds":["{{backup.Id}}"],"scope":"Tenant","requester":"Forged Requester"}""");
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        string restoreId;
        using (var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync()))
        {
            restoreId = document.RootElement.GetProperty("id").GetString()!;
            Assert.Equal("test-operator", document.RootElement.GetProperty("requester").GetString());
        }

        using var selfApproval = await SendWriteAsync(requesterClient, "POST", $"/ops/restores/{restoreId}/approve",
            """{"resolution":"Isolated verification"}""");
        using var pending = await requesterClient.GetAsync("/ops/restores");
        Assert.Equal(HttpStatusCode.Conflict, selfApproval.StatusCode);
        using var pendingDocument = JsonDocument.Parse(await pending.Content.ReadAsStringAsync());
        var pendingRestore = Assert.Single(pendingDocument.RootElement.EnumerateArray());
        Assert.Equal("PendingApproval", pendingRestore.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, pendingRestore.GetProperty("approver").ValueKind);

        using var approverClient = factory.CreateClient();
        approverClient.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, "SRE", operatorId: "second-operator"));
        using var approval = await SendWriteAsync(approverClient, "POST", $"/ops/restores/{restoreId}/approve",
            """{"resolution":"Isolated verification"}""");
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        using var approvedDocument = JsonDocument.Parse(await approval.Content.ReadAsStringAsync());
        Assert.Equal("second-operator", approvedDocument.RootElement.GetProperty("approver").GetString());
        Assert.Equal("Verified", approvedDocument.RootElement.GetProperty("status").GetString());
    }

    private static RouteEndpoint[] GetOpsEndpoints(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is string route
                && (route == "/ops" || route.StartsWith("/ops/", StringComparison.Ordinal)))
            .ToArray();

    private static string[] GetOpsGetPaths(WebApplicationFactory<Program> factory) =>
        GetOpsEndpoints(factory)
            .Where(endpoint => HasMethod(endpoint, HttpMethods.Get))
            .Select(endpoint => endpoint.RoutePattern.RawText!
                .Replace("{profileId}", "default", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static async Task<HttpResponseMessage> SendWriteAsync(HttpClient client, string method, string path, string? json)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static OperationsWebApplicationFactory CreateFactory(string environment, bool? allowDemoLogin) =>
        new(environment, allowDemoLogin);

    private async Task<IReadOnlyList<(Guid Id, string Component)>> CreateCompletedBackupSetAsync(
        OperationsWebApplicationFactory factory, HttpClient client, bool includeKeyVault)
    {
        var components = includeKeyVault ? "rustfs,keyvault" : "rustfs";
        var keyVault = includeKeyVault ? "true" : "false";
        if (includeKeyVault) await File.WriteAllTextAsync(Path.Combine(factory.SourcePath, "keyvault.json"), "{}");
        using var configure = await SendWriteAsync(client, "PUT", "/ops/backup-profile",
            $$"""
            {
              "id": "default", "deploymentId": "local", "enabled": true,
              "components": "{{components}}", "retentionDays": 35,
              "rustFs": true, "keyVault": {{keyVault}}, "restoreDrills": true,
              "schedule": "daily", "targetRef": "", "version": 1
            }
            """);
        Assert.Equal(HttpStatusCode.OK, configure.StatusCode);
        using var run = await SendWriteAsync(client, "POST", "/ops/backups/run", """{"profileId":"default"}""");
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        using var document = JsonDocument.Parse(await run.Content.ReadAsStringAsync());
        var jobs = document.RootElement.EnumerateArray()
            .Select(job => (Id: job.GetProperty("id").GetGuid(), Component: job.GetProperty("component").GetString()!,
                Status: job.GetProperty("status").GetString()))
            .ToArray();
        Assert.Equal(includeKeyVault ? 2 : 1, jobs.Length);
        Assert.Contains(jobs, job => job.Component == "rustfs");
        Assert.All(jobs, job => Assert.Equal("Completed", job.Status));
        return jobs.Select(job => (job.Id, job.Component)).ToArray();
    }

    private static string CreateCookie(WebApplicationFactory<Program> factory, string role, string cookieName = "regent.ops",
        string operatorId = "test-operator")
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, operatorId),
            new Claim(ClaimTypes.Name, "Test Operator"),
            new Claim(ClaimTypes.Email, "operator@example.test"),
            new Claim(ClaimTypes.Role, role),
            new Claim("auth_method", "Cookie"),
            new Claim("session_kind", "Operator")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = options.TicketDataFormat.Protect(
            new AuthenticationTicket(principal, CookieAuthenticationDefaults.AuthenticationScheme));
        return $"{cookieName}={Uri.EscapeDataString(ticket)}";
    }

    private sealed class OperationsWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _environment;
        private readonly bool? _allowDemoLogin;
        private readonly string _dataPath = Path.Combine(Path.GetTempPath(), $"regent-operations-tests-{Guid.NewGuid():N}");
        private readonly string _sourcePath;

        public string SourcePath => _sourcePath;

        public OperationsWebApplicationFactory(string environment, bool? allowDemoLogin)
        {
            _environment = environment;
            _allowDemoLogin = allowDemoLogin;
            _sourcePath = Path.Combine(_dataPath, "source");
            Directory.CreateDirectory(_dataPath);
            Directory.CreateDirectory(Path.Combine(_sourcePath, "objects"));
            File.WriteAllText(Path.Combine(_sourcePath, "objects", ".snapshot-verified"), "verified");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            builder.UseSetting("Data:OperationsPath", _dataPath);
            builder.UseSetting("Backup:SourceRoot", _sourcePath);
            builder.UseSetting("Telemetry:IngestToken", "operations-tests-only");
            if (_allowDemoLogin.HasValue)
                builder.UseSetting("Operations:AllowDemoLogin", _allowDemoLogin.Value.ToString());
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                if (disposing && Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true);
            }
        }
    }
}
