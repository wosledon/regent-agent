using System.Diagnostics;
using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;

namespace RegentAgent.Operations;

public sealed class PlatformStatusService(
    JsonDocumentStore<PlatformComponentStatus> components,
    JsonDocumentStore<PlatformIncident> incidents,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    TimeProvider clock)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await components.GetAllAsync(cancellationToken)).Count > 0) return;
        foreach (var name in new[] { "ControlPlane", "Gateway", "Operations", "LocalStorage" })
            await components.PutAsync(name, new PlatformComponentStatus
            {
                Name = name, Status = "Unknown", CheckedAt = clock.GetUtcNow(), Message = "Not checked yet."
            }, cancellationToken);
    }

    public async Task<PlatformStatusDto> GetAsync(CancellationToken cancellationToken)
    {
        await SeedAsync(cancellationToken);
        var componentList = (await components.GetAllAsync(cancellationToken)).OrderBy(x => x.Name).ToArray();
        var incidentList = (await incidents.GetAllAsync(cancellationToken)).Where(x => x.Status == "Open")
            .OrderByDescending(x => x.StartedAt).ToArray();
        var overall = incidentList.Any(x => x.Severity is "SEV1" or "SEV2") ? "MajorOutage"
            : componentList.Any(x => x.Status == "Down") ? "Degraded"
            : componentList.All(x => x.Status == "Operational") ? "Operational" : "Unknown";
        return new PlatformStatusDto(overall, clock.GetUtcNow(), componentList, incidentList);
    }

    public async Task<PlatformStatusDto> RefreshAsync(CancellationToken cancellationToken)
    {
        await CheckServiceAsync("ControlPlane", configuration["Services:ControlPlane"] ?? "http://localhost:5080", cancellationToken);
        await CheckServiceAsync("Gateway", configuration["Services:Gateway"] ?? "http://localhost:5081", cancellationToken);
        await components.PutAsync("Operations", new PlatformComponentStatus
        {
            Name = "Operations", Status = "Operational", LatencyMs = 0,
            CheckedAt = clock.GetUtcNow(), Message = "Local operations host is running."
        }, cancellationToken);
        await components.PutAsync("LocalStorage", new PlatformComponentStatus
        {
            Name = "LocalStorage", Status = "Operational", LatencyMs = 0,
            CheckedAt = clock.GetUtcNow(), Message = "Local filesystem storage is available."
        }, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<PlatformIncident> CreateIncidentAsync(PlatformIncident incident, CancellationToken cancellationToken)
    {
        if (incident.Severity is not ("SEV1" or "SEV2" or "SEV3" or "SEV4")) throw new InvalidOperationException("Invalid severity.");
        incident.StartedAt = clock.GetUtcNow();
        incident.Status = "Open";
        await incidents.PutAsync(incident.Id.ToString(), incident, cancellationToken);
        return incident;
    }

    public async Task<PlatformIncident> ResolveIncidentAsync(Guid id, CancellationToken cancellationToken)
    {
        var incident = await incidents.GetAsync(id.ToString(), cancellationToken) ?? throw new KeyNotFoundException("Incident not found.");
        incident.Status = "Resolved";
        incident.ResolvedAt = clock.GetUtcNow();
        await incidents.PutAsync(incident.Id.ToString(), incident, cancellationToken);
        return incident;
    }

    private async Task CheckServiceAsync(string name, string baseUrl, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var client = httpClientFactory.CreateClient($"status-{name}");
            client.Timeout = TimeSpan.FromSeconds(3);
            using var response = await client.GetAsync(new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "health/ready"), cancellationToken);
            await components.PutAsync(name, new PlatformComponentStatus
            {
                Name = name, Status = response.IsSuccessStatusCode ? "Operational" : "Degraded",
                LatencyMs = (int)stopwatch.ElapsedMilliseconds, CheckedAt = clock.GetUtcNow(),
                Message = response.IsSuccessStatusCode ? "Health check passed." : "Health check returned a non-success status."
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            await components.PutAsync(name, new PlatformComponentStatus
            {
                Name = name, Status = "Down", LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                CheckedAt = clock.GetUtcNow(), Message = "Health check failed."
            }, cancellationToken);
        }
    }
}
