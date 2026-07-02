using System;
using System.Text;
using Sufni.Telemetry;

using Sufni.App.Acquisition.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

// Centralizes raw-source payload creation so imported SSTs and live captures
// use stable schema versions and hash inputs before they are persisted or synced.
public static class RecordedSessionSourceFactory
{
    private const int SchemaVersion = 1;

    public static RecordedSessionSource CreateImportedSst(Guid sessionId, TelemetryFileSource source)
    {
        var payload = RecordedSessionSourcePayloadCodec.CompressImportedSst(source.SstBytes);

        return new RecordedSessionSource
        {
            SessionId = sessionId,
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = source.FileName,
            SchemaVersion = SchemaVersion,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                source.FileName,
                SchemaVersion,
                payload),
            Payload = payload
        };
    }

    public static RecordedSessionSource CreateLiveCapture(Guid sessionId, LiveTelemetryCapture capture)
    {
        var payload = new RecordedLiveCaptureSourcePayload
        {
            SchemaVersion = SchemaVersion,
            Metadata = capture.Metadata,
            FrontSegments = capture.FrontSegments,
            RearSegments = capture.RearSegments,
            ImuData = capture.ImuData,
            GpsData = capture.GpsData,
            Markers = capture.Markers,
            StreamGaps = capture.StreamGaps,
            FinalStatus = capture.FinalStatus,
            MissingFinalStatus = capture.MissingFinalStatus,
        };
        var payloadBytes = Encoding.UTF8.GetBytes(AppJson.Serialize(payload));

        return new RecordedSessionSource
        {
            SessionId = sessionId,
            SourceKind = RecordedSessionSourceKind.LiveCapture,
            SourceName = capture.Metadata.SourceName,
            SchemaVersion = SchemaVersion,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.LiveCapture,
                capture.Metadata.SourceName,
                SchemaVersion,
                payloadBytes),
            Payload = payloadBytes
        };
    }
}
