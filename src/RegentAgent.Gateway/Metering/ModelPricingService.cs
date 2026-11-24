using System.Globalization;
using System.Text;

namespace RegentAgent.Gateway.Metering;

public sealed class ModelPricingService(int safetyMarginBasisPoints = 1000)
{
    public int EstimateInputTokens(string prompt) => Math.Max(1, Encoding.UTF8.GetByteCount(prompt) / 4);

    public long EstimateReservationMinor(PriceQuote quote, string prompt, int maxOutputTokens, int maxAttempts)
    {
        if (maxOutputTokens <= 0 || maxAttempts <= 0) throw new InvalidOperationException("Token and attempt limits must be positive.");
        var input = EstimateInputTokens(prompt);
        var billableInput = ApplyMargin(input);
        var billableOutput = ApplyMargin(maxOutputTokens) * maxAttempts;
        return CalculateAmountMinor(quote, billableInput, 0, billableOutput);
    }

    public long CalculateAmountMinor(PriceQuote quote, int inputTokens, int cachedInputTokens, int outputTokens)
    {
        if (inputTokens < 0 || outputTokens < 0 || cachedInputTokens < 0 || cachedInputTokens > inputTokens)
            throw new InvalidOperationException("Provider usage token counts are invalid.");
        var uncachedInput = Math.Max(0, inputTokens - cachedInputTokens);
        var amount = ((decimal)uncachedInput * quote.InputPricePerMillion
            + (decimal)cachedInputTokens * quote.CachedInputPricePerMillion
            + (decimal)outputTokens * quote.OutputPricePerMillion) / 1_000_000m * quote.MinorUnitFactor;
        return (long)decimal.Ceiling(amount);
    }

    private int ApplyMargin(int tokens) => (int)decimal.Ceiling(tokens * (1m + safetyMarginBasisPoints / 10_000m));

    internal static string Period(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
