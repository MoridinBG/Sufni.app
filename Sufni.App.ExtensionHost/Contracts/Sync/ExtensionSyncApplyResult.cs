using System.Collections.Generic;

namespace Sufni.App.ExtensionHost.Contracts.Sync;

public abstract record ExtensionSyncApplyResult
{
    private ExtensionSyncApplyResult(IReadOnlyList<string> progressMessages)
    {
        ProgressMessages = progressMessages;
    }

    public IReadOnlyList<string> ProgressMessages { get; }

    public sealed record Applied(IReadOnlyList<string> Messages) : ExtensionSyncApplyResult(Messages);

    public sealed record Skipped(IReadOnlyList<string> Messages) : ExtensionSyncApplyResult(Messages);

    public sealed record Failed(string ErrorMessage, IReadOnlyList<string> Messages) : ExtensionSyncApplyResult(Messages);
}

