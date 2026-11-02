using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Shared.Models;

namespace Sufni.App.Acquisition.Coordinators;

public partial class ImportSessionsCoordinator
{
    private readonly System.Threading.Lock sourceGate = new();
    private readonly HashSet<EventHandler<DataStoreDiscoveryIssue>> sourceObservers = [];
    private readonly HashSet<Task> sourceOperations = [];

    public ReadOnlyObservableCollection<ITelemetryDataStore> Sources { get; } = new(dataStores.DataStores);
    public bool CanPickFolder => appEnvironment.Capabilities.CanPickFolder;

    public IDisposable ObserveSources(Action<DataStoreDiscoveryIssue> reportIssue)
    {
        EventHandler<DataStoreDiscoveryIssue> handler = (_, issue) => reportIssue(issue);
        lock (sourceGate)
        {
            shutdown.Token.ThrowIfCancellationRequested();
            sourceObservers.Add(handler);
            dataStores.DiscoveryIssueReported += handler;
            if (sourceObservers.Count == 1)
            {
                try { dataStores.StartBrowse(); }
                catch (Exception exception)
                {
                    sourceObservers.Remove(handler);
                    dataStores.DiscoveryIssueReported -= handler;
                    StopBrowse(reportIssue);
                    // DaqBrowseOwner logs startup failures; this boundary supplies their user outcome.
                    reportIssue(new DataStoreDiscoveryIssue(DataStoreDiscoveryFailureReason.Browse, exception.ToString()));
                    return Disposable.Empty;
                }
            }
        }
        return Disposable.Create(() =>
        {
            lock (sourceGate)
            {
                if (!sourceObservers.Remove(handler)) return;
                dataStores.DiscoveryIssueReported -= handler;
                if (sourceObservers.Count == 0) StopBrowse(reportIssue);
            }
        });
    }

    public Task<TelemetryFileListing> LoadFilesAsync(ITelemetryDataStore source, CancellationToken cancellationToken = default) =>
        RunSourceOperationAsync<TelemetryFileListing>(async token =>
        {
            try { return await dataStores.LoadFilesAsync(source, token); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                logger.Warning(exception, "Could not list telemetry files from {Source}", source.Name);
                return new TelemetryFileListing.Failed(ImportSourceFailureReason.ListFiles, exception.ToString());
            }
        }, cancellationToken);

    public Task<StorageProviderRegistrationResult> OpenFolderAsync(CancellationToken cancellationToken = default) =>
        RunSourceOperationAsync<StorageProviderRegistrationResult>(async token =>
        {
            if (!CanPickFolder) return new StorageProviderRegistrationResult.Unavailable();
            try
            {
                var folder = await filesService.OpenDataStoreFolderAsync();
                shutdown.Token.ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested();
                return folder is null ? new StorageProviderRegistrationResult.Canceled()
                    : await dataStores.TryAddStorageProviderAsync(folder, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                logger.Warning(exception, "Could not open telemetry source folder");
                return new StorageProviderRegistrationResult.Failed(ImportSourceFailureReason.OpenFolder, exception.ToString());
            }
        }, cancellationToken);

    private Task<T> RunSourceOperationAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource linked;
        lock (sourceGate)
        {
            shutdown.Token.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            linked = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, cancellationToken);
            sourceOperations.Add(completion.Task);
        }
        _ = CompleteAsync();
        return completion.Task;

        async Task CompleteAsync()
        {
            T? result = default;
            Exception? failure = null;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                result = await action(linked.Token);
            }
            catch (Exception exception) { failure = exception; }
            linked.Dispose();
            lock (sourceGate)
            {
                if (failure is OperationCanceledException canceled) completion.TrySetCanceled(canceled.CancellationToken);
                else if (failure is not null) completion.TrySetException(failure);
                else completion.TrySetResult(result!);
                sourceOperations.Remove(completion.Task);
            }
        }
    }

    private async Task DrainSourceOperationsAsync()
    {
        Task[] pending;
        lock (sourceGate) pending = [.. sourceOperations];
        try { await Task.WhenAll(pending); }
        catch (OperationCanceledException) { }
    }

    private void StopSourceObservations()
    {
        lock (sourceGate)
        {
            if (sourceObservers.Count == 0) return;
            var observers = sourceObservers.ToArray();
            foreach (var observer in observers) dataStores.DiscoveryIssueReported -= observer;
            sourceObservers.Clear();
            StopBrowse(issue =>
            {
                foreach (var observer in observers) observer(this, issue);
            });
        }
    }
    private void StopBrowse(Action<DataStoreDiscoveryIssue> reportIssue)
    {
        try { dataStores.StopBrowse(); }
        catch (Exception exception)
        {
            logger.Warning(exception, "Could not stop telemetry source browse");
            reportIssue(new DataStoreDiscoveryIssue(DataStoreDiscoveryFailureReason.Browse, exception.ToString()));
        }
    }

}
