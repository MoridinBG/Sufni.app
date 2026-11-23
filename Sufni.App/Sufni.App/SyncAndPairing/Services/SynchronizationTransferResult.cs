namespace Sufni.App.SyncAndPairing.Services;

public enum SynchronizationTransferStatus { Completed, Pending }

public closed record SynchronizationTransferResult<T> where T : class
{
    private SynchronizationTransferResult() { }

    public sealed record Pending : SynchronizationTransferResult<T>;
    public sealed record Ignored : SynchronizationTransferResult<T>;
    public sealed record Received(T Payload) : SynchronizationTransferResult<T>;
}
