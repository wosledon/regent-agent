using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using RegentAgent.Contracts.Identity;

namespace RegentAgent.ControlPlane.Identity;

public sealed class OidcExternalIdentityProvider(
    HttpClient http,
    ExternalIdentityOptions options,
    IExternalIdentitySecretProvider? secrets = null) : IExternalIdentityProvider
{
    private readonly HttpClient http = http;
    private readonly ExternalIdentityOptions options = options;
    private readonly IExternalIdentitySecretProvider? secrets = secrets;

    public string Provider => "Oidc";

    public async Task<string> BuildAuthorizationUrlAsync(ExternalAuthorizationParameters parameters,
        ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        var metadata = await GetDiscoveryAsync(configuration, cancellationToken);
        if (string.IsNullOrWhiteSpace(metadata.AuthorizationEndpoint))
            throw new ExternalIdentityException("OIDC discovery document does not contain an authorization endpoint.");
        IdentityHttpContent.ValidateEndpoint(metadata.AuthorizationEndpoint);
        return QueryHelpers.AddQueryString(metadata.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = configuration.ClientId,
            ["redirect_uri"] = parameters.RedirectUri,
            ["scope"] = configuration.Scopes,
            ["state"] = parameters.State,
            ["nonce"] = parameters.Nonce,
            ["code_challenge"] = parameters.CodeChallenge,
            ["code_challenge_method"] = "S256",
            ["acr_values"] = parameters.AcrValues,
            ["max_age"] = parameters.MaxAgeSeconds?.ToString(CultureInfo.InvariantCulture),
            ["prompt"] = parameters.Prompt
        });
    }

    private readonly TimeProvider clock = TimeProvider.System;

    public async Task<ExternalIdentityResult> AuthenticateAsync(ExternalIdentityRequest request,
        ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.Issuer) || string.IsNullOrWhiteSpace(configuration.ClientId))
            throw new ExternalIdentityException("OIDC issuer and client id are required.");
        if (options.Oidc.RequireNonce && string.IsNullOrWhiteSpace(request.Nonce))
            throw new ExternalIdentityException("OIDC nonce is required.");
        try
        {
            var metadata = await GetDiscoveryAsync(configuration, cancellationToken);
            var idToken = request.IdToken;
            if (!string.IsNullOrWhiteSpace(request.Code))
                idToken = await ExchangeCodeAsync(request, configuration, metadata, cancellationToken);
            if (string.IsNullOrWhiteSpace(idToken)) throw new ExternalIdentityException("OIDC ID token is required.");
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
            var principal = new JwtSecurityTokenHandler().ValidateToken(idToken, validation, out var validated);
            if (validated is not JwtSecurityToken token || !options.Oidc.AllowedAlgorithms.Contains(token.Header.Alg, StringComparer.OrdinalIgnoreCase))
                throw new ExternalIdentityException("OIDC signing algorithm is not allowed.");
            var claims = token.Claims.GroupBy(claim => claim.Type, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
            if (token.Audiences.Count() > 1 && (!claims.TryGetValue("azp", out var azp) || azp != configuration.ClientId))
                throw new ExternalIdentityException("OIDC authorized party is invalid for a multi-audience token.");
            if (claims.TryGetValue("iat", out var issuedAtRaw)
                && long.TryParse(issuedAtRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var issuedAt)
                && DateTimeOffset.FromUnixTimeSeconds(issuedAt) > clock.GetUtcNow().Add(TimeSpan.FromSeconds(options.Oidc.ClockSkewSeconds)))
                throw new ExternalIdentityException("OIDC token was issued in the future.");
            if (options.Oidc.RequireNonce && (!claims.TryGetValue("nonce", out var nonce) || nonce != request.Nonce))
                throw new ExternalIdentityException("OIDC nonce does not match.");
            if (!claims.TryGetValue("sub", out var subject) || string.IsNullOrWhiteSpace(subject))
                throw new ExternalIdentityException("OIDC subject is missing.");
            if (!claims.TryGetValue("email", out var email) || string.IsNullOrWhiteSpace(email)
                || !claims.TryGetValue("email_verified", out var verified) || !bool.TryParse(verified, out var emailVerified) || !emailVerified)
                throw new ExternalIdentityException("OIDC did not return a verified email.");
            var displayName = claims.GetValueOrDefault("name") ?? claims.GetValueOrDefault("preferred_username") ?? email;
            var groups = ReadClaimValues(token, configuration.GroupClaim);
            var amr = ReadClaimValues(token, "amr");
            var authTime = claims.TryGetValue("auth_time", out var authTimeRaw)
                && long.TryParse(authTimeRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authTimeSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(authTimeSeconds) : (DateTimeOffset?)null;
            return new ExternalIdentityResult(token.Issuer, subject, email, true, displayName, claims)
            {
                Groups = groups, Acr = claims.GetValueOrDefault("acr"), Amr = amr, AuthTime = authTime
            };
        }
        catch (ExternalIdentityException) { throw; }
        catch (Exception exception) when (exception is SecurityTokenException or HttpRequestException or InvalidOperationException or ArgumentException)
        {
            throw new ExternalIdentityException("OIDC identity validation failed.", exception);
        }
    }

    private static IReadOnlyList<string> ReadClaimValues(JwtSecurityToken token, string claimPath)
    {
        if (string.IsNullOrWhiteSpace(claimPath)) return [];
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(token.RawPayload);
            var current = document.RootElement;
            foreach (var segment in claimPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !current.TryGetProperty(segment, out var child)) return [];
                current = child;
            }
            return current.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Array => current.EnumerateArray()
                    .Where(value => value.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(value => value.GetString()!).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                System.Text.Json.JsonValueKind.String when !string.IsNullOrWhiteSpace(current.GetString()) => [current.GetString()!],
                _ => []
            };
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private async Task<OpenIdConnectConfiguration> GetDiscoveryAsync(
        ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        IdentityHttpContent.ValidateEndpoint(configuration.Issuer);
        var issuer = configuration.Issuer.TrimEnd('/');
        var discoveryUrl = issuer + "/.well-known/openid-configuration";
        IdentityHttpContent.ValidateEndpoint(discoveryUrl);
        var documentRetriever = new HttpDocumentRetriever(http)
        {
            RequireHttps = discoveryUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        };
        var metadata = await OpenIdConnectConfigurationRetriever.GetAsync(discoveryUrl, documentRetriever, cancellationToken);
        if (!string.Equals(metadata.Issuer, configuration.Issuer.TrimEnd('/'), StringComparison.Ordinal))
            throw new ExternalIdentityException("OIDC discovery issuer does not match the configured issuer.");
        return metadata;
    }

    private async Task<string> ExchangeCodeAsync(ExternalIdentityRequest request,
        ExternalIdentityProviderConfiguration configuration, OpenIdConnectConfiguration metadata,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(metadata.TokenEndpoint)) throw new ExternalIdentityException("OIDC token endpoint is missing.");
        IdentityHttpContent.ValidateEndpoint(metadata.TokenEndpoint);
        var clientSecret = secrets is null ? string.Empty : await secrets.GetSecretAsync(configuration.ClientSecretRef, cancellationToken);
        if (string.IsNullOrWhiteSpace(clientSecret)) throw new ExternalIdentityException("OIDC client secret is not configured.");
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = request.Code!,
            ["client_id"] = configuration.ClientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = configuration.RedirectUri
        };
        if (!string.IsNullOrWhiteSpace(request.CodeVerifier)) form["code_verifier"] = request.CodeVerifier;
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, metadata.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await http.SendAsync(tokenRequest, cancellationToken);
        var body = await IdentityHttpContent.ReadBoundedAsync(response, options.MaxResponseBytes, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ExternalIdentityException($"OIDC token exchange returned HTTP {(int)response.StatusCode}.");
        var json = System.Text.Json.Nodes.JsonNode.Parse(body) as System.Text.Json.Nodes.JsonObject
            ?? throw new ExternalIdentityException("OIDC token response is invalid.");
        return json["id_token"]?.GetValue<string>()
            ?? throw new ExternalIdentityException("OIDC token response did not contain an ID token.");
    }
}
