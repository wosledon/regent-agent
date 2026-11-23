namespace RegentAgent.Contracts.Artifacts;

public sealed record ArtifactDto(
    Guid Id,
    Guid ProjectId,
    Guid? TaskId,
    string ObjectKey,
    string Hash,
    string ContentType,
    long Length,
    string UploadState,
    DateTimeOffset CreatedAt);
