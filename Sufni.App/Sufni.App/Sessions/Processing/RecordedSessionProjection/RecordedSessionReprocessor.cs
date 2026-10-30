using System;
using Serilog;
using Sufni.App.Shared.Models;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
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
/// projects GPS points and summary metrics, and returns the matching fingerprint.
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
        var payload = JsonSerializer.Deserialize(sourcePayload.Span, AppJson.Context.RecordedLiveCaptureSourcePayload)
                      ?? throw new JsonException("Recorded live-capture source payload is invalid.");
        var hasSegmentPayload = payload.FrontSegments is not null || payload.RearSegments is not null;
        var capture = new LiveTelemetryCapture(
            payload.Metadata,
            bikeData,
            hasSegmentPayload ? payload.FrontSegments ?? [] : CreateDenseSegments(payload.FrontMeasurements ?? []),
            hasSegmentPayload ? payload.RearSegments ?? [] : CreateDenseSegments(payload.RearMeasurements ?? []),
            payload.ImuData,
            payload.GpsData,
            payload.Markers ?? [],
            hasSegmentPayload ? payload.StreamGaps ?? [] : [],
            hasSegmentPayload ? payload.FinalStatus : null,
            hasSegmentPayload && payload.MissingFinalStatus == true)
        {
            TemperatureData = payload.TemperatureData ?? [],
        };
        if (window is not null)
        {
            capture = capture.Slice(window.StartSeconds, window.EndSeconds);
        }

        return TelemetryData.FromLiveCapture(capture, processingOptions);
    }

    private static RawCountSegment[] CreateDenseSegments(ushort[] measurements) =>
        measurements.Length == 0
            ? []
            :
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = measurements,
                }
            ];

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

/// <summary>
/// Stable hash over the track point values that affect stored full-track
/// content. It allows generated tracks to be compared by content instead of by
/// row identity.
/// </summary>
internal static class TrackContentHash
{
    public static string Compute(Track track)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("points");
            writer.WriteStartArray();
            foreach (var point in track.Points)
            {
                writer.WriteStartObject();
                writer.WriteNumber("time", point.Time);
                writer.WriteNumber("x", point.X);
                writer.WriteNumber("y", point.Y);
                writer.WritePropertyName("elevation");
                if (point.Elevation.HasValue)
                {
                    writer.WriteNumberValue(point.Elevation.Value);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WritePropertyName("speed");
                if (point.Speed.HasValue)
                {
                    writer.WriteNumberValue(point.Speed.Value);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WritePropertyName("fixMode");
                if (point.FixMode.HasValue)
                {
                    writer.WriteNumberValue(point.FixMode.Value);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WritePropertyName("satellites");
                if (point.Satellites.HasValue)
                {
                    writer.WriteNumberValue(point.Satellites.Value);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WritePropertyName("epe2d");
                if (point.Epe2d.HasValue)
                {
                    writer.WriteNumberValue(point.Epe2d.Value);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WritePropertyName("epe3d");
                if (point.Epe3d.HasValue)
                {
                    writer.WriteNumberValue(point.Epe3d.Value);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    public static bool PointsEqual(Track? left, Track? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(Compute(left), Compute(right), StringComparison.Ordinal);
    }
}
