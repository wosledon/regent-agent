using System.Net;
using System.Text;

namespace RegentAgent.Providers;

internal static class ProviderHttpContent
{
    public static async Task<string> ReadBoundedAsync(HttpResponseMessage response, int maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new ProviderRequestException((int)response.StatusCode, "PROVIDER_RESPONSE_TOO_LARGE", false,
                "Provider response exceeds the configured limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new ProviderRequestException((int)response.StatusCode, "PROVIDER_RESPONSE_TOO_LARGE", false,
                    "Provider response exceeds the configured limit.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
