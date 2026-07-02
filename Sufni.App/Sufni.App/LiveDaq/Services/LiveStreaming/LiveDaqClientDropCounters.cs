namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record LiveDaqClientDropCounters(
    ulong RawTelemetryFramesSkipped,
    ulong ParsedTelemetryFramesDropped,
    ulong SubscriberFramesDropped,
    ulong SignalBatchesCoalesced,
    ulong SignalSamplesDiscarded,
    ulong AnalysisRecomputesSkipped)
{
    public static readonly LiveDaqClientDropCounters Empty = new(0, 0, 0, 0, 0, 0);

    public bool HasDrops => RawTelemetryFramesSkipped > 0
        || ParsedTelemetryFramesDropped > 0
        || SubscriberFramesDropped > 0
        || SignalBatchesCoalesced > 0
        || SignalSamplesDiscarded > 0
        || AnalysisRecomputesSkipped > 0;

    public LiveDaqClientDropCounters Add(LiveDaqClientDropCounters other) => new(
        RawTelemetryFramesSkipped + other.RawTelemetryFramesSkipped,
        ParsedTelemetryFramesDropped + other.ParsedTelemetryFramesDropped,
        SubscriberFramesDropped + other.SubscriberFramesDropped,
        SignalBatchesCoalesced + other.SignalBatchesCoalesced,
        SignalSamplesDiscarded + other.SignalSamplesDiscarded,
        AnalysisRecomputesSkipped + other.AnalysisRecomputesSkipped);
}
