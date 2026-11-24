using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RegentAgent.Providers;

public sealed class ProviderHttpOptions
{
    public ProviderEndpointOptions OpenAI { get; set; } = new();
    public ProviderEndpointOptions Anthropic { get; set; } = new();
}

public sealed class ProviderEndpointOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxResponseBytes { get; set; } = 4 * 1024 * 1024;
    public int MaxRetries { get; set; } = 2;
    public int BaseDelayMilliseconds { get; set; } = 200;

    public void Validate(string provider)
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"{provider} base URL is invalid.");
        var loopback = uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && loopback))
            throw new InvalidOperationException($"{provider} base URL must use HTTPS or loopback HTTP for tests.");
        if (TimeoutSeconds is < 1 or > 600 || MaxResponseBytes is < 1024 or > 64 * 1024 * 1024
            || MaxRetries is < 0 or > 5 || BaseDelayMilliseconds is < 0 or > 10_000)
            throw new InvalidOperationException($"{provider} HTTP limits are invalid.");
    }
}

public sealed class ProviderRequestException(int? statusCode, string errorCode, bool retryable, string message,
    string? nativeErrorJson = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public bool Retryable { get; } = retryable;
    public string? NativeErrorJson { get; } = nativeErrorJson;
}

public abstract class HttpProviderClient(HttpClient http, ProviderEndpointOptions options) : IProviderClient
{
    private readonly HttpClient http = http;
    private readonly ProviderEndpointOptions options = options;

    public string Provider => ProviderName;
    protected abstract string ProviderName { get; }
    protected abstract string ProtocolName { get; }
    protected abstract string EndpointPath { get; }
    protected abstract void ConfigureHeaders(HttpRequestMessage request, string apiKey);
    protected abstract ProviderResponse ParseResponse(string json, long latencyMs);

    public async Task<ProviderResponse> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken)
    {
        if (!CanHandle(request.Provider, request.Protocol)) throw new InvalidOperationException("Provider protocol mismatch.");
        if (string.IsNullOrWhiteSpace(request.ApiKey)) throw new ProviderRequestException(null, "PROVIDER_KEY_MISSING", false, "Provider API key is not configured.");
        options.Validate(ProviderName);
        var payload = BuildPayload(request);
        var stopwatch = Stopwatch.StartNew();
        for (var attempt = 0; ; attempt++)
        {
            var baseUrl = options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/";
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), EndpointPath));
            message.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            ConfigureHeaders(message, request.ApiKey);
            TimeSpan? retryDelay = null;
            try
            {
                using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var body = await ProviderHttpContent.ReadBoundedAsync(response, options.MaxResponseBytes, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    try { return ParseResponse(body, stopwatch.ElapsedMilliseconds); }
                    catch (Exception exception) when (exception is JsonException or InvalidOperationException)
                    {
                        throw new ProviderRequestException((int)response.StatusCode, "PROVIDER_RESPONSE_INVALID", false,
                            $"Provider {ProviderName} returned an invalid response.", body);
                    }
                }
                var retryable = ProviderHttpRetry.IsRetryable(response.StatusCode);
                if (!retryable || attempt >= options.MaxRetries)
                    throw new ProviderRequestException((int)response.StatusCode, "PROVIDER_REJECTED", retryable,
                        $"Provider {ProviderName} returned HTTP {(int)response.StatusCode}.", body);
                retryDelay = ProviderHttpRetry.Delay(response, attempt, options.BaseDelayMilliseconds);
            }
            catch (ProviderRequestException) { throw; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= options.MaxRetries)
                    throw new ProviderRequestException(GatewayTimeoutStatus, "PROVIDER_TIMEOUT", true, $"Provider {ProviderName} timed out.");
            }
            catch (HttpRequestException)
            {
                if (attempt >= options.MaxRetries)
                    throw new ProviderRequestException(null, "PROVIDER_UNAVAILABLE", true, $"Provider {ProviderName} is unavailable.");
            }
            await Task.Delay(retryDelay ?? ProviderHttpRetry.Delay(attempt, options.BaseDelayMilliseconds), cancellationToken);
        }
    }

    public async IAsyncEnumerable<ProviderStreamChunk> StreamAsync(ProviderRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!CanHandle(request.Provider, request.Protocol)) throw new InvalidOperationException("Provider protocol mismatch.");
        if (string.IsNullOrWhiteSpace(request.ApiKey)) throw new ProviderRequestException(null, "PROVIDER_KEY_MISSING", false, "Provider API key is not configured.");
        options.Validate(ProviderName);
        using var response = await OpenStreamingResponseAsync(BuildPayload(request), request.ApiKey, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var chunk in ProviderSseReader.ReadAsync(stream, options.MaxResponseBytes, cancellationToken))
            yield return chunk;
    }

    private async Task<HttpResponseMessage> OpenStreamingResponseAsync(string payload, string apiKey,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var baseUrl = options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/";
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), EndpointPath));
            message.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            ConfigureHeaders(message, apiKey);
            TimeSpan? retryDelay = null;
            HttpResponseMessage? response = null;
            try
            {
                response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    if (response.Content.Headers.ContentType?.MediaType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true)
                        return response;
                    var invalidBody = await ProviderHttpContent.ReadBoundedAsync(response, options.MaxResponseBytes, cancellationToken);
                    throw new ProviderRequestException((int)response.StatusCode, "PROVIDER_RESPONSE_INVALID", false,
                        $"Provider {ProviderName} did not return an event stream.", invalidBody);
                }
                var body = await ProviderHttpContent.ReadBoundedAsync(response, options.MaxResponseBytes, cancellationToken);
                var retryable = ProviderHttpRetry.IsRetryable(response.StatusCode);
                if (!retryable || attempt >= options.MaxRetries)
                    throw new ProviderRequestException((int)response.StatusCode, "PROVIDER_REJECTED", retryable,
                        $"Provider {ProviderName} returned HTTP {(int)response.StatusCode}.", body);
                retryDelay = ProviderHttpRetry.Delay(response, attempt, options.BaseDelayMilliseconds);
            }
            catch (ProviderRequestException) { response?.Dispose(); throw; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
                if (attempt >= options.MaxRetries)
                    throw new ProviderRequestException(GatewayTimeoutStatus, "PROVIDER_TIMEOUT", true, $"Provider {ProviderName} timed out.");
            }
            catch (HttpRequestException)
            {
                response?.Dispose();
                if (attempt >= options.MaxRetries)
                    throw new ProviderRequestException(null, "PROVIDER_UNAVAILABLE", true, $"Provider {ProviderName} is unavailable.");
            }
            response?.Dispose();
            await Task.Delay(retryDelay ?? ProviderHttpRetry.Delay(attempt, options.BaseDelayMilliseconds), cancellationToken);
        }
    }

    public bool CanHandle(string provider, string protocol) =>
        provider.Equals(ProviderName, StringComparison.OrdinalIgnoreCase)
        && protocol.Equals(ProtocolName, StringComparison.OrdinalIgnoreCase);

    private string BuildPayload(ProviderRequest request)
    {
        JsonObject payload;
        if (string.IsNullOrWhiteSpace(request.NativeRequestJson)) payload = new() { ["model"] = request.Model };
        else
        {
            var parsed = JsonNode.Parse(request.NativeRequestJson) as JsonObject
                ?? throw new InvalidOperationException("Provider request body must be a JSON object.");
            payload = parsed;
        }
        payload["model"] = request.Model;
        return payload.ToJsonString();
    }

    private const int GatewayTimeoutStatus = 504;
}
