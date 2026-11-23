using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class LocalizationService(JsonDocumentStore<LocalizationSettings> store)
{
    public async Task<LocalizationSettings> GetAsync(string tenantId, CancellationToken cancellationToken) =>
        await store.GetAsync(tenantId, cancellationToken)
        ?? new LocalizationSettings("zh-CN", "Asia/Shanghai", "CNY", 1);

    public async Task<LocalizationSettings> UpdateAsync(string tenantId, LocalizationSettings input,
        CancellationToken cancellationToken)
    {
        if (input.Locale is not ("zh-CN" or "en-US")) throw new InvalidOperationException("Locale must be zh-CN or en-US.");
        if (string.IsNullOrWhiteSpace(input.TimeZone)) throw new InvalidOperationException("IANA time zone is required.");
        try { TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone); }
        catch (TimeZoneNotFoundException) { throw new InvalidOperationException("Unknown IANA time zone."); }
        if (input.Currency.Length != 3 || input.Currency.Any(char.IsLower)) throw new InvalidOperationException("Currency must be an uppercase ISO 4217 code.");
        var current = await store.GetAsync(tenantId, cancellationToken);
        var updated = input with { Version = (current?.Version ?? 1) + 1 };
        await store.PutAsync(tenantId, updated, cancellationToken);
        return updated;
    }
}
