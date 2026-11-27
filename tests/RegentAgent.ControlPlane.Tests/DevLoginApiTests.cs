extern alias RegentAgentControlPlane;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using Xunit;
using ControlPlaneProgram = RegentAgentControlPlane::Program;

namespace RegentAgent.ControlPlane.Tests;

[CollectionDefinition("Dev login HTTP", DisableParallelization = true)]
public sealed class DevLoginHttpCollection;

[Collection("Dev login HTTP")]
public sealed class DevLoginApiTests
{
    [Theory]
    [InlineData("Development", null, HttpStatusCode.NotFound)]
    [InlineData("Development", false, HttpStatusCode.NotFound)]
    [InlineData("Development", true, HttpStatusCode.OK)]
    [InlineData("Staging", true, HttpStatusCode.NotFound)]
    [InlineData("Production", true, HttpStatusCode.NotFound)]
    public async Task Demo_login_requires_development_and_explicit_opt_in(
        string environment, bool? allowDemoLogin, HttpStatusCode expectedStatus)
    {
        await using var factory = await CreateFactoryAsync(environment, allowDemoLogin);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/auth/dev-login", new { email = "owner@example.test" });

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.NotFound)
        {
            AssertNotFoundWithoutSessionCookie(response);
            return;
        }

        var cookies = response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0].Split('=', 2)[0])
            .ToArray();
        Assert.Equal(["regent.session"], cookies);

        using var sessionResponse = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        using var session = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        Assert.Equal("Owner", session.RootElement.GetProperty("role").GetString());
        Assert.Equal("Development", session.RootElement.GetProperty("auth_method").GetString());
    }

    [Theory]
    [InlineData("Development", null, "")]
    [InlineData("Development", false, "")]
    [InlineData("Development", null, "{invalid")]
    [InlineData("Development", false, "{invalid")]
    [InlineData("Production", true, "")]
    [InlineData("Production", true, "{invalid")]
    [InlineData("Staging", true, "")]
    [InlineData("Staging", true, "{invalid")]
    public async Task Disabled_demo_login_returns_not_found_before_binding_request_body(
        string environment, bool? allowDemoLogin, string body)
    {
        await using var factory = await CreateFactoryAsync(environment, allowDemoLogin);
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/auth/dev-login", content);

        AssertNotFoundWithoutSessionCookie(response);
    }

    [Fact]
    public void Tracked_configuration_defaults_demo_login_to_false()
    {
        using var factory = new DevLoginWebApplicationFactory("Development", null);

        var allowDemoLogin = factory.Services.GetRequiredService<IConfiguration>()
            .GetValue<bool>("Identity:AllowDemoLogin");

        Assert.False(allowDemoLogin);
    }

    private static void AssertNotFoundWithoutSessionCookie(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private static async Task<DevLoginWebApplicationFactory> CreateFactoryAsync(string environment, bool? allowDemoLogin)
    {
        var factory = new DevLoginWebApplicationFactory(environment, allowDemoLogin);
        try
        {
            await factory.InitializeAsync();
            return factory;
        }
        catch
        {
            factory.Dispose();
            throw;
        }
    }

    private sealed class DevLoginWebApplicationFactory : WebApplicationFactory<ControlPlaneProgram>
    {
        private readonly string _environment;
        private readonly bool? _allowDemoLogin;
        private readonly string _previousCurrentDirectory = Directory.GetCurrentDirectory();
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"regent-dev-login-tests-{Guid.NewGuid():N}");
        private readonly string _contentRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RegentAgent.ControlPlane"));
        private string DatabasePath => Path.Combine(_root, "regent.db");

        public DevLoginWebApplicationFactory(string environment, bool? allowDemoLogin)
        {
            _environment = environment;
            _allowDemoLogin = allowDemoLogin;
            Directory.CreateDirectory(_root);
            Directory.SetCurrentDirectory(_root);
        }

        public async Task InitializeAsync()
        {
            if (_environment == "Development") return;

            var options = new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={DatabasePath}")
                .Options;
            await using var db = new RegentDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.SchemaVersions.Add(new SchemaVersionRecord
            {
                Version = SchemaVersionService.CurrentVersion,
                Name = "test-schema-baseline",
                AppliedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(_contentRoot);
            builder.UseEnvironment(_environment);
            builder.UseSetting("Data:OperationsPath", Path.Combine(_root, "ops"));
            builder.UseSetting("Storage:ObjectRoot", Path.Combine(_root, "objects"));
            if (_environment == "Development")
            {
                builder.UseSetting("Storage:Provider", "InMemory");
                builder.UseSetting("Storage:UseInMemory", "true");
            }
            else
            {
                builder.UseSetting("Storage:Provider", "Sqlite");
                builder.UseSetting("Storage:UseInMemory", "false");
                builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={DatabasePath}");
                builder.UseSetting("Runner:Token", "controlplane-tests-only");
                builder.UseSetting("Metering:Token", "controlplane-tests-only");
            }

            if (_allowDemoLogin.HasValue)
                builder.UseSetting("Identity:AllowDemoLogin", _allowDemoLogin.Value.ToString());
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                if (disposing)
                {
                    Directory.SetCurrentDirectory(_previousCurrentDirectory);
                    if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
                }
            }
        }
    }
}
