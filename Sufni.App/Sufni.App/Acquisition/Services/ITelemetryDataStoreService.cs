using Sufni.App.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

using Sufni.App.Acquisition.Models;
namespace Sufni.App.Acquisition.Services;

// Aggregates every current telemetry source and owns the browse lifecycle that
// keeps mass-storage, network, and user-selected stores visible to the UI.
public interface ITelemetryDataStoreService
{
    public ObservableCollection<ITelemetryDataStore> DataStores { get; }
    public event EventHandler<DataStoreDiscoveryIssue>? DiscoveryIssueReported;
    public void StartBrowse();
    public void StopBrowse();

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

public abstract record DataStoreDiscoveryIssue
{
    private DataStoreDiscoveryIssue() { }
    public sealed record DaqUnavailable : DataStoreDiscoveryIssue;
    public sealed record Failed(DataStoreDiscoveryFailureReason Reason, string? Details = null) : DataStoreDiscoveryIssue;
}
