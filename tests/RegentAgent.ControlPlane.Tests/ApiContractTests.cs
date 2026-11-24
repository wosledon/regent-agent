using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Api;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ApiContractTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Write_requires_idempotency_key_and_replays_only_identical_requests()
    {
        await using var db = CreateDb();
        var middleware = new ApiContractMiddleware(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsync("created");
        });
        var missing = CreateContext(HttpMethods.Post, "{}");
        await middleware.InvokeAsync(missing, db, TimeProvider.System);
        Assert.Equal(400, missing.Response.StatusCode);

        var first = CreateContext(HttpMethods.Post, "{\"name\":\"one\"}");
        first.Request.Headers["Idempotency-Key"] = "key-1";
        await middleware.InvokeAsync(first, db, TimeProvider.System);
        Assert.Equal(201, first.Response.StatusCode);
        Assert.Equal("key-1", first.Request.Headers["Idempotency-Key"].ToString());
        var replay = CreateContext(HttpMethods.Post, "{\"name\":\"one\"}");
        replay.Request.Headers["Idempotency-Key"] = "key-1";
        await middleware.InvokeAsync(replay, db, TimeProvider.System);
        Assert.Equal("true", replay.Response.Headers["Idempotency-Replayed"].ToString());
        var conflict = CreateContext(HttpMethods.Post, "{\"name\":\"two\"}");
        conflict.Request.Headers["Idempotency-Key"] = "key-1";
        await middleware.InvokeAsync(conflict, db, TimeProvider.System);
        Assert.Equal(409, conflict.Response.StatusCode);
    }

    [Fact]
    public async Task Update_requires_matching_if_match_and_row_version()
    {
        await using var db = CreateDb();
        var middleware = new ApiContractMiddleware(context => { context.Response.StatusCode = 204; return Task.CompletedTask; });
        var missing = CreateContext(HttpMethods.Put, "{\"version\":2}");
        missing.Request.Headers["Idempotency-Key"] = "update-1";
        await middleware.InvokeAsync(missing, db, TimeProvider.System);
        Assert.Equal(400, missing.Response.StatusCode);
        var stale = CreateContext(HttpMethods.Put, "{\"version\":2}");
        stale.Request.Headers["Idempotency-Key"] = "update-2";
        stale.Request.Headers["If-Match"] = "\"1\"";
        await middleware.InvokeAsync(stale, db, TimeProvider.System);
        Assert.Equal(412, stale.Response.StatusCode);
        var accepted = CreateContext(HttpMethods.Put, "{\"version\":2}");
        accepted.Request.Headers["Idempotency-Key"] = "update-3";
        accepted.Request.Headers["If-Match"] = "\"2\"";
        await middleware.InvokeAsync(accepted, db, TimeProvider.System);
        Assert.Equal(204, accepted.Response.StatusCode);
    }

    [Fact]
    public void Cursor_is_opaque_signed_and_bound_to_tenant_and_filter()
    {
        var codec = new CursorCodec("cursor-test-key");
        var tenant = Guid.NewGuid();
        var filter = CursorCodec.Filter("Delegation", null, null, null);
        var cursor = codec.Encode(tenant, filter, DateTimeOffset.UtcNow, "sort-key");
        Assert.DoesNotContain("sort-key", cursor);
        Assert.Equal("sort-key", codec.Decode(cursor, tenant, filter));
        Assert.Throws<InvalidOperationException>(() => codec.Decode(cursor, Guid.NewGuid(), filter));
        Assert.Throws<InvalidOperationException>(() => codec.Decode(cursor, tenant, CursorCodec.Filter("Handoff")));
        Assert.Throws<InvalidOperationException>(() => codec.Decode(cursor[..^1] + "x", tenant, filter));
    }

    private static DefaultHttpContext CreateContext(string method, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = "/v1/example";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = context.Request.Body.Length;
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("tenant_id", TenantId.ToString())
        ], "test"));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static RegentDbContext CreateDb() => new(new DbContextOptionsBuilder<RegentDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
