using RegentAgent.Contracts.Models;
using RegentAgent.Providers;

namespace RegentAgent.Gateway.Metering;

public sealed class ModelPriceBook
{
    private readonly ModelPriceSnapshot[] prices;
    private readonly decimal minorUnitFactor;

    public ModelPriceBook(IEnumerable<ModelPriceSnapshot> prices, decimal minorUnitFactor = 100m)
    {
        if (minorUnitFactor <= 0) throw new InvalidOperationException("Currency minor unit factor must be positive.");
        this.prices = prices.OrderBy(price => price.EffectiveAt).ToArray();
        this.minorUnitFactor = minorUnitFactor;
    }

    public static ModelPriceBook FromConfiguration(IConfiguration configuration)
    {
        var factor = configuration.GetValue<decimal>("Gateway:Pricing:MinorUnitFactor", 100m);
        var (_, entries) = LocalModelCatalog.CreateSnapshot(DateOnly.FromDateTime(DateTime.UtcNow));
        var prices = entries.Where(entry => entry.PriceState == "Priced").Select(entry => new ModelPriceSnapshot(
            entry.Provider, entry.ModelId, entry.Version, "local-deterministic-catalog", entry.Currency,
            entry.InputPricePerMillion ?? 0m, entry.OutputPricePerMillion ?? 0m,
            entry.InputPricePerMillion, DateTimeOffset.UnixEpoch)).ToDictionary(price => Key(price.Provider, price.Model));
        foreach (var entry in entries.Where(item => item.Provider == "Local" && item.PriceState == "Priced"))
        foreach (var protocolProvider in new[] { "openai", "anthropic" })
            prices[Key(protocolProvider, entry.ModelId)] = new ModelPriceSnapshot(protocolProvider, entry.ModelId,
                entry.Version, "local-deterministic-catalog", entry.Currency, entry.InputPricePerMillion ?? 0m,
                entry.OutputPricePerMillion ?? 0m, entry.InputPricePerMillion, DateTimeOffset.UnixEpoch);
        foreach (var section in configuration.GetSection("Gateway:Pricing:Models").GetChildren())
        {
            var provider = section["Provider"] ?? string.Empty;
            var model = section["Model"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model)) continue;
            prices[Key(provider, model)] = new ModelPriceSnapshot(
                provider, model, section["PriceVersion"] ?? "manual-1", section["CatalogSnapshotId"] ?? "manual",
                section["Currency"] ?? "CNY", section.GetValue<decimal>("InputPricePerMillion"),
                section.GetValue<decimal>("OutputPricePerMillion"),
                section["CachedInputPricePerMillion"] is { } cached ? decimal.Parse(cached) : null,
                DateTimeOffset.TryParse(section["EffectiveAt"], out var effectiveAt) ? effectiveAt : DateTimeOffset.UnixEpoch);
        }
        return new ModelPriceBook(prices.Values, factor);
    }

    public PriceQuote Quote(string provider, string model, DateTimeOffset at)
    {
        var price = prices.LastOrDefault(candidate => candidate.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)
            && candidate.Model.Equals(model, StringComparison.OrdinalIgnoreCase) && candidate.EffectiveAt <= at)
            ?? throw new InvalidOperationException($"UNPRICED: no effective price snapshot for {provider}/{model}.");
        if (price.InputPricePerMillion < 0 || price.OutputPricePerMillion < 0
            || price.CachedInputPricePerMillion is < 0 || string.IsNullOrWhiteSpace(price.Currency)
            || string.IsNullOrWhiteSpace(price.PriceVersion))
            throw new InvalidOperationException($"UNPRICED: invalid price snapshot for {provider}/{model}.");
        return new PriceQuote(price.Provider, price.Model, price.PriceVersion, price.CatalogSnapshotId,
            price.Currency, price.InputPricePerMillion, price.OutputPricePerMillion,
            price.CachedInputPricePerMillion ?? price.InputPricePerMillion, price.EffectiveAt, minorUnitFactor);
    }

    private static string Key(string provider, string model) => $"{provider}:{model}".ToLowerInvariant();
}
