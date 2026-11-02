using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Sufni.App.Acquisition.Services;
using System.Threading.Tasks;
using System.Threading;
using Sufni.Telemetry;

using Sufni.App.Acquisition.Models;
namespace Sufni.App.Acquisition.Coordinators;

public interface IImportSessionsCoordinator
{
    SessionImportState State { get; }
    ReadOnlyObservableCollection<ITelemetryDataStore> Sources { get; }
    bool CanPickFolder { get; }
    IDisposable ObserveSources(Action<DataStoreDiscoveryIssue> reportIssue);
    Task<TelemetryFileListing> LoadFilesAsync(ITelemetryDataStore source, CancellationToken cancellationToken = default);
    Task<StorageProviderRegistrationResult> OpenFolderAsync(CancellationToken cancellationToken = default);

    IObservable<SessionImportState> Observe();

    Task ImportAsync(
        ITelemetryDataStore dataStore,
        IReadOnlyList<SessionImportItem> files,
        Guid setupId,
        CancellationToken cancellationToken = default);

    Task StopAsync();
}
