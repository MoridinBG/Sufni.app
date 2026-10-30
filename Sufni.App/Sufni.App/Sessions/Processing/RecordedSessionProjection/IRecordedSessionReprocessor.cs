using System;
using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Stores;
using Sufni.App.Sessions.Store;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;
using Sufni.App.Shared.Models;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>The captured inputs for one recorded-session derivation.</summary>
public sealed record RecordedSessionDerivationInput(
    SessionSnapshot Session,
    SetupSnapshot Setup,
    BikeSnapshot Bike,
    RecordedSessionSourceSnapshot Source,
    ReadOnlyMemory<byte> Payload)
{
    public static RecordedSessionDerivationInput Create(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSource source) =>
        new(session, setup, bike, RecordedSessionSourceSnapshot.From(source), source.Payload);

    public TelemetryProcessingOptions ProcessingOptions => Session.ProcessingOptions;
}

/// <summary>Derives telemetry and its fingerprint from one captured input.</summary>
public interface IRecordedSessionReprocessor
{
    Task<RecordedSessionReprocessResult> ReprocessAsync(
        RecordedSessionDerivationInput input,
        CancellationToken cancellationToken = default);
}

// Telemetry and its serialized form are produced together, without a decode on the write path.
public sealed record ProcessedTelemetryPayload(
    TelemetryData TelemetryData,
    byte[] Data,
    string? FingerprintJson);

public abstract record RecordedSessionReprocessResult
{
    private RecordedSessionReprocessResult() { }
    public sealed record Processed(
        ProcessedGeneration Generation,
        ProcessingFingerprint Fingerprint) : RecordedSessionReprocessResult;
    public sealed record Failed(SessionProcessingFailureReason Reason, string? Details = null) : RecordedSessionReprocessResult;
}
