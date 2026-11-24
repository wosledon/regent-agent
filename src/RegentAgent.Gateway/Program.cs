using System.Text.Json.Nodes;
using RegentAgent.Gateway.KeyVault;
using RegentAgent.Gateway.Metering;
using RegentAgent.Providers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi("v1");
builder.Services.AddProblemDetails();
var providerMode = builder.Configuration["Gateway:ProviderMode"] ?? "local";
if (providerMode.Trim().ToLowerInvariant() is not ("local" or "remote"))
    throw new InvalidOperationException("Gateway provider mode must be local or remote.");
var providerHttpOptions = new ProviderHttpOptions
{
    OpenAI = new ProviderEndpointOptions
    {
        BaseUrl = builder.Configuration["Gateway:Providers:OpenAI:BaseUrl"] ?? "https://api.openai.com/v1",
        TimeoutSeconds = builder.Configuration.GetValue("Gateway:Providers:OpenAI:TimeoutSeconds", 120),
        MaxResponseBytes = builder.Configuration.GetValue("Gateway:Providers:OpenAI:MaxResponseBytes", 4 * 1024 * 1024),
        MaxRetries = builder.Configuration.GetValue("Gateway:Providers:OpenAI:MaxRetries", 2),
        BaseDelayMilliseconds = builder.Configuration.GetValue("Gateway:Providers:OpenAI:BaseDelayMilliseconds", 200)
    },
    Anthropic = new ProviderEndpointOptions
    {
        BaseUrl = builder.Configuration["Gateway:Providers:Anthropic:BaseUrl"] ?? "https://api.anthropic.com/v1",
        TimeoutSeconds = builder.Configuration.GetValue("Gateway:Providers:Anthropic:TimeoutSeconds", 120),
        MaxResponseBytes = builder.Configuration.GetValue("Gateway:Providers:Anthropic:MaxResponseBytes", 4 * 1024 * 1024),
        MaxRetries = builder.Configuration.GetValue("Gateway:Providers:Anthropic:MaxRetries", 2),
        BaseDelayMilliseconds = builder.Configuration.GetValue("Gateway:Providers:Anthropic:BaseDelayMilliseconds", 200)
    }
};
if (providerMode.Equals("remote", StringComparison.OrdinalIgnoreCase))
{
    providerHttpOptions.OpenAI.Validate("OpenAI");
    providerHttpOptions.Anthropic.Validate("Anthropic");
}
builder.Services.AddSingleton(new ProviderRoutingOptions { Mode = providerMode });
builder.Services.AddSingleton(providerHttpOptions);
builder.Services.AddHttpClient("openai", (provider, client) =>
{
    var options = provider.GetRequiredService<ProviderHttpOptions>().OpenAI;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddHttpClient("anthropic", (provider, client) =>
{
    var options = provider.GetRequiredService<ProviderHttpOptions>().Anthropic;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddSingleton<LocalProviderClient>();
builder.Services.AddSingleton<IProviderClient>(provider => new OpenAiProviderClient(
    provider.GetRequiredService<IHttpClientFactory>().CreateClient("openai"),
    provider.GetRequiredService<ProviderHttpOptions>().OpenAI));
builder.Services.AddSingleton<IProviderClient>(provider => new AnthropicProviderClient(
    provider.GetRequiredService<IHttpClientFactory>().CreateClient("anthropic"),
    provider.GetRequiredService<ProviderHttpOptions>().Anthropic));
builder.Services.AddSingleton<IProviderClient>(provider => provider.GetRequiredService<LocalProviderClient>());
builder.Services.AddSingleton<IProviderRouter, ProviderRouter>();
builder.Services.AddSingleton<IUsageJournal>(sp =>
    new JsonlUsageJournal(builder.Configuration["Gateway:UsagePath"] ?? "data/gateway-usage.jsonl"));
builder.Services.AddSingleton<IKeyVault>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var master = new LocalMasterKeyProvider(config["Gateway:MasterKeyPath"] ?? "data/secrets/master.key",
        config["Gateway:MasterKey"]);
    return new LocalFileKeyVault(config["Gateway:KeyVaultPath"] ?? "data/keyvault.json", master);
});
builder.Services.AddSingleton(sp => new LocalBudgetService(
    sp.GetRequiredService<IConfiguration>().GetValue<long>("Gateway:MonthlyLimit", 8_000_000)));
var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (exception is ProviderRequestException providerException)
    {
        context.Response.StatusCode = providerException.StatusCode is >= 400 and < 500
            ? providerException.StatusCode.Value
            : providerException.ErrorCode == "PROVIDER_TIMEOUT" ? 504 : 502;
        if (!string.IsNullOrWhiteSpace(providerException.NativeErrorJson))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(providerException.NativeErrorJson, context.RequestAborted);
            return;
        }
        await context.Response.WriteAsJsonAsync(new
        {
            error = new { code = providerException.ErrorCode, message = providerException.Message, retryable = providerException.Retryable },
            trace_id = context.TraceIdentifier
        });
        return;
    }
    context.Response.StatusCode = exception is InvalidOperationException ? 409 : 500;
    await context.Response.WriteAsJsonAsync(new
    {
        error = new { code = exception is InvalidOperationException ? "INVALID_STATE" : "INTERNAL_ERROR", message = "Gateway request failed." },
        trace_id = context.TraceIdentifier
    });
}));
app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", mode = providerMode })).AllowAnonymous();

app.MapPost("/v1/chat/completions", async (HttpContext context, IProviderRouter provider,
    IKeyVault keyVault, IUsageJournal usage, LocalBudgetService budget, CancellationToken cancellationToken) =>
{
    var body = await ReadBodyAsync(context, cancellationToken);
    var model = Header(context, "X-Regent-Model") ?? body?["model"]?.GetValue<string>() ?? "local-cheap";
    var tenant = Header(context, "X-Regent-Tenant") ?? "demo";
    var prompt = ExtractOpenAiPrompt(body);
    var maxOutput = body?["max_tokens"]?.GetValue<int?>() ?? 512;
    var key = await ResolveKeyAsync(context, tenant, keyVault, cancellationToken);
    if (!await budget.TryReserveAsync(Math.Max(1, maxOutput), cancellationToken))
    {
        return Results.Problem(statusCode: 429, title: "Budget limit reached.", extensions: new Dictionary<string, object?>
        {
            ["error"] = new { code = "BUDGET_HARD_LIMIT", message = "Local monthly limit reached.", retryable = false }
        });
    }
    var response = await provider.CompleteAsync(new ProviderRequest("openai", model, "openai_chat_completions", prompt, maxOutput, key, body?.ToJsonString()), cancellationToken);
    await usage.AppendAsync(new UsageRecord(DateTimeOffset.UtcNow, tenant, "openai", model,
        response.InputTokens, response.OutputTokens, response.ProviderRequestId), cancellationToken);
    if (response.NativeResponseJson is { } nativeResponse) return Results.Content(nativeResponse, "application/json");
    return Results.Json(new
    {
        id = $"chatcmpl-{response.ProviderRequestId}",
        @object = "chat.completion",
        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        model,
        choices = new[] { new { index = 0, message = new { role = "assistant", content = response.Text }, finish_reason = "stop" } },
        usage = new { prompt_tokens = response.InputTokens, completion_tokens = response.OutputTokens, total_tokens = response.InputTokens + response.OutputTokens }
    });
}).AllowAnonymous();

app.MapPost("/v1/messages", async (HttpContext context, IProviderRouter provider,
    IKeyVault keyVault, IUsageJournal usage, LocalBudgetService budget, CancellationToken cancellationToken) =>
{
    var body = await ReadBodyAsync(context, cancellationToken);
    var model = Header(context, "X-Regent-Model") ?? body?["model"]?.GetValue<string>() ?? "local-cheap";
    var tenant = Header(context, "X-Regent-Tenant") ?? "demo";
    var prompt = ExtractAnthropicPrompt(body);
    var maxOutput = body?["max_tokens"]?.GetValue<int?>() ?? 512;
    var key = await ResolveKeyAsync(context, tenant, keyVault, cancellationToken);
    if (!await budget.TryReserveAsync(Math.Max(1, maxOutput), cancellationToken))
    {
        return Results.Problem(statusCode: 429, title: "Budget limit reached.", extensions: new Dictionary<string, object?>
        {
            ["error"] = new { code = "BUDGET_HARD_LIMIT", message = "Local monthly limit reached.", retryable = false }
        });
    }
    var response = await provider.CompleteAsync(new ProviderRequest("anthropic", model, "anthropic_messages", prompt, maxOutput, key, body?.ToJsonString()), cancellationToken);
    await usage.AppendAsync(new UsageRecord(DateTimeOffset.UtcNow, tenant, "anthropic", model,
        response.InputTokens, response.OutputTokens, response.ProviderRequestId), cancellationToken);
    if (response.NativeResponseJson is { } nativeResponse) return Results.Content(nativeResponse, "application/json");
    return Results.Json(new
    {
        id = $"msg-{response.ProviderRequestId}",
        type = "message",
        role = "assistant",
        model,
        content = new[] { new { type = "text", text = response.Text } },
        stop_reason = "end_turn",
        usage = new { input_tokens = response.InputTokens, output_tokens = response.OutputTokens }
    });
}).AllowAnonymous();

var admin = app.MapGroup("/admin/keys").AllowAnonymous();
admin.MapPost("/{provider}/{model}", async (string provider, string model, SetKeyRequest request,
    IKeyVault keyVault, CancellationToken cancellationToken) =>
    Results.Ok(await keyVault.SetAsync("demo", provider, model, request.ApiKey, cancellationToken)));
admin.MapGet("", async (IKeyVault keyVault, CancellationToken cancellationToken) =>
    Results.Ok(await keyVault.ListAsync("demo", cancellationToken)));
admin.MapPost("/{keyRef}/rotate", async (string keyRef, SetKeyRequest request,
    IKeyVault keyVault, CancellationToken cancellationToken) =>
    Results.Ok(await keyVault.RotateAsync(keyRef, request.ApiKey, cancellationToken)));
admin.MapDelete("/{keyRef}", async (string keyRef, IKeyVault keyVault, CancellationToken cancellationToken) =>
{
    await keyVault.DeleteAsync("demo", keyRef, cancellationToken);
    return Results.NoContent();
});

app.Run();

static string? Header(HttpContext context, string name) =>
    context.Request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;

static async Task<JsonNode?> ReadBodyAsync(HttpContext context, CancellationToken cancellationToken)
{
    var text = await new StreamReader(context.Request.Body).ReadToEndAsync(cancellationToken);
    return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
}

static string ExtractOpenAiPrompt(JsonNode? body)
{
    var parts = new List<string>();
    if (body?["messages"] is JsonArray messages)
    {
        foreach (var item in messages)
        {
            if (item?["content"]?.GetValue<string>() is { } content) parts.Add(content);
        }
    }
    return string.Join("\n", parts);
}

static string ExtractAnthropicPrompt(JsonNode? body)
{
    var parts = new List<string>();
    if (body?["messages"] is JsonArray messages)
    {
        foreach (var item in messages)
        {
            if (item?["content"]?.GetValue<string>() is { } content) parts.Add(content);
        }
    }
    if (body?["system"]?.GetValue<string>() is { } system) parts.Insert(0, system);
    return string.Join("\n", parts);
}

static async Task<string?> ResolveKeyAsync(HttpContext context, string tenant, IKeyVault keyVault, CancellationToken cancellationToken)
{
    var keyRef = Header(context, "X-Regent-Key-Ref");
    return string.IsNullOrWhiteSpace(keyRef) ? null : await keyVault.UnlockAsync(tenant, keyRef, cancellationToken);
}

public sealed record SetKeyRequest(string ApiKey);
