using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

namespace Sufni.App.ExtensionHost.TestSupport.Doubles;

/// <summary>
/// Settable <see cref="IRecordedSessionDerivationWindowProvider"/> for extension
/// tests that need durable-window behavior without extension database plumbing.
/// </summary>
public sealed class StubRecordedSessionDerivationWindowProvider : IRecordedSessionDerivationWindowProvider
{
    public event EventHandler? WindowsChanged;

    public Dictionary<Guid, RecordedSessionDerivationWindow> Windows { get; } = [];

    public Task<RecordedSessionDerivationWindow?> GetWindowAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GetWindow(sessionId));

    public Task<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>> GetWindowsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>>(
            new Dictionary<Guid, RecordedSessionDerivationWindow>(Windows));

    public Task<bool> IsRecordingSourceReferencedAsync(
        Guid sourceSessionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Windows.Any(pair =>
            pair.Key != sourceSessionId &&
            pair.Value.SourceSessionId == sourceSessionId));

    public Task<IReadOnlyCollection<Guid>> GetReferencedSourceSessionIdsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Guid>>(
            Windows
                .Where(pair => pair.Key != pair.Value.SourceSessionId)
                .Select(pair => pair.Value.SourceSessionId)
                .Distinct()
                .ToArray());

    public void RaiseWindowsChanged() => WindowsChanged?.Invoke(this, EventArgs.Empty);

    private RecordedSessionDerivationWindow? GetWindow(Guid sessionId) =>
        Windows.TryGetValue(sessionId, out var window) ? window : null;
}
