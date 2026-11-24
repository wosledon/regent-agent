namespace RegentAgent.Contracts.Identity;

public sealed record ExternalIdentityRequest(
    Guid TenantId,
    string Provider,
    string Issuer,
    string? Subject,
    string? Email,
    string? DisplayName,
    string? Code = null,
    string? IdToken = null,
    string? CodeVerifier = null,
    string? Nonce = null);

public sealed record ExternalIdentityProviderConfiguration(
    string Issuer,
    string ClientId,
    string ClientSecretRef,
    string RedirectUri,
    string Scopes,
    string GroupClaim);

public sealed record ExternalAuthorizationParameters(
    string State,
    string Nonce,
    string CodeChallenge,
    string RedirectUri,
    string? AcrValues = null,
    int? MaxAgeSeconds = null,
    string? Prompt = null);

public sealed record ExternalIdentityResult(
    string Issuer,
    string Subject,
    string Email,
    bool EmailVerified,
    string DisplayName,
    IReadOnlyDictionary<string, string> Claims)
{
    public IReadOnlyList<string> Groups { get; init; } = [];
    public string? Acr { get; init; }
    public IReadOnlyList<string> Amr { get; init; } = [];
    public DateTimeOffset? AuthTime { get; init; }
}

public interface IExternalIdentityProvider
{
    string Provider { get; }
    Task<ExternalIdentityResult> AuthenticateAsync(ExternalIdentityRequest request,
        ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken);
    Task<string> BuildAuthorizationUrlAsync(ExternalAuthorizationParameters parameters,
        ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken) =>
        throw new ExternalIdentityException("Authorization URL generation is not supported by this adapter.");
}

public interface IExternalIdentityProviderResolver
{
    IExternalIdentityProvider Resolve(string provider);
}

public sealed class ExternalIdentityException(string message, Exception? innerException = null)
    : Exception(message, innerException);
