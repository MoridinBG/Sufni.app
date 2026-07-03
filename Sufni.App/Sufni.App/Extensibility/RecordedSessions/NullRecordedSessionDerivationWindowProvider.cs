using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

namespace Sufni.App.Extensibility.RecordedSessions;

internal sealed class NullRecordedSessionDerivationWindowProvider : IRecordedSessionDerivationWindowProvider
{
    public event EventHandler? WindowsChanged
    {
        add { }
        remove { }
    }

    public Task<RecordedSessionDerivationWindow?> GetWindowAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<RecordedSessionDerivationWindow?>(null);

    public Task<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>> GetWindowsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>>(
            new Dictionary<Guid, RecordedSessionDerivationWindow>());

    public Task<bool> IsRecordingSourceReferencedAsync(
        Guid sourceSessionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyCollection<Guid>> GetReferencedSourceSessionIdsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Guid>>([]);
}
