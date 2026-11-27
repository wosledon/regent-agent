using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Api;

public sealed class ApiContractMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, RegentDbContext db, TimeProvider clock)
    {
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128) requestId = Guid.NewGuid().ToString();
        context.TraceIdentifier = requestId;
        context.Response.Headers["X-Request-Id"] = requestId;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        var path = context.Request.Path.Value ?? string.Empty;
        var isApi = path.StartsWith("/v1", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/agent/v1", StringComparison.OrdinalIgnoreCase);
        var isWrite = HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method);
        if (!isApi || !isWrite || HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        var body = await ReadRequestBodyAsync(context);
        if (!await ValidateUniqueJsonPropertiesAsync(context, body)) return;

        // These authority-sensitive actions must evaluate the current actor instead of replaying a response
        // cached for another actor. Approval execution owns its actor-scoped idempotency and target checks.
        if (IsApprovalDecision(path) || IsToolDisclosure(path) || await IsApprovalGatedRouteAsync(context, body))
        {
            await next(context);
            return;
        }

        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
        {
            await ProblemAsync(context, 400, "INVALID_ARGUMENT", "Idempotency-Key is required and must be at most 200 characters.");
            return;
        }
        if (!Guid.TryParse(context.User.FindFirstValue("tenant_id"), out var tenantId))
        {
            await ProblemAsync(context, 401, "UNAUTHENTICATED", "A tenant identity is required.");
            return;
        }
        var ifMatch = context.Request.Headers["If-Match"].ToString();
        if ((HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method)
            || HttpMethods.IsDelete(context.Request.Method)) && !await ValidateIfMatchAsync(context, ifMatch, body))
            return;
        var requestHash = Hash($"{context.Request.Method}\n{path}\n{context.Request.QueryString}\n{ifMatch}\n", body);
        var operation = $"{context.Request.Method}:{path}";
        var record = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item =>
            item.TenantId == tenantId && item.Key == key && item.Operation == operation, context.RequestAborted);
        if (record is not null)
        {
            if (record.RequestHash != requestHash)
            {
                await ProblemAsync(context, 409, "IDEMPOTENCY_CONFLICT", "The idempotency key was already used with a different request.");
                return;
            }
            if (record.Status != "Completed")
            {
                await ProblemAsync(context, 409, "IDEMPOTENCY_IN_PROGRESS", "The original request is still in progress or has an unknown result.", true);
                return;
            }
            context.Response.StatusCode = record.StatusCode;
            context.Response.ContentType = record.ContentType;
            context.Response.Headers["Idempotency-Replayed"] = "true";
            await context.Response.Body.WriteAsync(Convert.FromBase64String(record.ResponseJson), context.RequestAborted);
            return;
        }

        record = new IdempotencyRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Key = key, Operation = operation,
            RequestHash = requestHash, Status = "InProgress", CreatedAt = clock.GetUtcNow()
        };
        db.IdempotencyRecords.Add(record);
        try { await db.SaveChangesAsync(context.RequestAborted); }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            await ProblemAsync(context, 409, "IDEMPOTENCY_IN_PROGRESS", "The original request is still in progress or has an unknown result.", true);
            return;
        }

        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;
        try
        {
            await next(context);
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                record.Status = "Completed";
                record.StatusCode = context.Response.StatusCode;
                record.ContentType = context.Response.ContentType ?? "application/json";
                record.ResponseJson = Convert.ToBase64String(responseBuffer.ToArray());
            }
            else record.Status = "Failed";
            await db.SaveChangesAsync(context.RequestAborted);
            context.Response.Body = originalBody;
            context.Response.ContentLength = responseBuffer.Length;
            responseBuffer.Position = 0;
            await responseBuffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        catch
        {
            context.Response.Body = originalBody;
            record.Status = "Failed";
            await db.SaveChangesAsync(context.RequestAborted);
            throw;
        }
    }

    private static async Task<byte[]> ReadRequestBodyAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
        var body = buffer.ToArray();
        context.Request.Body.Position = 0;
        return body;
    }

    private static async Task<bool> ValidateUniqueJsonPropertiesAsync(HttpContext context, byte[] body)
    {
        var firstNonWhitespace = body.FirstOrDefault(value => !char.IsWhiteSpace((char)value));
        if (firstNonWhitespace != (byte)'{' && firstNonWhitespace != (byte)'[') return true;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!ContainsDuplicateProperty(document.RootElement)) return true;
        }
        catch (JsonException)
        {
            return true;
        }

        await ProblemAsync(context, StatusCodes.Status400BadRequest, "INVALID_ARGUMENT",
            "JSON request bodies must not contain duplicate property names.");
        return false;
    }

    private static bool ContainsDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || ContainsDuplicateProperty(property.Value))
                    return true;
            return false;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (ContainsDuplicateProperty(item))
                    return true;
        }
        return false;
    }

    private static bool IsApprovalDecision(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 4 && string.Equals(segments[0], "v1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "approvals", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(segments[2], out _) && string.Equals(segments[3], "decision", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsToolDisclosure(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 4 && string.Equals(segments[0], "v1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "agent-evolution", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[2], "tools", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[3], "disclose", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> IsApprovalGatedRouteAsync(HttpContext context, byte[] body)
    {
        if (!HttpMethods.IsPost(context.Request.Method)) return false;
        var segments = (context.Request.Path.Value ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 4 && string.Equals(segments[0], "v1", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(segments[2], out _))
        {
            if (string.Equals(segments[1], "employees", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(segments[3], "offboard", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segments[3], "rehire", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (string.Equals(segments[1], "candidates", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[3], "hire", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        if (segments.Length != 2 || !string.Equals(segments[0], "v1", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], "performance-actions", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && TryGetPropertyIgnoreCase(document.RootElement, "actionType", out var actionType)
                && actionType.ValueKind == JsonValueKind.String
                && string.Equals(actionType.GetString(), "Terminate", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static async Task<bool> ValidateIfMatchAsync(HttpContext context, string value, byte[] body)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            await ProblemAsync(context, 400, "INVALID_ARGUMENT", "If-Match is required for update and delete operations.");
            return false;
        }
        int? bodyVersion = null;
        if (body.Length > 0)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                foreach (var name in new[] { "version", "rowVersion", "row_version" })
                    if (document.RootElement.TryGetProperty(name, out var element)
                        && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var version))
                        bodyVersion = version;
            }
            catch (JsonException) { }
        }
        if (bodyVersion is null)
        {
            if (value.Trim() == "*") return true;
            await ProblemAsync(context, 412, "VERSION_CONFLICT", "If-Match: * is required when the request has no row version.");
            return false;
        }
        var normalized = value.Trim().Trim('"');
        if (value.Trim().StartsWith("W/", StringComparison.OrdinalIgnoreCase) || normalized != bodyVersion.Value.ToString())
        {
            await ProblemAsync(context, 412, "VERSION_CONFLICT", "The supplied row version does not match If-Match.");
            return false;
        }
        return true;
    }

    private static string Hash(string prefix, byte[] body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prefix).Concat(body).ToArray())).ToLowerInvariant();

    private static async Task ProblemAsync(HttpContext context, int status, string code, string message, bool retryable = false)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new
        {
            request_id = context.TraceIdentifier,
            error = new { code, message, retryable }
        });
    }
}
