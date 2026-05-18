using System;
using System.Collections.Generic;
using Sufni.App.Models;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Sufni.App.Services;

// Aggregates every current telemetry source and owns the browse lifecycle that
// keeps mass-storage, network, and user-selected stores visible to the UI.
public interface ITelemetryDataStoreService
{
    public ObservableCollection<ITelemetryDataStore> DataStores { get; }
    public event EventHandler<string>? ErrorOccurred;
    public void StartBrowse();
    public void StopBrowse();

    // One-shot mass-storage probe for setup flows. It reads mounted drives
    // without mutating DataStores, browse state, or DAQ-side directories.
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
