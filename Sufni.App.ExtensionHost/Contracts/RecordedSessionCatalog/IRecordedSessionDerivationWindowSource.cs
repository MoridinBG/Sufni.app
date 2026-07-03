using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

/// <summary>
/// Durable extension-owned source of recorded-session derivation windows.
/// Implementations must answer from persistent state rather than UI read stores
/// because the app queries this surface during background processing and startup
/// hydration.
/// </summary>
public interface IRecordedSessionDerivationWindowSource
{
    string ExtensionId { get; }

    event EventHandler? WindowsChanged;

    Task<RecordedSessionDerivationWindow?> GetWindowAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>> GetWindowsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when another live recorded session derives from the given
    /// source session's recording source. Self-windows should not retain their
    /// own source through deletion.
    /// </summary>
    Task<bool> IsRecordingSourceReferencedAsync(
        Guid sourceSessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetReferencedSourceSessionIdsAsync(
        CancellationToken cancellationToken = default);
}
