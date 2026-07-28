namespace Sufni.App.ExtensionHost.Contracts.Sync;

public interface IExtensionSyncPreparedBatch
{
}

public abstract record ExtensionSyncPrepareResult
{
    private ExtensionSyncPrepareResult()
    {
    }

    public sealed record Prepared(IExtensionSyncPreparedBatch Batch) : ExtensionSyncPrepareResult;

    public sealed record Failed(string ErrorMessage) : ExtensionSyncPrepareResult;
}
