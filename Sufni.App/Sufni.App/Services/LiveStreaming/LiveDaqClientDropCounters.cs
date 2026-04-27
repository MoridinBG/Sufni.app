namespace Sufni.App.Services.LiveStreaming;

public sealed record LiveDaqClientDropCounters(
    ulong RawTelemetryFramesSkipped,
    ulong ParsedTelemetryFramesDropped,
    ulong SubscriberFramesDropped,
    ulong GraphBatchesCoalesced,
    ulong GraphSamplesDiscarded,
    ulong StatisticsRecomputesSkipped)
{
    public static readonly LiveDaqClientDropCounters Empty = new(0, 0, 0, 0, 0, 0);

    public bool HasDrops => RawTelemetryFramesSkipped > 0
        || ParsedTelemetryFramesDropped > 0
        || SubscriberFramesDropped > 0
        || GraphBatchesCoalesced > 0
        || GraphSamplesDiscarded > 0
        || StatisticsRecomputesSkipped > 0;

    public LiveDaqClientDropCounters Add(LiveDaqClientDropCounters other) => new(
        RawTelemetryFramesSkipped + other.RawTelemetryFramesSkipped,
        ParsedTelemetryFramesDropped + other.ParsedTelemetryFramesDropped,
        SubscriberFramesDropped + other.SubscriberFramesDropped,
        GraphBatchesCoalesced + other.GraphBatchesCoalesced,
        GraphSamplesDiscarded + other.GraphSamplesDiscarded,
        StatisticsRecomputesSkipped + other.StatisticsRecomputesSkipped);
}
