using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Contracts.Identity;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ExternalIdentityProviderTests
{
    [Fact]
    public async Task GitHub_adapter_exchanges_code_and_requires_a_verified_email()
    {
        var handler = new StubHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/token") return Json("""{"access_token":"github-token"}""");
            if (path == "/user") return Json("""{"id":42,"login":"octo","name":"Octo"}""");
            if (path == "/emails") return Json("""[{"email":"unverified@example.test","verified":false},{"email":"octo@example.test","verified":true,"primary":true}]""");
            throw new InvalidOperationException("Unexpected GitHub endpoint.");
        });
        using var http = new HttpClient(handler);
        var options = new ExternalIdentityOptions
        {
            GitHub = new GitHubIdentityOptions
            {
                TokenUrl = "http://localhost/token", UserUrl = "http://localhost/user", EmailsUrl = "http://localhost/emails"
            }
        };
        var provider = new GitHubExternalIdentityProvider(http, options, new StubSecretProvider("client-secret"));

        var result = await provider.AuthenticateAsync(new ExternalIdentityRequest(Guid.NewGuid(), "GitHub",
            "https://github.com", null, null, null, "code-1", CodeVerifier: "verifier"),
            new ExternalIdentityProviderConfiguration("https://github.com", "client", "github", "http://localhost/callback", "user:email", "groups"),
            CancellationToken.None);

        Assert.Equal("42", result.Subject);
        Assert.Equal("octo@example.test", result.Email);
        Assert.True(result.EmailVerified);
        Assert.Equal("Octo", result.DisplayName);
    }

    [Fact]
    public async Task Oidc_adapter_validates_signature_issuer_audience_and_nonce()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var issuer = "http://localhost/tenant";
        var jwks = $$"""{"keys":[{"kty":"RSA","use":"sig","kid":"test-key","alg":"RS256","n":"{{Base64UrlEncoder.Encode(parameters.Modulus!)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent!)}}"}]}""";
        var handler = new StubHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/tenant/.well-known/openid-configuration")
                return Json($$"""{"issuer":"{{issuer}}","jwks_uri":"http://localhost/jwks","authorization_endpoint":"http://localhost/authorize","token_endpoint":"http://localhost/token","response_types_supported":["code"],"subject_types_supported":["public"],"id_token_signing_alg_values_supported":["RS256"]}""");
            if (request.RequestUri.AbsolutePath == "/jwks") return Json(jwks);
            throw new InvalidOperationException("Unexpected OIDC endpoint.");
        });
        using var http = new HttpClient(handler);
        var provider = new OidcExternalIdentityProvider(http, new ExternalIdentityOptions());
        var token = new JwtSecurityToken(issuer, "client-id", [
            new Claim("sub", "subject-1"), new Claim("email", "user@example.test"),
            new Claim("email_verified", "true"), new Claim("nonce", "nonce-1"), new Claim("name", "OIDC User")
        ], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        var configuration = new ExternalIdentityProviderConfiguration(issuer, "client-id", "oidc", "http://localhost/callback", "openid profile", "groups");

        var result = await provider.AuthenticateAsync(new ExternalIdentityRequest(Guid.NewGuid(), "Oidc", issuer,
            null, null, null, IdToken: jwt, Nonce: "nonce-1"), configuration, CancellationToken.None);

        Assert.Equal("subject-1", result.Subject);
        Assert.Equal("user@example.test", result.Email);
        Assert.Equal("OIDC User", result.DisplayName);
        await Assert.ThrowsAsync<ExternalIdentityException>(() => provider.AuthenticateAsync(
            new ExternalIdentityRequest(Guid.NewGuid(), "Oidc", issuer, null, null, null, IdToken: jwt, Nonce: "wrong"),
            configuration, CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_uses_local_by_default_and_remote_only_when_selected()
    {
        IExternalIdentityProvider[] providers = [new LocalExternalIdentityProvider(), new StubRemoteProvider()];
        var local = new ExternalIdentityProviderResolver(providers, new ExternalIdentityOptions { Mode = "local" });
        var remote = new ExternalIdentityProviderResolver(providers, new ExternalIdentityOptions { Mode = "remote" });

        Assert.Equal("local", local.Resolve("GitHub").Provider);
        Assert.Equal("GitHub", remote.Resolve("GitHub").Provider);
        Assert.Throws<InvalidOperationException>(() => remote.Resolve("Oidc"));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubSecretProvider(string secret) : IExternalIdentitySecretProvider
    {
        public Task<string> GetSecretAsync(string secretRef, CancellationToken cancellationToken) => Task.FromResult(secret);
    }

    private sealed class StubRemoteProvider : IExternalIdentityProvider
    {
        public string Provider => "GitHub";
        public Task<ExternalIdentityResult> AuthenticateAsync(ExternalIdentityRequest request,
            ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
