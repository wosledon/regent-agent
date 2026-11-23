namespace RegentAgent.Contracts.Results;

public sealed record Error(string Code, string Message, bool Retryable = false)
{
    public static Error Validation(string message) => new("INVALID_ARGUMENT", message);
    public static Error Unauthorized(string message = "Authentication required.") => new("UNAUTHENTICATED", message);
    public static Error Forbidden(string message = "Access denied.") => new("FORBIDDEN", message);
    public static Error NotFound(string message = "Resource not found.") => new("NOT_FOUND", message);
    public static Error Conflict(string message) => new("INVALID_STATE", message);
    public static Error Policy(string message) => new("POLICY_DENIED", message);
    public static Error Unavailable(string message) => new("DEPENDENCY_UNAVAILABLE", message, true);
}

public static class Result
{
    public static Result<T> Success<T>(T value) => new(value, null);
    public static Result<T> Failure<T>(Error error) => new(default, error);
}

public sealed record Result<T>(T? Value, Error? Error)
{
    public bool IsSuccess => Error is null;
    public T ValueOrThrow => IsSuccess
        ? Value!
        : throw new InvalidOperationException(Error!.Code);
}
