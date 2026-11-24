namespace RegentAgent.Observability;

public static class SensitiveDataPolicy
{
    public const string Redacted = "[redacted]";
    private static readonly string[] SensitiveKeyParts =
        ["authorization", "password", "secret", "token", "api_key", "apikey", "prompt", "source_code", "payload", "email", "pii"];

    public static bool IsSensitiveKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && SensitiveKeyParts.Any(part =>
            key.Contains(part, StringComparison.OrdinalIgnoreCase));
}
