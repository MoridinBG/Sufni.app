using Sufni.App.Shared.Models;

namespace Sufni.App.SyncAndPairing.Services;

public abstract record ServerStartResult
{
    private ServerStartResult() { }
    public sealed record Started : ServerStartResult;
    public sealed record Failed(ServerStartFailureReason Reason, string? Details = null) : ServerStartResult;
}
