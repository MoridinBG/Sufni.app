using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Rebuilds processed telemetry from a persisted recorded raw source.
/// It decodes the source payload, reconstructs the bike processing context,
/// generates any GPS-backed full track, and returns the matching fingerprint.
/// </summary>
internal sealed class RecordedSessionReprocessor(
    IProcessingFingerprintService fingerprintService,
    ITelemetryBikeProcessingContextFactory bikeProcessingContextFactory)
    : IRecordedSessionReprocessor
{
    public Task<RecordedSessionReprocessResult> ProcessImportedSstAsync(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        ReadOnlyMemory<byte> sstBytes,
        CancellationToken cancellationToken = default)
    {
        var bikeData = ValidateAndCreateBikeData(
            domain,
            source,
            TelemetryProcessingOptions.Default,
            cancellationToken);
        if (source.SourceKind != RecordedSessionSourceKind.ImportedSst)
        {
            throw new ArgumentException("Initial imported SST processing requires an imported SST source.", nameof(source));
        }

        var telemetryData = ProcessImportedSst(
            source.SourceName,
            sstBytes,
            bikeData,
            TelemetryProcessingOptions.Default,
            domain.DerivationWindow);
        return Task.FromResult(CreateResult(domain, telemetryData, TelemetryProcessingOptions.Default));
    }

    public Task<RecordedSessionReprocessResult> ReprocessAsync(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        CancellationToken cancellationToken = default)
    {
        return ReprocessAsync(domain, source, TelemetryProcessingOptions.Default, cancellationToken);
    }

    public Task<RecordedSessionReprocessResult> ReprocessAsync(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        TelemetryProcessingOptions processingOptions,
        CancellationToken cancellationToken = default)
    {
        var bikeData = ValidateAndCreateBikeData(domain, source, processingOptions, cancellationToken);

        var telemetryData = source.SourceKind switch
        {
            RecordedSessionSourceKind.ImportedSst => ReprocessImportedSst(source, bikeData, processingOptions, domain.DerivationWindow),
            RecordedSessionSourceKind.LiveCapture => ReprocessLiveCapture(source, bikeData, processingOptions, domain.DerivationWindow),
            _ => throw new ArgumentOutOfRangeException(nameof(source.SourceKind), source.SourceKind, "Unknown recorded source kind.")
        };

        return Task.FromResult(CreateResult(domain, telemetryData, processingOptions));
    }

    private BikeData ValidateAndCreateBikeData(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        TelemetryProcessingOptions processingOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processingOptions);
        cancellationToken.ThrowIfCancellationRequested();

        if (domain.Setup is null || domain.Bike is null || domain.Source is null)
        {
            throw new InvalidOperationException("Recorded session cannot be reprocessed without setup, bike, and source metadata.");
        }

        var expectedSourceSessionId = RecordedSessionDerivationResolver.GetEffectiveSourceSessionId(
            domain.Session.Id,
            domain.DerivationWindow);
        if (source.SessionId != expectedSourceSessionId || domain.Source.SessionId != source.SessionId)
        {
            throw new InvalidOperationException("Recorded source does not match the domain session.");
        }

        return bikeProcessingContextFactory.Create(domain.Setup, domain.Bike).BikeData;
    }

    private RecordedSessionReprocessResult CreateResult(
        RecordedSessionDomainSnapshot domain,
        TelemetryData telemetryData,
        TelemetryProcessingOptions processingOptions)
    {
        var setup = domain.Setup!;
        var bike = domain.Bike!;
        var source = domain.Source!;
        var fullTrack = telemetryData.GpsData is { Length: > 0 }
            ? Track.FromGpsRecords(telemetryData.GpsData)
            : null;
        var fingerprint = CanReuseCurrentFingerprint(domain.CurrentFingerprint, processingOptions, domain.DerivationWindow)
            ? domain.CurrentFingerprint!
            : domain.DependencyHash is { } dependencyHash
                ? fingerprintService.CreateCurrent(
                    domain.Session,
                    setup,
                    bike,
                    source,
                    dependencyHash,
                    processingOptions,
                    domain.DerivationWindow)
                : fingerprintService.CreateCurrent(
                    domain.Session,
                    setup,
                    bike,
                    source,
                    processingOptions,
                    domain.DerivationWindow);
        var fingerprintJson = AppJson.Serialize(fingerprint);
        var processedTelemetry = new ProcessedTelemetryPayload(
            telemetryData,
            telemetryData.BinaryForm,
            fingerprintJson);

        return new RecordedSessionReprocessResult(processedTelemetry, fullTrack, fingerprint);
    }

    private static bool CanReuseCurrentFingerprint(
        ProcessingFingerprint? fingerprint,
        TelemetryProcessingOptions processingOptions,
        RecordedSessionDerivationWindow? window) =>
        fingerprint?.VelocityFilterWindowMilliseconds ==
        processingOptions.ClampedVelocityFilterWindowMilliseconds &&
        fingerprint.DerivationWindow == window;

    private static TelemetryData ReprocessImportedSst(
        RecordedSessionSource source,
        BikeData bikeData,
        TelemetryProcessingOptions processingOptions,
        RecordedSessionDerivationWindow? window)
    {
        var sstBytes = RecordedSessionSourcePayloadCodec.DecompressImportedSst(source.Payload);
        return ProcessImportedSst(source.SourceName, sstBytes, bikeData, processingOptions, window);
    }

    private static TelemetryData ProcessImportedSst(
        string sourceName,
        ReadOnlyMemory<byte> sstBytes,
        BikeData bikeData,
        TelemetryProcessingOptions processingOptions,
        RecordedSessionDerivationWindow? window)
    {
        var rawTelemetryData = RawTelemetryData.FromMemory(sstBytes);
        if (window is not null)
        {
            rawTelemetryData = rawTelemetryData.Slice(window.StartSeconds, window.EndSeconds);
        }

        var metadata = MetadataFromRaw(sourceName, rawTelemetryData);
        return TelemetryData.FromRecording(rawTelemetryData, metadata, bikeData, processingOptions);
    }

    private static TelemetryData ReprocessLiveCapture(
        RecordedSessionSource source,
        BikeData bikeData,
        TelemetryProcessingOptions processingOptions,
        RecordedSessionDerivationWindow? window)
    {
        var payload = JsonSerializer.Deserialize(source.Payload, AppJson.Context.RecordedLiveCaptureSourcePayload)
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
            TemperatureData = payload.TemperatureData,
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
