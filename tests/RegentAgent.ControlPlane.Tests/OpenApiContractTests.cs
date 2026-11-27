extern alias RegentAgentControlPlane;
extern alias RegentAgentGateway;
extern alias RegentAgentOperations;

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ControlPlaneProgram = RegentAgentControlPlane::Program;
using GatewayProgram = RegentAgentGateway::Program;
using OperationsProgram = RegentAgentOperations::Program;

namespace RegentAgent.ControlPlane.Tests;

[CollectionDefinition("OpenAPI contract", DisableParallelization = true)]
public sealed class OpenApiContractCollection;

[Collection("OpenAPI contract")]
public sealed class OpenApiContractTests
{
    [Fact]
    public async Task Checked_in_contract_matches_runtime_schemas_and_status_codes()
    {
        await using var factory = new ControlPlaneRuntimeFactory("Development", "InMemory", true);
        using var client = factory.CreateClient();
        var actual = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var path = Path.Combine(PostgresTestSupport.RepositoryRoot, "contracts", "openapi", "controlplane.json");
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(path));
        Assert.Null(actual!["servers"]);
        Assert.Null(expected!["servers"]);

        Assert.True(JsonNode.DeepEquals(expected, actual),
            "Regenerate contracts/openapi/controlplane.json and the TypeScript schema from the running application.");
    }

    [Theory]
    [InlineData("controlplane")]
    [InlineData("operations")]
    [InlineData("gateway")]
    public async Task Every_published_document_omits_environment_servers(string document)
    {
        JsonNode actual;
        if (document == "controlplane")
        {
            await using var factory = new ControlPlaneRuntimeFactory("Development", "InMemory", true);
            using var client = factory.CreateClient();
            actual = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        }
        else if (document == "operations")
        {
            using var factory = new OperationsOpenApiFactory();
            using var client = factory.CreateClient();
            actual = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        }
        else
        {
            using var factory = new GatewayOpenApiFactory();
            using var client = factory.CreateClient();
            actual = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        }

        Assert.Null(actual["servers"]);
        Assert.NotNull(actual["paths"]);
        Assert.NotEmpty(actual["paths"]!.AsObject());
    }

    private sealed class OperationsOpenApiFactory : WebApplicationFactory<OperationsProgram>
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"regent-ops-openapi-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(Path.Combine(root, "source"));
            builder.UseEnvironment("Development");
            builder.UseSetting("Data:OperationsPath", Path.Combine(root, "ops"));
            builder.UseSetting("Backup:SourceRoot", Path.Combine(root, "source"));
            builder.UseSetting("Telemetry:IngestToken", "openapi-tests-only");
        }

        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally
            {
                if (disposing && Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class GatewayOpenApiFactory : WebApplicationFactory<GatewayProgram>
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"regent-gateway-openapi-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(root);
            builder.UseEnvironment("Development");
            builder.UseSetting("Gateway:UsagePath", Path.Combine(root, "usage.jsonl"));
            builder.UseSetting("Gateway:UsageProjectionPath", Path.Combine(root, "usage-projections.json"));
            builder.UseSetting("Gateway:BudgetPath", Path.Combine(root, "budget.json"));
            builder.UseSetting("Gateway:KeyVaultPath", Path.Combine(root, "keyvault.json"));
            builder.UseSetting("Gateway:MasterKeyPath", Path.Combine(root, "master.key"));
            builder.UseSetting("Gateway:ControlPlaneUrl", "http://localhost:5080");
            builder.UseSetting("Gateway:MeteringToken", "openapi-tests-only");
        }

        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally
            {
                if (disposing && Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }
}
