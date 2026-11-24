using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using RegentAgent.Observability;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ObservabilityRedactionTests
{
    [Fact]
    public void Strict_log_processor_removes_sensitive_values_and_exception_details()
    {
        var sink = new CaptureLogProcessor();
        using var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.ParseStateValues = true;
            options.AddProcessor(new StrictLogRedactionProcessor());
            options.AddProcessor(sink);
        }));
        factory.CreateLogger("redaction-test").LogError(new InvalidOperationException("sensitive exception detail"),
            "token={Token} prompt={Prompt} tenant={Tenant}", "secret-token", "private prompt", "tenant-a");

        Assert.NotNull(sink.LastAttributes);
        Assert.Equal(SensitiveDataPolicy.Redacted, sink.LastAttributes!["Token"]);
        Assert.Equal(SensitiveDataPolicy.Redacted, sink.LastAttributes["Prompt"]);
        Assert.Equal("tenant-a", sink.LastAttributes["Tenant"]);
        Assert.Equal(typeof(InvalidOperationException).FullName, sink.LastAttributes["exception.type"]);
        Assert.Null(sink.LastFormattedMessage);
        Assert.Null(sink.LastException);
    }

    [Fact]
    public void Strict_activity_processor_redacts_sensitive_tags()
    {
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("redaction-test")
            .AddProcessor(new StrictActivityRedactionProcessor())
            .Build();
        using var source = new ActivitySource("redaction-test");
        using var activity = source.StartActivity("operation");
        Assert.NotNull(activity);
        activity!.SetTag("api_key", "secret-key");
        activity.SetTag("tenant_id", "tenant-a");
        activity.Stop();

        Assert.Equal(SensitiveDataPolicy.Redacted, activity.GetTagItem("api_key"));
        Assert.Equal("tenant-a", activity.GetTagItem("tenant_id"));
    }

    private sealed class CaptureLogProcessor : BaseProcessor<LogRecord>
    {
        public IReadOnlyDictionary<string, object?>? LastAttributes { get; private set; }
        public string? LastFormattedMessage { get; private set; }
        public Exception? LastException { get; private set; }
        public override void OnEnd(LogRecord data)
        {
            LastAttributes = data.Attributes?.ToDictionary(x => x.Key, x => x.Value);
            LastFormattedMessage = data.FormattedMessage;
            LastException = data.Exception;
        }
    }
}
