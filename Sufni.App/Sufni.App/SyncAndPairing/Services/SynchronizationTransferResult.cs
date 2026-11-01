namespace Sufni.App.SyncAndPairing.Services;

public enum SynchronizationTransferStatus { Completed, Pending }

/// <summary>A completed download may carry no payload when the peer has ignored the item.</summary>
public sealed record SynchronizationTransferResult<T>(T? Payload, SynchronizationTransferStatus Status)
    where T : class;
