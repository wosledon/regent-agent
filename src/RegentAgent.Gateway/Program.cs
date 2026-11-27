using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using RegentAgent.AgentIdentity;
using RegentAgent.Gateway;
using RegentAgent.Gateway.KeyVault;
using RegentAgent.Gateway.Metering;
using RegentAgent.Observability;
using RegentAgent.Providers;
using RegentAgent.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRegentObservability(builder.Configuration);
builder.Services.AddAgentIdentity(builder.Configuration);
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Servers = [];
        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails();
var providerMode = builder.Configuration["Gateway:ProviderMode"] ?? "local";
if (providerMode.Trim().ToLowerInvariant() is not ("local" or "remote"))
    throw new InvalidOperationException("Gateway provider mode must be local or remote.");
var gatewayAdminToken = builder.Configuration["Gateway:AdminToken"];
if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(gatewayAdminToken))
    throw new InvalidOperationException("Production Gateway requires Gateway:AdminToken.");
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
var configuredControlPlaneUrl = builder.Configuration["Gateway:ControlPlaneUrl"];
var controlPlaneUrl = string.IsNullOrWhiteSpace(configuredControlPlaneUrl) ? "http://localhost:5080" : configuredControlPlaneUrl;
var meteringToken = builder.Configuration["Gateway:MeteringToken"];
if (builder.Environment.IsProduction() && (string.IsNullOrWhiteSpace(meteringToken)
    || string.IsNullOrWhiteSpace(configuredControlPlaneUrl)
    || !Uri.TryCreate(configuredControlPlaneUrl, UriKind.Absolute, out _)))
    throw new InvalidOperationException("Production Gateway requires Gateway:ControlPlaneUrl and Gateway:MeteringToken.");
builder.Services.AddHttpClient("controlplane-metering", client =>
{
    client.BaseAddress = new Uri(controlPlaneUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
    if (!string.IsNullOrWhiteSpace(meteringToken)) client.DefaultRequestHeaders.Add("X-Regent-Metering-Token", meteringToken);
});
builder.Services.AddHttpClient("controlplane-assignment", client =>
{
    client.BaseAddress = new Uri(controlPlaneUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<AgentAssignmentAccessValidator>();
builder.Services.AddSingleton(new JsonDocumentStore<UsageProjectionRecord>(
    builder.Configuration["Gateway:UsageProjectionPath"] ?? "data/gateway-usage-projections.json"));
builder.Services.AddSingleton<IUsageProjector, HttpUsageProjector>();
builder.Services.AddHostedService<UsageProjectionWorker>();
var keyVaultProvider = builder.Configuration["Gateway:KeyVault:Provider"] ?? "local";
if (keyVaultProvider.Trim().ToLowerInvariant() is not ("local" or "postgres"))
    throw new InvalidOperationException("Gateway keyvault provider must be local or postgres.");
if (keyVaultProvider.Equals("local", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IKeyVault>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var master = new LocalMasterKeyProvider(config["Gateway:MasterKeyPath"] ?? "data/secrets/master.key",
            config["Gateway:MasterKey"]);
        return new LocalFileKeyVault(config["Gateway:KeyVaultPath"] ?? "data/keyvault.json", master);
    });
}
else
{
    var keyVaultOptions = new PostgresKeyVaultOptions
    {
        ConnectionString = builder.Configuration["Gateway:KeyVault:ConnectionString"] ?? string.Empty,
        Schema = builder.Configuration["Gateway:KeyVault:Schema"] ?? "keyvault",
        Table = builder.Configuration["Gateway:KeyVault:Table"] ?? "model_keys",
        CommandTimeoutSeconds = builder.Configuration.GetValue("Gateway:KeyVault:CommandTimeoutSeconds", 15),
        RequireTls = builder.Configuration.GetValue("Gateway:KeyVault:RequireTls", true)
    };
    keyVaultOptions.Validate();
    if (string.IsNullOrWhiteSpace(builder.Configuration["Gateway:MasterKey"]))
        throw new InvalidOperationException("Postgres keyvault requires Gateway:MasterKey from the secret provider.");
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(keyVaultOptions.ConnectionString));
    builder.Services.AddSingleton(keyVaultOptions);
    builder.Services.AddSingleton<IKeyVault>(sp => new PostgresKeyVault(sp.GetRequiredService<NpgsqlDataSource>(),
        new LocalMasterKeyProvider(builder.Configuration["Gateway:MasterKeyPath"] ?? "data/secrets/master.key",
            builder.Configuration["Gateway:MasterKey"]), sp.GetRequiredService<PostgresKeyVaultOptions>()));
}
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(ModelPriceBook.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton(new ModelPricingService(
    builder.Configuration.GetValue("Gateway:Pricing:SafetyMarginBasisPoints", 1000)));
builder.Services.AddSingleton(sp => new LocalBudgetService(
    sp.GetRequiredService<IConfiguration>().GetValue<long>("Gateway:MonthlyLimit", 800_000),
    builder.Configuration["Gateway:BudgetCurrency"] ?? "CNY",
    builder.Configuration["Gateway:BudgetPath"] ?? "data/gateway-budget.json",
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<BudgetExpiryWorker>();
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
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.StartsWith("/v1/", StringComparison.Ordinal))
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        try
        {
            var identity = context.RequestServices.GetRequiredService<AgentAssignmentTokenValidator>()
                .Validate(authorization["Bearer ".Length..].Trim(), "regent-gateway");
            if (!identity.Scopes.Contains("infer:employee")) throw new SecurityTokenException("Employee inference scope is required.");
            await context.RequestServices.GetRequiredService<AgentAssignmentAccessValidator>()
                .ValidateAsync(authorization["Bearer ".Length..].Trim(), identity, context.RequestAborted);
            context.Items[AgentIdentityContext.Key] = identity;
        }
        catch (SecurityTokenException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    if ((path.StartsWith("/admin/keys", StringComparison.Ordinal) || path.StartsWith("/admin/budget", StringComparison.Ordinal))
        && !string.IsNullOrWhiteSpace(gatewayAdminToken)
        && !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(context.Request.Headers["X-Regent-Admin-Key"].ToString()),
            Encoding.UTF8.GetBytes(gatewayAdminToken)))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});
app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", mode = providerMode })).AllowAnonymous();

app.MapPost("/v1/chat/completions", async (HttpContext context, IProviderRouter provider,
    IKeyVault keyVault, IUsageJournal usage, IUsageProjector projector, LocalBudgetService budget, ModelPriceBook priceBook,
    ModelPricingService pricing, TimeProvider clock, CancellationToken cancellationToken) =>
{
    var body = await ReadBodyAsync(context, cancellationToken);
    var model = Header(context, "X-Regent-Model") ?? body?["model"]?.GetValue<string>() ?? "local-cheap";
    var identity = AgentIdentityContext.Required(context.Items);
    var prompt = ExtractOpenAiPrompt(body);
    var maxOutput = body?["max_tokens"]?.GetValue<int?>() ?? 512;
    var key = await ResolveKeyAsync(context, identity.TenantId.ToString(), keyVault, cancellationToken);
    var reservation = await ReserveBudgetAsync(context, identity, "openai", model, prompt, maxOutput,
        priceBook, pricing, budget, clock, cancellationToken);
    if (reservation is null) return BudgetLimitProblem();
    context.Response.Headers["X-Regent-Budget-Reservation-Id"] = reservation.Id.ToString();
    var request = new ProviderRequest("openai", model, "openai_chat_completions", prompt, maxOutput, key,
        body?.ToJsonString(), (attempt, token) => ObserveAttemptAsync(usage, projector, budget, pricing, clock, reservation, attempt, token));
    try
    {
        if (body?["stream"]?.GetValue<bool>() == true)
        {
            await WriteProviderStreamAsync(context, provider, usage, projector, budget, pricing, clock, reservation, request, cancellationToken);
            await budget.CompleteAsync(reservation.Id, cancellationToken);
            return Results.Empty;
        }
        var response = await provider.CompleteAsync(request, cancellationToken);
        await budget.CompleteAsync(reservation.Id, cancellationToken);
        if (response.NativeResponseJson is { } nativeResponse) return Results.Content(nativeResponse, "application/json");
        return Results.Json(new
        {
            id = $"chatcmpl-{response.ProviderRequestId}", @object = "chat.completion",
            created = clock.GetUtcNow().ToUnixTimeSeconds(), model,
            choices = new[] { new { index = 0, message = new { role = "assistant", content = response.Text }, finish_reason = "stop" } },
            usage = new { prompt_tokens = response.InputTokens, completion_tokens = response.OutputTokens, total_tokens = response.InputTokens + response.OutputTokens }
        });
    }
    catch
    {
        await budget.ReleaseAsync(reservation.Id, "Provider request failed", cancellationToken);
        throw;
    }
}).AllowAnonymous();

app.MapPost("/v1/messages", async (HttpContext context, IProviderRouter provider,
    IKeyVault keyVault, IUsageJournal usage, IUsageProjector projector, LocalBudgetService budget, ModelPriceBook priceBook,
    ModelPricingService pricing, TimeProvider clock, CancellationToken cancellationToken) =>
{
    var body = await ReadBodyAsync(context, cancellationToken);
    var model = Header(context, "X-Regent-Model") ?? body?["model"]?.GetValue<string>() ?? "local-cheap";
    var identity = AgentIdentityContext.Required(context.Items);
    var prompt = ExtractAnthropicPrompt(body);
    var maxOutput = body?["max_tokens"]?.GetValue<int?>() ?? 512;
    var key = await ResolveKeyAsync(context, identity.TenantId.ToString(), keyVault, cancellationToken);
    var reservation = await ReserveBudgetAsync(context, identity, "anthropic", model, prompt, maxOutput,
        priceBook, pricing, budget, clock, cancellationToken);
    if (reservation is null) return BudgetLimitProblem();
    context.Response.Headers["X-Regent-Budget-Reservation-Id"] = reservation.Id.ToString();
    var request = new ProviderRequest("anthropic", model, "anthropic_messages", prompt, maxOutput, key,
        body?.ToJsonString(), (attempt, token) => ObserveAttemptAsync(usage, projector, budget, pricing, clock, reservation, attempt, token));
    try
    {
        if (body?["stream"]?.GetValue<bool>() == true)
        {
            await WriteProviderStreamAsync(context, provider, usage, projector, budget, pricing, clock, reservation, request, cancellationToken);
            await budget.CompleteAsync(reservation.Id, cancellationToken);
            return Results.Empty;
        }
        var response = await provider.CompleteAsync(request, cancellationToken);
        await budget.CompleteAsync(reservation.Id, cancellationToken);
        if (response.NativeResponseJson is { } nativeResponse) return Results.Content(nativeResponse, "application/json");
        return Results.Json(new
        {
            id = $"msg-{response.ProviderRequestId}", type = "message", role = "assistant", model,
            content = new[] { new { type = "text", text = response.Text } }, stop_reason = "end_turn",
            usage = new { input_tokens = response.InputTokens, output_tokens = response.OutputTokens }
        });
    }
    catch
    {
        await budget.ReleaseAsync(reservation.Id, "Provider request failed", cancellationToken);
        throw;
    }
}).AllowAnonymous();

var admin = app.MapGroup("/admin/keys").AllowAnonymous();
admin.MapPost("/{provider}/{model}", async (string provider, string model, SetKeyRequest request,
    IKeyVault keyVault, CancellationToken cancellationToken) =>
    Results.Ok(await keyVault.SetAsync("demo", provider, model, request.ApiKey, cancellationToken)));
admin.MapGet("", async (IKeyVault keyVault, CancellationToken cancellationToken) =>
    Results.Ok(await keyVault.ListAsync("demo", cancellationToken)));
admin.MapPost("/{keyRef}/rotate", async (string keyRef, SetKeyRequest request,
    IKeyVault keyVault, CancellationToken cancellationToken) =>
    Results.Ok(await keyVault.RotateAsync("demo", keyRef, request.ApiKey, cancellationToken)));
admin.MapDelete("/{keyRef}", async (string keyRef, IKeyVault keyVault, CancellationToken cancellationToken) =>
{
    await keyVault.DeleteAsync("demo", keyRef, cancellationToken);
    return Results.NoContent();
});

var budgetAdmin = app.MapGroup("/admin/budget").AllowAnonymous();
budgetAdmin.MapGet("/{tenantId}/periods/{period}", async (string tenantId, string period,
    LocalBudgetService budget, CancellationToken cancellationToken) =>
    Results.Ok(await budget.GetSnapshotAsync(tenantId, period, cancellationToken)))
    .Produces<BudgetSnapshot>();
budgetAdmin.MapGet("/{tenantId}/ledger", async (string tenantId, string? period, string? provider,
    LocalBudgetService budget, CancellationToken cancellationToken) =>
    Results.Ok(await budget.ListLedgerAsync(tenantId, period, provider, cancellationToken)))
    .Produces<IReadOnlyList<BudgetLedgerEntry>>();
budgetAdmin.MapPost("/{tenantId}/reconcile", async (string tenantId, BudgetReconciliationRequest request,
    LocalBudgetService budget, CancellationToken cancellationToken) =>
    Results.Ok(await budget.ReconcileAsync(tenantId, request, cancellationToken)))
    .Produces<BudgetReconciliationResult>();
budgetAdmin.MapPost("/{tenantId}/corrections", async (string tenantId, BudgetCorrectionRequest request,
    LocalBudgetService budget, CancellationToken cancellationToken) =>
    Results.Ok(await budget.CorrectAsync(tenantId, request, cancellationToken)))
    .Produces<BudgetLedgerEntry>();
budgetAdmin.MapPost("/expire", async (LocalBudgetService budget, CancellationToken cancellationToken) =>
    Results.Ok(new { expired = await budget.ExpireAsync(cancellationToken) }));

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

static async Task<BudgetReservationRecord?> ReserveBudgetAsync(HttpContext context, AgentIdentity identity,
    string provider, string model, string prompt, int maxOutput, ModelPriceBook priceBook,
    ModelPricingService pricing, LocalBudgetService budget, TimeProvider clock, CancellationToken cancellationToken)
{
    var now = clock.GetUtcNow();
    PriceQuote quote;
    try { quote = priceBook.Quote(provider, model, now); }
    catch (InvalidOperationException exception) when (exception.Message.StartsWith("UNPRICED", StringComparison.Ordinal))
    {
        throw new ProviderRequestException(422, "UNPRICED", false, "Model has no effective price snapshot.");
    }
    var options = context.RequestServices.GetRequiredService<ProviderHttpOptions>();
    var maxAttempts = (provider == "openai" ? options.OpenAI.MaxRetries : options.Anthropic.MaxRetries) + 1;
    var amount = pricing.EstimateReservationMinor(quote, prompt, maxOutput, maxAttempts);
    return await budget.TryReserveAsync(new BudgetReservationRequest(identity.TenantId.ToString(),
        ModelPricingService.Period(now), amount, quote.Currency, now.AddMinutes(10),
        Header(context, "X-Regent-Request-Id") ?? context.TraceIdentifier,
        identity.EmployeeId.ToString(), identity.TaskId?.ToString(), provider, model, quote), cancellationToken);
}

static async Task ObserveAttemptAsync(IUsageJournal usage, IUsageProjector projector, LocalBudgetService budget,
    ModelPricingService pricing, TimeProvider clock, BudgetReservationRecord reservation,
    ProviderAttemptRecord attempt, CancellationToken cancellationToken)
{
    var quote = reservation.Quote ?? throw new InvalidOperationException("Reservation has no price snapshot.");
    var now = clock.GetUtcNow();
    var amount = pricing.CalculateAmountMinor(quote, attempt.InputTokens, attempt.CachedInputTokens, attempt.OutputTokens);
    var usageEventId = Guid.NewGuid().ToString();
    await budget.RecordUsageAsync(new BudgetUsageRecord(reservation.Id, usageEventId, amount, now,
        attempt.ProviderRequestId, attempt.Attempt, attempt.Succeeded, attempt.ErrorCode), cancellationToken);
    var record = new UsageRecord(now, reservation.TenantId, reservation.Provider, reservation.Model,
        attempt.InputTokens, attempt.OutputTokens, attempt.ProviderRequestId, reservation.EmployeeId, reservation.TaskId)
    {
        ReservationId = reservation.Id, UsageEventId = usageEventId, RequestId = reservation.RequestId,
        CachedInputTokens = attempt.CachedInputTokens, ReasoningTokens = attempt.ReasoningTokens,
        AmountMinor = amount, MinorUnitFactor = quote.MinorUnitFactor, Currency = quote.Currency,
        PriceVersion = quote.PriceVersion, OccurredPeriod = ModelPricingService.Period(now),
        PostedPeriod = ModelPricingService.Period(now), Status = attempt.Succeeded ? "Succeeded" : "Failed",
        Attempt = attempt.Attempt, ErrorCode = attempt.ErrorCode
    };
    await usage.AppendAsync(record, cancellationToken);
    await projector.ProjectAsync(record, cancellationToken);
}

static IResult BudgetLimitProblem() => Results.Problem(statusCode: 429, title: "Budget limit reached.",
    extensions: new Dictionary<string, object?>
    {
        ["error"] = new { code = "BUDGET_HARD_LIMIT", message = "Monthly committed and reserved amount reached the hard limit.", retryable = false }
    });

static async Task WriteProviderStreamAsync(HttpContext context, IProviderRouter provider, IUsageJournal usage,
    IUsageProjector projector, LocalBudgetService budget, ModelPricingService pricing, TimeProvider clock,
    BudgetReservationRecord reservation, ProviderRequest request, CancellationToken cancellationToken)
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers["X-Accel-Buffering"] = "no";
    var inputTokens = 0;
    var cachedInputTokens = 0;
    var outputTokens = 0;
    var reasoningTokens = 0;
    var requestId = $"{request.Provider}-{Guid.NewGuid():N}";
    var completed = false;
    try
    {
        await foreach (var chunk in provider.StreamAsync(request, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(chunk.EventType))
                await context.Response.WriteAsync($"event: {chunk.EventType}\n", cancellationToken);
            await context.Response.WriteAsync($"data: {chunk.Data}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
            UpdateStreamUsage(request.Provider, chunk.Data, ref requestId, ref inputTokens, ref cachedInputTokens,
                ref outputTokens, ref reasoningTokens);
        }
        completed = true;
    }
    finally
    {
        await ObserveAttemptAsync(usage, projector, budget, pricing, clock, reservation, new ProviderAttemptRecord(1,
            requestId, inputTokens, cachedInputTokens, outputTokens, reasoningTokens, completed,
            completed ? null : "STREAM_INTERRUPTED"), cancellationToken);
    }
}

static void UpdateStreamUsage(string providerName, string data, ref string requestId, ref int inputTokens,
    ref int cachedInputTokens, ref int outputTokens, ref int reasoningTokens)
{
    if (data == "[DONE]") return;
    try
    {
        var root = JsonNode.Parse(data);
        if (root is null) return;
        if (providerName == "openai")
        {
            requestId = root["id"]?.GetValue<string>() ?? requestId;
            inputTokens = root["usage"]?["prompt_tokens"]?.GetValue<int>() ?? inputTokens;
            cachedInputTokens = root["usage"]?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<int>() ?? cachedInputTokens;
            outputTokens = root["usage"]?["completion_tokens"]?.GetValue<int>() ?? outputTokens;
            reasoningTokens = root["usage"]?["completion_tokens_details"]?["reasoning_tokens"]?.GetValue<int>() ?? reasoningTokens;
        }
        else if (root["type"]?.GetValue<string>() == "message_start")
        {
            requestId = root["message"]?["id"]?.GetValue<string>() ?? requestId;
            inputTokens = root["message"]?["usage"]?["input_tokens"]?.GetValue<int>() ?? inputTokens;
            cachedInputTokens = (root["message"]?["usage"]?["cache_read_input_tokens"]?.GetValue<int>() ?? 0)
                + (root["message"]?["usage"]?["cache_creation_input_tokens"]?.GetValue<int>() ?? 0);
        }
        else if (root["type"]?.GetValue<string>() == "message_delta")
            outputTokens = root["usage"]?["output_tokens"]?.GetValue<int>() ?? outputTokens;
    }
    catch (JsonException) { }
}

static async Task<string?> ResolveKeyAsync(HttpContext context, string tenant, IKeyVault keyVault, CancellationToken cancellationToken)
{
    var keyRef = Header(context, "X-Regent-Key-Ref");
    return string.IsNullOrWhiteSpace(keyRef) ? null : await keyVault.UnlockAsync(tenant, keyRef, cancellationToken);
}

public sealed record SetKeyRequest(string ApiKey);
