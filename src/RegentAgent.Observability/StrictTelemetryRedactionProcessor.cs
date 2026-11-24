using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace RegentAgent.Observability;

public sealed class StrictLogRedactionProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        if (data.Attributes is not null)
            data.Attributes = data.Attributes.Select(attribute => new KeyValuePair<string, object?>(
                attribute.Key, SensitiveDataPolicy.IsSensitiveKey(attribute.Key) ? SensitiveDataPolicy.Redacted : attribute.Value)).ToArray();
        data.Body = null;
        data.FormattedMessage = null;
        if (data.Exception is not null)
        {
            data.Attributes = (data.Attributes ?? []).Append(new KeyValuePair<string, object?>(
                "exception.type", data.Exception.GetType().FullName)).ToArray();
            data.Exception = null;
        }
    }
}

public sealed class StrictActivityRedactionProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        foreach (var tag in data.TagObjects.Where(tag => SensitiveDataPolicy.IsSensitiveKey(tag.Key)).ToArray())
            data.SetTag(tag.Key, SensitiveDataPolicy.Redacted);
    }
}
