using System.Diagnostics;

namespace LupiraContactApi.Core.Domain;

/// <summary>Domain-specific tracing source, registered with OpenTelemetry in Program.cs.</summary>
public static class Telemetry
{
    public const string ActivitySourceName = "LupiraContactApi.Calendar";
    public static readonly ActivitySource Source = new(ActivitySourceName);
}
