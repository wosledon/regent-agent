using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RegentAgent.Contracts.Governance;

namespace RegentAgent.ControlPlane.Application;

public static partial class GovernancePolicy
{
    [GeneratedRegex(@"ignore\s+(all\s+)?previous|system\s+prompt|reveal\s+(your\s+)?instructions|exfiltrat|credential|api[_ -]?key|password", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InjectionPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?:\+?86[- ]?)?1[3-9]\d{9}", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b(?:sk-[A-Za-z0-9_-]{12,}|ghp_[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16})\b", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    public static IReadOnlyList<string> ScanInjection(string content)
    {
        var findings = new List<string>();
        foreach (Match match in InjectionPattern().Matches(content))
            findings.Add($"Untrusted instruction pattern: {match.Value[..Math.Min(match.Value.Length, 24)]}");
        return findings;
    }

    public static DlpRedactionResult Redact(string content, Guid tenantId)
    {
        var tokens = new List<DlpToken>();
        string Replace(Match match, string classification)
        {
            var token = $"dlp_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}:{classification}:{match.Value}")))[..20].ToLowerInvariant()}";
            tokens.Add(new DlpToken(token, classification));
            return $"[REDACTED:{classification}:{token}]";
        }
        var redacted = SecretPattern().Replace(content, x => Replace(x, "SECRET"));
        redacted = EmailPattern().Replace(redacted, x => Replace(x, "EMAIL"));
        redacted = PhonePattern().Replace(redacted, x => Replace(x, "PHONE"));
        return new DlpRedactionResult { RedactedText = redacted, Tokens = tokens };
    }

    public static (string Category, string Disposition) ClassifyContent(string content)
    {
        var lower = content.ToLowerInvariant();
        if (lower.Contains("kill", StringComparison.Ordinal) || lower.Contains("bomb", StringComparison.Ordinal) || lower.Contains("self-harm", StringComparison.Ordinal))
            return ("ViolenceOrSelfHarm", "Block");
        if (lower.Contains("hate", StringComparison.Ordinal) || lower.Contains("slur", StringComparison.Ordinal) || lower.Contains("explicit", StringComparison.Ordinal))
            return ("HateOrSexual", "Review");
        if (lower.Contains("password", StringComparison.Ordinal) || lower.Contains("private key", StringComparison.Ordinal))
            return ("SecretExposure", "Block");
        return ("None", "Allow");
    }

    public static bool TryGetCriticalVulnerabilities(string sbomJson, out int critical)
    {
        critical = 0;
        try
        {
            using var document = JsonDocument.Parse(sbomJson);
            if (document.RootElement.TryGetProperty("vulnerabilities", out var vulnerabilities))
                critical = vulnerabilities.EnumerateArray().Count(x => x.TryGetProperty("severity", out var severity) && severity.GetString() == "critical");
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static bool IsValidIncidentSeverity(string severity) => severity is "SEV1" or "SEV2" or "SEV3" or "SEV4";
}
