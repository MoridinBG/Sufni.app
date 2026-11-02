using System.Collections.Immutable;
using Sufni.App.Shared.Models;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Acquisition.Coordinators;

/// <summary>The current import run, retained across tab lifetimes until the next run.</summary>
public abstract record SessionImportState(ImmutableList<SessionImportEvent> Events)
{
    public sealed record Idle() : SessionImportState([]);
    public sealed record SourceBusy(DaqTransportHolder Holder) : SessionImportState([]);
    public sealed record SourceUnavailable() : SessionImportState([]);

    public sealed record Running(
        int Current,
        int Total,
        ImmutableList<SessionImportEvent> Events) : SessionImportState(Events);

    public sealed record Completed(
        SessionImportResult Result,
        ImmutableList<SessionImportEvent> Events) : SessionImportState(Events);

    public sealed record Failed(
        SessionImportFailureReason Reason,
        string? Details,
        ImmutableList<SessionImportEvent> Events) : SessionImportState(Events);
}
