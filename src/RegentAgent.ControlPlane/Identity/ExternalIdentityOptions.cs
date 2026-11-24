using RegentAgent.Contracts.Identity;

namespace RegentAgent.ControlPlane.Identity;

public sealed class ExternalIdentityOptions
{
    public string Mode { get; set; } = "local";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxResponseBytes { get; set; } = 1024 * 1024;
    public GitHubIdentityOptions GitHub { get; set; } = new();
    public OidcIdentityOptions Oidc { get; set; } = new();
}

public sealed class GitHubIdentityOptions
{
    public string TokenUrl { get; set; } = "https://github.com/login/oauth/access_token";
    public string UserUrl { get; set; } = "https://api.github.com/user";
    public string EmailsUrl { get; set; } = "https://api.github.com/user/emails";
}

public sealed class OidcIdentityOptions
{
    public string[] AllowedAlgorithms { get; set; } = ["RS256"];
    public int ClockSkewSeconds { get; set; } = 60;
    public bool RequireNonce { get; set; } = true;
}

public interface IExternalIdentitySecretProvider
{
    Task<string> GetSecretAsync(string secretRef, CancellationToken cancellationToken);
}

public sealed class ConfigurationIdentitySecretProvider(IConfiguration configuration) : IExternalIdentitySecretProvider
{
    public Task<string> GetSecretAsync(string secretRef, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(secretRef)) return Task.FromResult(string.Empty);
        return Task.FromResult(configuration[$"Identity:Secrets:{secretRef}"] ?? string.Empty);
    }
}

public sealed class ExternalIdentityProviderResolver(
    IEnumerable<IExternalIdentityProvider> providers,
    ExternalIdentityOptions options) : IExternalIdentityProviderResolver
{
    private readonly IExternalIdentityProvider[] providers = providers.ToArray();

    public IExternalIdentityProvider Resolve(string provider)
    {
        var mode = options.Mode.Trim().ToLowerInvariant();
        if (mode is not ("local" or "remote")) throw new InvalidOperationException("External identity mode must be local or remote.");
        var candidate = mode == "local"
            ? providers.SingleOrDefault(item => item.Provider.Equals("local", StringComparison.OrdinalIgnoreCase))
            : providers.FirstOrDefault(item => item.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));
        return candidate ?? throw new InvalidOperationException($"No identity adapter handles {provider} in {mode} mode.");
    }
}

public sealed class LocalExternalIdentityProvider : IExternalIdentityProvider
{
    public string Provider => "local";

    public Task<ExternalIdentityResult> AuthenticateAsync(ExternalIdentityRequest request,
        ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.Issuer, configuration.Issuer, StringComparison.OrdinalIgnoreCase))
            throw new ExternalIdentityException("Identity issuer does not match the tenant configuration.");
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Email))
            throw new ExternalIdentityException("A test issuer, subject and email are required in local identity mode.");
        return Task.FromResult(new ExternalIdentityResult(request.Issuer, request.Subject,
            request.Email, true, request.DisplayName ?? request.Email, new Dictionary<string, string>
            {
                ["iss"] = request.Issuer, ["sub"] = request.Subject, ["email"] = request.Email
            }));
    }
}
