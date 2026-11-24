using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using RegentAgent.Contracts.Identity;

namespace RegentAgent.ControlPlane.Identity;

public sealed class OidcExternalIdentityProvider(
    HttpClient http,
    ExternalIdentityOptions options) : IExternalIdentityProvider
{
    private readonly HttpClient http = http;
    private readonly ExternalIdentityOptions options = options;

    public string Provider => "Oidc";

    public async Task<ExternalIdentityResult> AuthenticateAsync(ExternalIdentityRequest request,
        ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken)) throw new ExternalIdentityException("OIDC ID token is required.");
        if (string.IsNullOrWhiteSpace(configuration.Issuer) || string.IsNullOrWhiteSpace(configuration.ClientId))
            throw new ExternalIdentityException("OIDC issuer and client id are required.");
        if (options.Oidc.RequireNonce && string.IsNullOrWhiteSpace(request.Nonce))
            throw new ExternalIdentityException("OIDC nonce is required.");
        IdentityHttpContent.ValidateEndpoint(configuration.Issuer);
        try
        {
            var issuer = configuration.Issuer.TrimEnd('/');
            var discoveryUrl = issuer + "/.well-known/openid-configuration";
            IdentityHttpContent.ValidateEndpoint(discoveryUrl);
            var documentRetriever = new HttpDocumentRetriever(http)
            {
                RequireHttps = discoveryUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            };
            var metadata = await OpenIdConnectConfigurationRetriever
                .GetAsync(discoveryUrl, documentRetriever, cancellationToken);
            if (string.IsNullOrWhiteSpace(metadata.JwksUri))
                throw new ExternalIdentityException("OIDC discovery document does not contain a JWKS endpoint.");
            IdentityHttpContent.ValidateEndpoint(metadata.JwksUri);
            using var jwksRequest = new HttpRequestMessage(HttpMethod.Get, metadata.JwksUri);
            using var jwksResponse = await http.SendAsync(jwksRequest, cancellationToken);
            var jwksJson = await IdentityHttpContent.ReadBoundedAsync(jwksResponse, options.MaxResponseBytes, cancellationToken);
            if (!jwksResponse.IsSuccessStatusCode)
                throw new ExternalIdentityException($"OIDC JWKS request returned HTTP {(int)jwksResponse.StatusCode}.");
            var keys = new JsonWebKeySet(jwksJson);
            var validation = new TokenValidationParameters
            {
                ValidIssuer = metadata.Issuer,
                ValidAudience = configuration.ClientId,
                IssuerSigningKeys = keys.GetSigningKeys(),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(options.Oidc.ClockSkewSeconds)
            };
            var principal = new JwtSecurityTokenHandler().ValidateToken(request.IdToken, validation, out var validated);
            if (validated is not JwtSecurityToken token || !options.Oidc.AllowedAlgorithms.Contains(token.Header.Alg, StringComparer.OrdinalIgnoreCase))
                throw new ExternalIdentityException("OIDC signing algorithm is not allowed.");
            var claims = token.Claims.GroupBy(claim => claim.Type, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
            if (options.Oidc.RequireNonce && (!claims.TryGetValue("nonce", out var nonce) || nonce != request.Nonce))
                throw new ExternalIdentityException("OIDC nonce does not match.");
            if (!claims.TryGetValue("sub", out var subject) || string.IsNullOrWhiteSpace(subject))
                throw new ExternalIdentityException("OIDC subject is missing.");
            if (!claims.TryGetValue("email", out var email) || string.IsNullOrWhiteSpace(email)
                || !claims.TryGetValue("email_verified", out var verified) || !bool.TryParse(verified, out var emailVerified) || !emailVerified)
                throw new ExternalIdentityException("OIDC did not return a verified email.");
            var displayName = claims.GetValueOrDefault("name") ?? claims.GetValueOrDefault("preferred_username") ?? email;
            return new ExternalIdentityResult(token.Issuer, subject, email, true, displayName, claims);
        }
        catch (ExternalIdentityException) { throw; }
        catch (Exception exception) when (exception is SecurityTokenException or HttpRequestException or InvalidOperationException or ArgumentException)
        {
            throw new ExternalIdentityException("OIDC identity validation failed.", exception);
        }
    }
}
