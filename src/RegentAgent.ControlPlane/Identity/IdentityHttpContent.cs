using System.Net;
using System.Text;
using RegentAgent.Contracts.Identity;

namespace RegentAgent.ControlPlane.Identity;

internal static class IdentityHttpContent
{
    public static void ValidateEndpoint(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ExternalIdentityException("Identity endpoint is invalid.");
        var loopback = uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        var internalHost = uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && (loopback || internalHost)))
            throw new ExternalIdentityException("Identity endpoint must use HTTPS or loopback HTTP for tests.");
    }

    public static async Task<string> ReadBoundedAsync(HttpResponseMessage response, int maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new ExternalIdentityException("Identity response exceeds the configured limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new ExternalIdentityException("Identity response exceeds the configured limit.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
