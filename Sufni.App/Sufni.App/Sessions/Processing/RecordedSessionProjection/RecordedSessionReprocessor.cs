using System;
using Serilog;
using Sufni.App.Shared.Models;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Bikes.Services;
using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Rebuilds processed telemetry from a persisted recorded raw source.
/// It decodes the source payload, reconstructs the bike processing context,
/// projects GPS points, and returns the matching fingerprint.
/// </summary>
internal sealed class RecordedSessionReprocessor(
    IProcessingFingerprintService fingerprintService,
    ITelemetryBikeProcessingContextFactory bikeProcessingContextFactory)
    : IRecordedSessionReprocessor
{
    private static readonly ILogger logger = Log.ForContext<RecordedSessionReprocessor>();

    public Task<RecordedSessionReprocessResult> ReprocessAsync(
        RecordedSessionDerivationInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = input.Source;
        var processingOptions = input.ProcessingOptions;
        if (source.SessionId != input.Session.RecordingSourceSessionId)
            throw new InvalidOperationException("Recorded source does not match the derivation session.");

        var bikeProcessingContext = bikeProcessingContextFactory.Create(input.Setup, input.Bike);
        if (bikeProcessingContext.RearFailure is { } calibrationFailure)
            return Task.FromResult<RecordedSessionReprocessResult>(new RecordedSessionReprocessResult.Failed(
                calibrationFailure.Reason == RearCalibrationFailureReason.Configuration
                    ? SessionProcessingFailureReason.InvalidCalibration : SessionProcessingFailureReason.Calibration,
                calibrationFailure.Details));
        var bikeData = bikeProcessingContext.BikeData;

        TelemetryData telemetryData;
        try
        {
            telemetryData = source.SourceKind switch
            {
                RecordedSessionSourceKind.ImportedSst => ReprocessImportedSst(source, input.Payload, bikeData, processingOptions, input.Session.DerivationWindow),
                RecordedSessionSourceKind.LiveCapture => ReprocessLiveCapture(input.Payload, bikeData, processingOptions, input.Session.DerivationWindow),
                _ => throw new ArgumentOutOfRangeException(nameof(source.SourceKind), source.SourceKind, "Unknown recorded source kind.")
            };
        }
        catch (TelemetryProcessingException e)
        {
            logger.Warning(e, "Processing failed for session {SessionId}, source {SourceName}: {Reason}",
                input.Session.Id, source.SourceName, e.Reason);
            var reason = e.Reason == TelemetryProcessingFailureReason.NoTravelData
                ? SessionProcessingFailureReason.NoTravelData : SessionProcessingFailureReason.InvalidRecording;
            return Task.FromResult<RecordedSessionReprocessResult>(new RecordedSessionReprocessResult.Failed(reason, e.Message));
        }

        var points = telemetryData.GpsData is { Length: > 0 }
            ? GpsTrackPointProjection.ProjectAll(telemetryData.GpsData)
            : null;
        var fingerprint = fingerprintService.CreateCurrent(
            input.Session,
            input.Setup,
            input.Bike,
            source,
            bikeProcessingContext.DependencyHash);
        var fingerprintJson = AppJson.Serialize(fingerprint);
        var processedTelemetry = new ProcessedTelemetryPayload(
            telemetryData,
            telemetryData.BinaryForm,
            fingerprintJson);

        return Task.FromResult<RecordedSessionReprocessResult>(new RecordedSessionReprocessResult.Processed(ProcessedGeneration.Create(processedTelemetry, points), fingerprint));
    }

    private static TelemetryData ReprocessImportedSst(
        RecordedSessionSourceSnapshot source,
        ReadOnlyMemory<byte> payload,
        BikeData bikeData,
        TelemetryProcessingOptions processingOptions,
        RecordedSessionDerivationWindow? window)
    {
        var sstBytes = RecordedSessionSourcePayloadCodec.DecompressImportedSst(payload);
        var rawTelemetryData = RawTelemetryData.FromByteArray(sstBytes);
        if (window is not null)
        {
            rawTelemetryData = rawTelemetryData.Slice(window.StartSeconds, window.EndSeconds);
        }

        var metadata = MetadataFromRaw(source.SourceName, rawTelemetryData);
        return TelemetryData.FromRecording(rawTelemetryData, metadata, bikeData, processingOptions);
    }

    private static TelemetryData ReprocessLiveCapture(
        ReadOnlyMemory<byte> sourcePayload,
        BikeData bikeData,
        TelemetryProcessingOptions processingOptions,
        RecordedSessionDerivationWindow? window)
    {
        var capture = TelemetryCodec.DecodeLiveSource(sourcePayload).ToCapture(bikeData);
        if (window is not null)
        {
            capture = capture.Slice(window.StartSeconds, window.EndSeconds);
        }

        return TelemetryData.FromLiveCapture(capture, processingOptions);
    }

    internal static Metadata MetadataFromRaw(string sourceName, RawTelemetryData rawTelemetryData) => new()
    {
        SourceName = sourceName,
        Version = rawTelemetryData.Version,
        SampleRate = rawTelemetryData.SampleRate,
        Timestamp = rawTelemetryData.Timestamp,
        Duration = rawTelemetryData.RecordingDurationSeconds ?? (rawTelemetryData.SampleRate > 0
            ? (double)Math.Max(rawTelemetryData.Front.Length, rawTelemetryData.Rear.Length) / rawTelemetryData.SampleRate
            : 0.0)
    };

}
