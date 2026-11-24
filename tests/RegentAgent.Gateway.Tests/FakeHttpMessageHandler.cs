using System.Net;
using System.Text;

namespace RegentAgent.Gateway.Tests;

public sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var allHeaders = request.Content is null ? request.Headers : request.Headers.Concat(request.Content.Headers);
        var headers = allHeaders.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => string.Join(",", x.SelectMany(value => value.Value)), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new CapturedRequest(request.Method.Method, request.RequestUri!, body, headers));
        return await responder(request, cancellationToken);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}

public sealed record CapturedRequest(string Method, Uri Uri, string? Body,
    IReadOnlyDictionary<string, string> Headers);
