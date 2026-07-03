using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Extensibility.Capabilities;
namespace Sufni.App.Extensibility.RecordedSessions;

public interface IRecordedSessionDerivationWindowService
{
    event EventHandler? WindowsChanged;

    Task<RecordedSessionDerivationWindow?> GetWindowAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>> GetWindowsAsync(
        CancellationToken cancellationToken = default);

    Task<bool> IsRecordingSourceReferencedAsync(
        Guid sourceSessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetReferencedSourceSessionIdsAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class RecordedSessionDerivationWindowService : IRecordedSessionDerivationWindowService, IDisposable
{
    private readonly IReadOnlyList<IRecordedSessionDerivationWindowSource> sources;

    public RecordedSessionDerivationWindowService(IEnumerable<IRecordedSessionDerivationWindowSource>? sources = null)
    {
        this.sources = sources?.ToArray() ?? [];
        ValidateSources(this.sources);
        foreach (var source in this.sources)
        {
            source.WindowsChanged += OnSourceWindowsChanged;
        }
    }

    public event EventHandler? WindowsChanged;

    public async Task<RecordedSessionDerivationWindow?> GetWindowAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        foreach (var source in sources)
        {
            var window = await source.GetWindowAsync(sessionId, cancellationToken);
            if (window is not null)
            {
                return window;
            }
        }

        return null;
    }

    public async Task<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>> GetWindowsAsync(
        CancellationToken cancellationToken = default)
    {
        var windows = new Dictionary<Guid, RecordedSessionDerivationWindow>();
        foreach (var source in sources)
        {
            var sourceWindows = await source.GetWindowsAsync(cancellationToken);
            foreach (var (sessionId, window) in sourceWindows)
            {
                if (!windows.TryAdd(sessionId, window))
                {
                    throw new InvalidOperationException(
                        $"More than one recorded-session derivation window source returned a window for session '{sessionId}'.");
                }
            }
        }

        return windows;
    }

    public async Task<bool> IsRecordingSourceReferencedAsync(
        Guid sourceSessionId,
        CancellationToken cancellationToken = default)
    {
        foreach (var source in sources)
        {
            if (await source.IsRecordingSourceReferencedAsync(sourceSessionId, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<IReadOnlyCollection<Guid>> GetReferencedSourceSessionIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var referenced = new HashSet<Guid>();
        foreach (var source in sources)
        {
            referenced.UnionWith(await source.GetReferencedSourceSessionIdsAsync(cancellationToken));
        }

        return referenced.ToArray();
    }

    public void Dispose()
    {
        foreach (var source in sources)
        {
            source.WindowsChanged -= OnSourceWindowsChanged;
        }
    }

    private static void ValidateSources(IReadOnlyList<IRecordedSessionDerivationWindowSource> sources)
    {
        var extensionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            ExtensionContributionValidator.ValidateRequiredId(
                source.ExtensionId,
                "Recorded-session derivation window source");
            if (!extensionIds.Add(source.ExtensionId))
            {
                throw new InvalidOperationException(
                    $"More than one recorded-session derivation window source is registered for extension '{source.ExtensionId}'.");
            }
        }
    }

    private void OnSourceWindowsChanged(object? sender, EventArgs args)
    {
        WindowsChanged?.Invoke(this, EventArgs.Empty);
    }
}
