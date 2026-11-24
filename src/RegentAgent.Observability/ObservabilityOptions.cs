namespace RegentAgent.Observability;

public sealed class ObservabilityOptions
{
    public string Profile { get; set; } = "local";
    public bool Metrics { get; set; } = true;
    public bool Logs { get; set; } = true;
    public bool Traces { get; set; } = true;
    public double Sampling { get; set; } = 0.1;
    public string Redaction { get; set; } = "strict";
    public string? OtlpEndpoint { get; set; }
    public string? ElasticsearchEndpoint { get; set; }
    public string? ElasticsearchUsername { get; set; }
    public string? ElasticsearchPassword { get; set; }
    public string ServiceName { get; set; } = "regent-agent";
    public string ServiceVersion { get; set; } = "local";

    public void Validate()
    {
        Profile = Profile.Trim().ToLowerInvariant();
        Redaction = Redaction.Trim().ToLowerInvariant();
        if (Profile is not ("off" or "local" or "otlp" or "elastic"))
            throw new InvalidOperationException("Observability profile must be off, local, otlp or elastic.");
        if (Sampling is < 0 or > 1) throw new InvalidOperationException("Observability sampling must be between 0 and 1.");
        if (Redaction.Trim().ToLowerInvariant() != "strict")
            throw new InvalidOperationException("Observability redaction must be strict.");
        if (Profile is "otlp" or "elastic")
            ValidateEndpoint(OtlpEndpoint, "OTLP", allowElasticsearch: Profile == "elastic");
        if (Profile == "elastic")
        {
            ValidateEndpoint(ElasticsearchEndpoint, "Elasticsearch", allowElasticsearch: true);
            if (string.IsNullOrWhiteSpace(ElasticsearchUsername) || string.IsNullOrWhiteSpace(ElasticsearchPassword))
                throw new InvalidOperationException("Elasticsearch username and password are required for the elastic profile.");
        }
    }

    private static void ValidateEndpoint(string? value, string name, bool allowElasticsearch)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"A valid {name} endpoint is required for the selected observability profile.");
        var loopback = uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        var internalHost = uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || uri.Host is "collector" or "otel-collector"
            || (allowElasticsearch && uri.Host.Equals("elasticsearch", StringComparison.OrdinalIgnoreCase));
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && (loopback || internalHost)))
            throw new InvalidOperationException($"{name} endpoint must use HTTPS or an approved internal HTTP endpoint.");
    }
}
