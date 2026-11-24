using RegentAgent.Observability;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ObservabilityOptionsTests
{
    [Fact]
    public void Local_profile_is_the_default_and_off_requires_no_endpoint()
    {
        var options = new ObservabilityOptions();
        options.Validate();
        options.Profile = "off";
        options.Validate();
        options.Profile = "LOCAL";
        options.Validate();
        Assert.Equal("local", options.Profile);
    }

    [Fact]
    public void Remote_profiles_require_a_secure_or_internal_otlp_endpoint()
    {
        var options = new ObservabilityOptions { Profile = "otlp" };
        Assert.Throws<InvalidOperationException>(options.Validate);
        options.OtlpEndpoint = "http://collector.example.test:4317";
        Assert.Throws<InvalidOperationException>(options.Validate);
        options.OtlpEndpoint = "http://collector:4317";
        options.Validate();
        options.Profile = "elastic";
        options.OtlpEndpoint = "https://telemetry.example.test:4317";
        options.Validate();
    }

    [Fact]
    public void Sampling_and_redaction_are_validated()
    {
        var options = new ObservabilityOptions { Sampling = 1.1 };
        Assert.Throws<InvalidOperationException>(options.Validate);
        options.Sampling = 0.1;
        options.Redaction = "none";
        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
