using System;
using System.Collections.Generic;
using Sufni.App.Models;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Sufni.App.Services;

public interface ITelemetryDataStoreService
{
    public ObservableCollection<ITelemetryDataStore> DataStores { get; }
    public event EventHandler<string>? ErrorOccurred;
    public void StartBrowse();
    public void StopBrowse();

    /// <summary>
    /// One-shot probe for a mass-storage DAQ at this instant. Iterates
    /// the OS drives, finds the first one with a <c>BOARDID</c> file at
    /// the root, and returns its derived board id. Returns <c>null</c>
    /// if no DAQ drive is currently mounted. Pure read — does not touch
    /// the <see cref="DataStores"/> collection or the browse state, and
    /// does not create any side-effect directories on the drive.
    /// </summary>
    public Task<Guid?> DetectConnectedBoardIdAsync(CancellationToken cancellationToken = default);

    public Task<IReadOnlyList<ITelemetryFile>> LoadFilesAsync(
        ITelemetryDataStore dataStore,
        CancellationToken cancellationToken = default);

    public Task<StorageProviderRegistrationResult> TryAddStorageProviderAsync(
        IStorageFolder folder,
        CancellationToken cancellationToken = default);
}

public abstract record StorageProviderRegistrationResult
{
    private StorageProviderRegistrationResult() { }

    public sealed record Added(ITelemetryDataStore DataStore) : StorageProviderRegistrationResult;

    public sealed record AlreadyOpen(ITelemetryDataStore DataStore) : StorageProviderRegistrationResult;
}
