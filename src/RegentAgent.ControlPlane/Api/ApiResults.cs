using RegentAgent.Contracts.Results;

namespace RegentAgent.ControlPlane.Api;

public static class ApiResults
{
    public static IResult Problem(Error error, string? requestId = null) => Results.Problem(
        statusCode: MapStatus(error.Code),
        title: error.Message,
        extensions: new Dictionary<string, object?>
        {
            ["request_id"] = requestId ?? Guid.NewGuid().ToString(),
            ["error"] = new { code = error.Code, message = error.Message, retryable = error.Retryable }
        });

    private static int MapStatus(string code) => code switch
    {
        "INVALID_ARGUMENT" => 400,
        "UNAUTHENTICATED" => 401,
        "FORBIDDEN" or "TENANT_MISMATCH" => 403,
        "NOT_FOUND" => 404,
        "INVALID_STATE" or "IDEMPOTENCY_CONFLICT" => 409,
        "VERSION_CONFLICT" => 412,
        "POLICY_DENIED" => 422,
        "RATE_LIMITED" or "BUDGET_HARD_LIMIT" => 429,
        "DEPENDENCY_UNAVAILABLE" => 503,
        _ => 500
    };
}
