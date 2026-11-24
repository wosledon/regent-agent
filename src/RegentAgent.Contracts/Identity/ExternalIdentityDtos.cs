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

public sealed record ExternalIdentityResult(
    string Issuer,
    string Subject,
    string Email,
    bool EmailVerified,
    string DisplayName,
    IReadOnlyDictionary<string, string> Claims);

public interface IExternalIdentityProvider
{
    string Provider { get; }
    Task<ExternalIdentityResult> AuthenticateAsync(ExternalIdentityRequest request,
        ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken);
}

public interface IExternalIdentityProviderResolver
{
    IExternalIdentityProvider Resolve(string provider);
}

public sealed class ExternalIdentityException(string message, Exception? innerException = null)
    : Exception(message, innerException);
