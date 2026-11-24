namespace RegentAgent.Contracts.Billing;

public sealed record DataExportDto(
    Guid Id,
    Guid TenantId,
    string Status,
    string Format,
    string SchemaVersion,
    string ObjectKey,
    string Sha256,
    long Length,
    int TableCount,
    int ModuleCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt,
    string? ValidationError);

public sealed record DataExportValidationDto(
    Guid ExportId,
    bool Valid,
    string Sha256,
    int TableCount,
    int ModuleCount,
    DateTimeOffset ValidatedAt,
    string? Error);
