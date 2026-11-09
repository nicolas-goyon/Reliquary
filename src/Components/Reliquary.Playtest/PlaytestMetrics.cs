using System.Diagnostics.Metrics;
using Reliquary.BuildingBlocks;

namespace Reliquary.Playtest;

/// <summary>
/// The metrics of the online games, on the meter <see cref="Telemetry.Meter"/>. See docs/architecture/online-games.md.
/// </summary>
internal static class PlaytestMetrics
{
    /// <summary>The name of the histogram of the time of the hub method <c>Act</c>, in seconds.</summary>
    public const string ActionDurationName = "reliquary.playtest.action.duration";

    /// <summary>The name of the counter of the desynchronizations that the clients report with <c>Resync</c>.</summary>
    public const string DesyncsName = "reliquary.playtest.desyncs";

    /// <summary>The name of the gauge of the open games in the game store.</summary>
    public const string ActiveGamesName = "reliquary.playtest.games.active";

    /// <summary>The name of the counter of the patches that the API sent to the seats.</summary>
    public const string PatchesName = "reliquary.playtest.patches";

    /// <summary>The reasons of <c>Resync</c> that are a desynchronization. Another reason, or no reason, has the value <c>other</c>.</summary>
    public static IReadOnlyList<string> DesyncReasons { get; } = ["hash", "gap", "patch"];

    /// <summary>The reason of <c>Resync</c> when the client has no view, for example after the start. It is not a desynchronization.</summary>
    public const string NoViewReason = "view";

    private static readonly Histogram<double> ActionDuration = Telemetry.Meter.CreateHistogram(
        ActionDurationName,
        unit: "s",
        description: "The time from the receipt of an action by the table hub to the send of its patches to all the seats.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.2, 0.5, 1, 2.5] });

    private static readonly Counter<long> Desyncs = Telemetry.Meter.CreateCounter<long>(
        DesyncsName,
        unit: "{desync}",
        description: "The number of times that a client found that its view was not the view of the server, and asked for a full view.");

    private static readonly Counter<long> Patches = Telemetry.Meter.CreateCounter<long>(
        PatchesName,
        unit: "{patch}",
        description: "The number of patches that the API sent to the seats of the online games.");

    private static Func<long?>? activeGames;

    static PlaytestMetrics() =>
        Telemetry.Meter.CreateObservableGauge(
            ActiveGamesName,
            ObserveActiveGames,
            unit: "{game}",
            description: "The number of open games in the game store: in the lobby or started, with a change in the idle lifetime. Each API replica gives the number of all the replicas.");

    /// <summary>
    /// Records the time of one action.
    /// </summary>
    /// <param name="seconds">The time, in seconds.</param>
    /// <param name="result">The result: <c>accepted</c>, <c>duplicate</c> or <c>rejected</c>.</param>
    public static void RecordAction(double seconds, string result) =>
        ActionDuration.Record(seconds, new KeyValuePair<string, object?>("result", result));

    /// <summary>
    /// Records a <c>Resync</c> of a client. The reason <see cref="NoViewReason"/> is not a desynchronization.
    /// </summary>
    /// <param name="reason">The reason that the client sent.</param>
    public static void RecordResync(string? reason)
    {
        if (reason == NoViewReason)
        {
            return;
        }

        var tag = reason is not null && DesyncReasons.Contains(reason) ? reason : "other";
        Desyncs.Add(1, new KeyValuePair<string, object?>("reason", tag));
    }

    /// <summary>Records one patch sent to one seat.</summary>
    public static void RecordPatch() => Patches.Add(1);

    /// <summary>
    /// Sets the source of the gauge of the open games. The source gives <see langword="null"/> when it has no value yet.
    /// </summary>
    /// <param name="source">The source, or <see langword="null"/> to remove it.</param>
    public static void SetActiveGamesSource(Func<long?>? source) => activeGames = source;

    private static IEnumerable<Measurement<long>> ObserveActiveGames()
    {
        if (activeGames?.Invoke() is { } count)
        {
            yield return new Measurement<long>(count);
        }
    }
}
