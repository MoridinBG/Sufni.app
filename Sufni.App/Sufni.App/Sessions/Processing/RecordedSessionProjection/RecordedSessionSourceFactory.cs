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

    public static RecordedSessionSource CreateImportedSst(TelemetryFileSource source)
    {
        var payload = RecordedSessionSourcePayloadCodec.CompressImportedSst(source.SstBytes);
        var sourceHash = RecordedSessionSourceHash.ComputeImportedSst(source.SstBytes.Span);

        return new RecordedSessionSource
        {
            SessionId = RecordedSessionSourceHash.CreateSessionId(sourceHash),
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = source.FileName,
            SchemaVersion = SchemaVersion,
            SourceHash = sourceHash,
            Payload = payload
        };
    }

    public static RecordedSessionSource CreateLiveCapture(LiveTelemetryCapture capture)
    {
        var payload = new RecordedLiveCaptureSourcePayload
        {
            SchemaVersion = SchemaVersion,
            Metadata = capture.Metadata,
            FrontSegments = capture.FrontSegments,
            RearSegments = capture.RearSegments,
            ImuData = capture.ImuData,
            GpsData = capture.GpsData,
            TemperatureData = capture.TemperatureData,
            Markers = capture.Markers,
            StreamGaps = capture.StreamGaps,
            FinalStatus = capture.FinalStatus,
            MissingFinalStatus = capture.MissingFinalStatus,
        };
        var payloadBytes = Encoding.UTF8.GetBytes(AppJson.Serialize(payload));
        var sourceHash = RecordedSessionSourceHash.Compute(RecordedSessionSourceKind.LiveCapture, payloadBytes);

        return new RecordedSessionSource
        {
            SessionId = RecordedSessionSourceHash.CreateSessionId(sourceHash),
            SourceKind = RecordedSessionSourceKind.LiveCapture,
            SourceName = capture.Metadata.SourceName,
            SchemaVersion = SchemaVersion,
            SourceHash = sourceHash,
            Payload = payloadBytes
        };
    }
}
