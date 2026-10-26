using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Reliquary.BuildingBlocks;

/// <summary>
/// The trace source and the meter of Reliquary. The core components use them for their own spans and metrics.
/// </summary>
public static class Telemetry
{
    /// <summary>The name of the trace source and of the meter.</summary>
    public const string Name = "Reliquary";

    /// <summary>Gets the trace source for the spans of the core components.</summary>
    public static ActivitySource ActivitySource { get; } = new(Name);

    /// <summary>Gets the meter for the metrics of the core components.</summary>
    public static Meter Meter { get; } = new(Name);
}
