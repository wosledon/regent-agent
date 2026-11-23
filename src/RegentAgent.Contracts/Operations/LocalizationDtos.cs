namespace RegentAgent.Contracts.Operations;

public sealed record LocalizationSettings(string Locale, string TimeZone, string Currency, int Version);
