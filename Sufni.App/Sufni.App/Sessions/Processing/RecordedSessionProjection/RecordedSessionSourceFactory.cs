using Sufni.Telemetry;

using Sufni.App.Acquisition.Models;
using Sufni.App.Sessions.Models;
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
        var payload = RecordedLiveCaptureSourcePayload.FromCapture(capture);
        var payloadBytes = TelemetryCodec.EncodeLiveSource(payload);
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
