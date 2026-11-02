using System;
using System.Collections.Immutable;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Acquisition.Models;

public abstract record TelemetryFileListing
{
    private TelemetryFileListing() { }
    public sealed record Listed(ImmutableArray<ITelemetryFile> Files, Guid? BoardId) : TelemetryFileListing;
    public sealed record Busy(DaqTransportHolder Holder) : TelemetryFileListing;
    public sealed record Unavailable : TelemetryFileListing;
}

public abstract record TelemetryImportBatchAcquisition
{
    private TelemetryImportBatchAcquisition() { }
    public sealed record Acquired(IAsyncDisposable? Lifetime) : TelemetryImportBatchAcquisition;
    public sealed record Busy(DaqTransportHolder Holder) : TelemetryImportBatchAcquisition;
    public sealed record Unavailable : TelemetryImportBatchAcquisition;
}
