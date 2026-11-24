using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using RegentAgent.Contracts.Identity;

namespace RegentAgent.ControlPlane.Identity;

public sealed class GitHubExternalIdentityProvider(
    HttpClient http,
    ExternalIdentityOptions options,
    IExternalIdentitySecretProvider secrets) : IExternalIdentityProvider
{
    private readonly HttpClient http = http;
    private readonly ExternalIdentityOptions options = options;
    private readonly IExternalIdentitySecretProvider secrets = secrets;

    public string Provider => "GitHub";

    public async Task<ExternalIdentityResult> AuthenticateAsync(ExternalIdentityRequest request,
        ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) throw new ExternalIdentityException("GitHub authorization code is required.");
        if (string.IsNullOrWhiteSpace(configuration.ClientId) || string.IsNullOrWhiteSpace(configuration.RedirectUri))
            throw new ExternalIdentityException("GitHub client id and redirect URI are required.");
        IdentityHttpContent.ValidateEndpoint(options.GitHub.TokenUrl);
        IdentityHttpContent.ValidateEndpoint(options.GitHub.UserUrl);
        IdentityHttpContent.ValidateEndpoint(options.GitHub.EmailsUrl);
        var clientSecret = await secrets.GetSecretAsync(configuration.ClientSecretRef, cancellationToken);
        if (string.IsNullOrWhiteSpace(clientSecret)) throw new ExternalIdentityException("GitHub client secret is not configured.");
        var token = await ExchangeCodeAsync(request, configuration, clientSecret, cancellationToken);
        using var userRequest = new HttpRequestMessage(HttpMethod.Get, options.GitHub.UserUrl);
        userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        userRequest.Headers.UserAgent.ParseAdd("Regent-Agent/1.0");
        var user = await SendJsonAsync(userRequest, cancellationToken);
        var subject = user["id"]?.ToJsonString().Trim('"');
        if (string.IsNullOrWhiteSpace(subject)) throw new ExternalIdentityException("GitHub subject is missing.");

        using var emailRequest = new HttpRequestMessage(HttpMethod.Get, options.GitHub.EmailsUrl);
        emailRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        emailRequest.Headers.UserAgent.ParseAdd("Regent-Agent/1.0");
        var emails = await SendJsonArrayAsync(emailRequest, cancellationToken);
        var verified = emails.FirstOrDefault(item => item?["verified"]?.GetValue<bool>() == true
            && item?["primary"]?.GetValue<bool>() == true)
            ?? emails.FirstOrDefault(item => item?["verified"]?.GetValue<bool>() == true);
        var email = verified?["email"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(email)) throw new ExternalIdentityException("GitHub did not return a verified email.");
        var displayName = user["name"]?.GetValue<string>() ?? user["login"]?.GetValue<string>() ?? email;
        return new ExternalIdentityResult("https://github.com", subject, email, true, displayName,
            new Dictionary<string, string>
            {
                ["iss"] = "https://github.com", ["sub"] = subject, ["email"] = email,
                ["login"] = user["login"]?.GetValue<string>() ?? string.Empty
            });
    }

    private async Task<string> ExchangeCodeAsync(ExternalIdentityRequest request,
        ExternalIdentityProviderConfiguration configuration, string clientSecret, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = configuration.ClientId,
            ["client_secret"] = clientSecret,
            ["code"] = request.Code!,
            ["redirect_uri"] = configuration.RedirectUri
        };
        if (!string.IsNullOrWhiteSpace(request.CodeVerifier)) form["code_verifier"] = request.CodeVerifier;
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, options.GitHub.TokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };
        tokenRequest.Headers.Accept.ParseAdd("application/json");
        var response = await http.SendAsync(tokenRequest, cancellationToken);
        var body = await IdentityHttpContent.ReadBoundedAsync(response, options.MaxResponseBytes, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ExternalIdentityException($"GitHub token exchange returned HTTP {(int)response.StatusCode}.");
        var json = JsonNode.Parse(body) as JsonObject
            ?? throw new ExternalIdentityException("GitHub token response is invalid.");
        return json["access_token"]?.GetValue<string>()
            ?? throw new ExternalIdentityException("GitHub token response did not contain an access token.");
    }

    private async Task<JsonObject> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await http.SendAsync(request, cancellationToken);
        var body = await IdentityHttpContent.ReadBoundedAsync(response, options.MaxResponseBytes, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ExternalIdentityException($"GitHub user request returned HTTP {(int)response.StatusCode}.");
        return JsonNode.Parse(body) as JsonObject
            ?? throw new ExternalIdentityException("GitHub user response is invalid.");
    }

    private async Task<JsonArray> SendJsonArrayAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await http.SendAsync(request, cancellationToken);
        var body = await IdentityHttpContent.ReadBoundedAsync(response, options.MaxResponseBytes, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ExternalIdentityException($"GitHub email request returned HTTP {(int)response.StatusCode}.");
        return JsonNode.Parse(body) as JsonArray
            ?? throw new ExternalIdentityException("GitHub email response is invalid.");
    }
}
