namespace Sufni.App.ExtensionHost.Sync;

public sealed record ExtensionSyncEnvelope(
    string ExtensionId,
    int SchemaVersion,
    string ContentType,
    byte[] Payload);

