namespace Sufni.App.ExtensionHost.Contracts.Sync;

public sealed record ExtensionSyncEnvelope(
    string ExtensionId,
    int SchemaVersion,
    string ContentType,
    byte[] Payload);

