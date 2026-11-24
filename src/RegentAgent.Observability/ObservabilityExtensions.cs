using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace RegentAgent.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddRegentObservability(this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Observability");
        var options = new ObservabilityOptions
        {
            Profile = section["Profile"] ?? "local",
            Metrics = ReadBool(section["Signals:Metrics"], true),
            Logs = ReadBool(section["Signals:Logs"], true),
            Traces = ReadBool(section["Signals:Traces"], true),
            Sampling = double.TryParse(section["Sampling"], NumberStyles.Float, CultureInfo.InvariantCulture,
                out var sampling) ? sampling : 0.1,
            Redaction = section["Redaction"] ?? "strict",
            OtlpEndpoint = section["OtlpEndpoint"],
            ServiceName = section["ServiceName"] ?? "regent-agent",
            ServiceVersion = section["ServiceVersion"] ?? "local"
        };
        options.Validate();
        services.AddSingleton(options);
        services.Configure<OpenTelemetryLoggerOptions>(loggerOptions =>
        {
            loggerOptions.ParseStateValues = true;
            loggerOptions.IncludeFormattedMessage = false;
        });
        if (options.Profile.Equals("off", StringComparison.OrdinalIgnoreCase)) return services;

        var builder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName,
                serviceVersion: options.ServiceVersion));
        if (options.Logs)
            builder.WithLogging(logging =>
            {
                logging.AddProcessor(new StrictLogRedactionProcessor());
                if (options.Profile.Equals("local", StringComparison.OrdinalIgnoreCase)) logging.AddConsoleExporter();
                else logging.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = new Uri(options.OtlpEndpoint!);
                    exporter.TimeoutMilliseconds = 5000;
                });
            });
        if (options.Traces)
            builder.WithTracing(tracing =>
            {
                tracing.SetSampler(new TraceIdRatioBasedSampler(options.Sampling));
                tracing.AddAspNetCoreInstrumentation(instrumentation => instrumentation.RecordException = false);
                tracing.AddHttpClientInstrumentation(instrumentation => instrumentation.RecordException = false);
                tracing.AddProcessor(new StrictActivityRedactionProcessor());
                if (options.Profile.Equals("local", StringComparison.OrdinalIgnoreCase)) tracing.AddConsoleExporter();
                else tracing.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = new Uri(options.OtlpEndpoint!);
                    exporter.TimeoutMilliseconds = 5000;
                });
            });
        if (options.Metrics)
            builder.WithMetrics(metrics =>
            {
                if (options.Profile.Equals("local", StringComparison.OrdinalIgnoreCase)) metrics.AddConsoleExporter();
                else metrics.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = new Uri(options.OtlpEndpoint!);
                    exporter.TimeoutMilliseconds = 5000;
                });
            });
        return services;
    }

    private static bool ReadBool(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;
}
