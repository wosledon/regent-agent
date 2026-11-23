using RegentAgent.Agents;
using RegentAgent.Runner;
using RegentAgent.Sandbox;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient("controlplane", client =>
    client.BaseAddress = new Uri(builder.Configuration["ControlPlane:BaseUrl"] ?? "http://localhost:5080"));
builder.Services.AddHttpClient("gateway", client =>
    client.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"] ?? "http://localhost:5081"));
builder.Services.AddSingleton<ISandboxExecutor>(sp =>
    new LocalDirectorySandbox(sp.GetRequiredService<IConfiguration>()["Sandbox:Root"] ?? "data/workspaces"));
builder.Services.AddSingleton<IAgentExecutor>(sp =>
    new LocalAgentExecutor(sp.GetRequiredService<IHttpClientFactory>().CreateClient("gateway")));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
