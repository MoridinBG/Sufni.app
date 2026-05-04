using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Stores;
using Sufni.Telemetry;

namespace Sufni.App.SessionGraph;

public sealed class RecordedSessionReprocessor(IProcessingFingerprintService fingerprintService)
    : IRecordedSessionReprocessor
{
    public Task<RecordedSessionReprocessResult> ReprocessAsync(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (domain.Setup is null || domain.Bike is null || domain.Source is null)
        {
            throw new InvalidOperationException("Recorded session cannot be reprocessed without setup, bike, and source metadata.");
        }

        if (source.SessionId != domain.Session.Id)
        {
            throw new InvalidOperationException("Recorded source does not match the domain session.");
        }

        var bike = Bike.FromSnapshot(domain.Bike);
        var setup = SetupFromSnapshot(domain.Setup);
        var bikeData = TelemetryBikeData.Create(setup, bike);

        var telemetryData = source.SourceKind switch
        {
            RecordedSessionSourceKind.ImportedSst => ReprocessImportedSst(source, bikeData),
            RecordedSessionSourceKind.LiveCapture => ReprocessLiveCapture(source, bikeData),
            _ => throw new ArgumentOutOfRangeException(nameof(source.SourceKind), source.SourceKind, "Unknown recorded source kind.")
        };

        var fullTrack = telemetryData.GpsData is { Length: > 0 }
            ? Track.FromGpsRecords(telemetryData.GpsData)
            : null;
        var fingerprint = fingerprintService.CreateCurrent(domain.Session, domain.Setup, domain.Bike, domain.Source);

        return Task.FromResult(new RecordedSessionReprocessResult(telemetryData, fullTrack, fingerprint));
    }

    private static TelemetryData ReprocessImportedSst(RecordedSessionSource source, BikeData bikeData)
    {
        var rawTelemetryData = RawTelemetryData.FromByteArray(source.Payload);
        var metadata = MetadataFromRaw(source.SourceName, rawTelemetryData);
        return TelemetryData.FromRecording(rawTelemetryData, metadata, bikeData);
    }

    private static TelemetryData ReprocessLiveCapture(RecordedSessionSource source, BikeData bikeData)
    {
        var json = Encoding.UTF8.GetString(source.Payload);
        var payload = AppJson.Deserialize<RecordedLiveCaptureSourcePayload>(json)
                      ?? throw new JsonException("Recorded live-capture source payload is invalid.");
        var capture = new LiveTelemetryCapture(
            payload.Metadata,
            bikeData,
            payload.FrontMeasurements,
            payload.RearMeasurements,
            payload.ImuData,
            payload.GpsData,
            payload.Markers);

        return TelemetryData.FromLiveCapture(capture);
    }

    private static Metadata MetadataFromRaw(string sourceName, RawTelemetryData rawTelemetryData) => new()
    {
        SourceName = sourceName,
        Version = rawTelemetryData.Version,
        SampleRate = rawTelemetryData.SampleRate,
        Timestamp = rawTelemetryData.Timestamp,
        Duration = rawTelemetryData.SampleRate > 0
            ? (double)Math.Max(rawTelemetryData.Front.Length, rawTelemetryData.Rear.Length) / rawTelemetryData.SampleRate
            : 0.0
    };

    private static Setup SetupFromSnapshot(SetupSnapshot snapshot) => new(snapshot.Id, snapshot.Name)
    {
        BikeId = snapshot.BikeId,
        FrontSensorConfigurationJson = snapshot.FrontSensorConfigurationJson,
        RearSensorConfigurationJson = snapshot.RearSensorConfigurationJson,
        Updated = snapshot.Updated,
    };
}

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

                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray())).ToLowerInvariant();
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
