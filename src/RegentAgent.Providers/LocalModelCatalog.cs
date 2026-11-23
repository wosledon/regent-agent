using RegentAgent.Contracts.Models;

namespace RegentAgent.Providers;

public static class LocalModelCatalog
{
    public static (ModelSnapshot Snapshot, IReadOnlyList<ModelCatalogEntry> Entries) CreateSnapshot(DateOnly date)
    {
        var snapshot = new ModelSnapshot($"snapshot-{date:yyyyMMdd}", date, "local-deterministic-catalog", true);
        var entries = new[]
        {
            Entry("Local", "local-cheap", "1", "Local", "cheap", 0m, 0m, "CNY"),
            Entry("Local", "local-mid", "1", "Local", "mid", 0m, 0m, "CNY"),
            Entry("Local", "local-premium", "1", "Local", "premium", 0m, 0m, "CNY"),
            Entry("OpenAI", "gpt-5-mini", "2026-11", "OpenAI", "cheap"),
            Entry("OpenAI", "gpt-5", "2026-11", "OpenAI", "mid"),
            Entry("Anthropic", "claude-sonnet-4-5", "2026-11", "Anthropic", "mid"),
            Entry("Anthropic", "claude-opus-4-1", "2026-11", "Anthropic", "premium")
        };
        return (snapshot, entries);
    }

    private static ModelCatalogEntry Entry(string provider, string modelId, string version, string protocol,
        string tier, decimal? input = null, decimal? output = null, string currency = "USD") => new()
    {
        Provider = provider, ModelId = modelId, Version = version, Protocol = protocol,
        PriceState = input is null ? "Unpriced" : "Priced", InputPricePerMillion = input,
        OutputPricePerMillion = output, Currency = currency, Tier = tier,
        Source = provider == "Local" ? "local-deterministic-catalog" : "manual-required"
    };
}
