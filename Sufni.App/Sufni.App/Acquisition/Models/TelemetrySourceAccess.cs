using System;
using System.Collections.Immutable;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Models;

namespace Sufni.App.Acquisition.Models;

public closed record TelemetryFileListing
{
    private TelemetryFileListing() { }
    public sealed record Listed(ImmutableArray<ITelemetryFile> Files, Guid? BoardId) : TelemetryFileListing;
    public sealed record Busy(DaqTransportHolder Holder) : TelemetryFileListing;
    public sealed record Unavailable : TelemetryFileListing;
    public sealed record Failed(ImportSourceFailureReason Reason, string? Details) : TelemetryFileListing;
}

public closed record TelemetryImportBatchAcquisition
{
    private TelemetryImportBatchAcquisition() { }
    public sealed record Acquired(IAsyncDisposable? Lifetime) : TelemetryImportBatchAcquisition;
    public sealed record Busy(DaqTransportHolder Holder) : TelemetryImportBatchAcquisition;
    public sealed record Unavailable : TelemetryImportBatchAcquisition;
}
