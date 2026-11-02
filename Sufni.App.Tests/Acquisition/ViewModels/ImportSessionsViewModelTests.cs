using System.Reactive.Subjects;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Acquisition.ViewModels;
using Avalonia.Platform.Storage;
using DynamicData;
using NSubstitute;
using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Acquisition;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Fixtures;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetrySources;

namespace Sufni.App.Tests.Acquisition.ViewModels;

public class ImportSessionsViewModelTests
{
    [Fact]
    public void ImportAvailability_FollowsSelectedPortHolderAndStopsObservingOnUnload()
    {
        var harness = new ImportWorkflowHarness();
        var source = CreateDataStore();
        using var transport = new BehaviorSubject<DaqTransportHolder?>(null);
        source.ObserveTransport().Returns(transport);
        var vm = harness.CreateViewModel();
        vm.SelectedDataStore = source;
        vm.SelectedSetup = Guid.NewGuid();
        Assert.True(vm.ImportSessionsCommand.CanExecute(null));

        transport.OnNext(new DaqTransportHolder.Streaming());
        Assert.True(vm.IsSourceBusy);
        Assert.False(vm.ImportSessionsCommand.CanExecute(null));
        transport.OnNext(null);
        Assert.False(vm.IsSourceBusy);
        Assert.True(vm.ImportSessionsCommand.CanExecute(null));
        vm.UnloadedCommand.Execute(null);
        transport.OnNext(new DaqTransportHolder.Import(Guid.NewGuid()));

        Assert.False(vm.IsSourceBusy);
        Assert.False(vm.ImportSessionsCommand.CanExecute(null));
    }

    [Fact]
    public void RefreshForSameSource_CancelsEarlierListingAndRejectsItsLateRows()
    {
        var harness = new ImportWorkflowHarness();
        var source = CreateDataStore();
        var oldFile = CreateTelemetryFile("old");
        var currentFile = CreateTelemetryFile("current");
        var first = new TaskCompletionSource<TelemetryFileListing>();
        CancellationToken firstToken = default;
        var calls = 0;
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(source, Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (++calls != 1) return Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([currentFile], null));
            firstToken = call.Arg<CancellationToken>();
            return first.Task;
        });
        var vm = harness.CreateViewModel();
        vm.SelectedDataStore = source;

        harness.ImportState.OnNext(new SessionImportState.Completed(new SessionImportResult([], []), []));
        Assert.True(firstToken.IsCancellationRequested);
        first.SetResult(new TelemetryFileListing.Listed([oldFile], null));

        Assert.Same(currentFile, Assert.Single(vm.TelemetryFiles).File);
        Assert.False(vm.IsLoadingFiles);
    }

    [Fact]
    public async Task ClearedSources_EndSelectionAndCancelPendingListing()
    {
        var harness = new ImportWorkflowHarness();
        var source = CreateDataStore();
        harness.DataStores.Add(source);
        var listing = new TaskCompletionSource<TelemetryFileListing>();
        CancellationToken token = default;
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(source, Arg.Any<CancellationToken>()).Returns(call =>
        {
            token = call.Arg<CancellationToken>();
            return listing.Task;
        });
        var vm = harness.CreateViewModel();

        harness.DataStores.Clear();
        Assert.True(token.IsCancellationRequested);
        Assert.Null(vm.SelectedDataStore);
        Assert.False(vm.IsLoadingFiles);
        listing.SetResult(new TelemetryFileListing.Listed([CreateTelemetryFile()], null));
        await vm.PrepareCloseAsync();

        Assert.Empty(vm.TelemetryFiles);
    }

    [Fact]
    public async Task Close_CancelsAndDrainsListingWithoutPublishingItsLateResult()
    {
        var harness = new ImportWorkflowHarness();
        var source = CreateDataStore();
        var listing = new TaskCompletionSource<TelemetryFileListing>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(source, Arg.Any<CancellationToken>()).Returns(call =>
        {
            token = call.Arg<CancellationToken>();
            return listing.Task;
        });
        var vm = harness.CreateViewModel();
        vm.SelectedDataStore = source;

        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = token.Register(() => canceled.TrySetResult());
        var close = vm.PrepareCloseAsync();
        await canceled.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(close.IsCompleted);
        listing.SetResult(new TelemetryFileListing.Listed([CreateTelemetryFile()], null));
        await close.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(vm.TelemetryFiles);
        Assert.Empty(vm.ErrorMessages);
    }

    [Fact]
    public void FolderCommand_IsDisabledWhenFolderPickingIsUnavailable()
    {
        var harness = new ImportWorkflowHarness();
        harness.ImportSessionsCoordinatorSubstitute.CanPickFolder.Returns(false);
        var vm = harness.CreateViewModel();

        Assert.False(vm.OpenDataStoreCommand.CanExecute(null));
    }

    [Fact]
    public async Task FolderCommand_DisplaysTypedFailureDetails()
    {
        var harness = new ImportWorkflowHarness();
        var vm = harness.CreateViewModel();
        harness.ImportSessionsCoordinatorSubstitute.OpenFolderAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StorageProviderRegistrationResult>(new StorageProviderRegistrationResult.Failed(ImportSourceFailureReason.OpenFolder, "folder diagnostic")));

        await vm.OpenDataStoreCommand.ExecuteAsync(null);

        var failure = Assert.Single(vm.ErrorMessages);
        Assert.Equal("folder diagnostic", failure.Details);
        Assert.DoesNotContain("folder diagnostic", failure.Summary);
    }

    [Fact]
    public async Task ImportCommand_CapturesRowEditsBeforeBackgroundWorkStarts()
    {
        var harness = new ImportWorkflowHarness();
        var source = CreateDataStore();
        var file = CreateTelemetryFile("recording");
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(source, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([file], null)));
        var vm = harness.CreateViewModel();
        vm.SelectedDataStore = source;
        vm.SelectedSetup = Guid.NewGuid();
        var row = Assert.Single(vm.TelemetryFiles);
        row.Name = "Morning ride";
        row.Description = "Dry trail";
        row.Action = ImportFileAction.Import;
        SessionImportItem? captured = null;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.ImportSessionsCoordinatorSubstitute.ImportAsync(Arg.Any<ITelemetryDataStore>(), Arg.Any<IReadOnlyList<SessionImportItem>>(), vm.SelectedSetup.Value,
            Arg.Any<CancellationToken>()).Returns(call =>
        {
            captured = Assert.Single(call.Arg<IReadOnlyList<SessionImportItem>>());
            return release.Task;
        });

        var run = vm.ImportSessionsCommand.ExecuteAsync(null);
        row.Name = "Edited later";
        row.Description = "Changed";
        row.Action = ImportFileAction.Trash;
        release.SetResult();
        await run.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(captured);
        Assert.Same(file, captured.File);
        Assert.Equal("Morning ride", captured.Name);
        Assert.Equal("Dry trail", captured.Description);
        Assert.Equal(ImportFileAction.Import, captured.Action);
    }

    [Fact]
    public async Task SelectingDataStore_ResolvesSetupLoadsFilesAndIgnoresStaleLoads()
    {
        var harness = new ImportWorkflowHarness();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup();
        harness.SetupQuery.Add(setup, boardId);
        var firstStore = CreateDataStore(name: "first", boardId: boardId);
        var secondStore = CreateDataStore(name: "second");
        var firstFiles = new[] { CreateTelemetryFile("first") };
        var secondFiles = new[] { CreateTelemetryFile("second") };
        var firstLoad = new TaskCompletionSource<TelemetryFileListing>();
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(firstStore, Arg.Any<CancellationToken>())
            .Returns(firstLoad.Task);
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(secondStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([.. secondFiles], null)));

        var viewModel = harness.CreateViewModel();
        viewModel.NewDataStoresAvailable = true;
        viewModel.SelectedDataStore = firstStore;

        Assert.Equal(setup.Id, viewModel.SelectedSetup);

        viewModel.SelectedDataStore = secondStore;
        firstLoad.SetResult(new TelemetryFileListing.Listed([.. firstFiles], null));
        await Task.Yield();

        Assert.False(viewModel.NewDataStoresAvailable);
        Assert.Null(viewModel.SelectedSetup);
        Assert.Equal(secondFiles, viewModel.TelemetryFiles.Select(row => row.File));
    }

    [Fact]
    public async Task ImportSessionsCommand_ReportsProgressSummarizesResultAndRefreshesCurrentStore()
    {
        var harness = new ImportWorkflowHarness();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup();
        harness.SetupQuery.Add(setup, boardId);
        var dataStore = CreateDataStore(boardId: boardId);
        var initialFile = CreateTelemetryFile("lap");
        var refreshedFile = CreateTelemetryFile("after-import");
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([.. new[] { initialFile }], null)),
                Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([.. new[] { refreshedFile }], null)));
        var failure = new SessionImportFailure.OperationFailed("broken.SST", SessionImportFailureOperation.Import, SessionImportFailureReason.Import, "boom");
        var capturedProgressText = new List<string>();
        var viewModel = harness.CreateViewModel();
        harness.ImportSessionsCoordinatorSubstitute.ImportAsync(
                Arg.Any<ITelemetryDataStore>(), Arg.Any<IReadOnlyList<SessionImportItem>>(),
                setup.Id)
            .Returns(callInfo =>
            {
                harness.ImportState.OnNext(new SessionImportState.Running(1, 2, []));
                capturedProgressText.Add(viewModel.ImportProgressText);
                harness.ImportState.OnNext(new SessionImportState.Completed(
                    new SessionImportResult([TestSnapshots.Session(name: "lap")], [failure]),
                    [new SessionImportEvent.Imported(TestSnapshots.Session(name: "lap")),
                     new SessionImportEvent.Failed(failure)]));
                return Task.CompletedTask;
            });

        viewModel.SelectedDataStore = dataStore;
        await viewModel.ImportSessionsCommand.ExecuteAsync(null);

        await harness.ImportSessionsCoordinatorSubstitute.Received(1).ImportAsync(
            dataStore, Arg.Is<IReadOnlyList<SessionImportItem>>(files => files.Count == 1 && ReferenceEquals(files[0].File, initialFile)),
            setup.Id);
        Assert.Equal(["File 1/2"], capturedProgressText);
        Assert.Equal(0, viewModel.CurrentFileIndex);
        Assert.Equal(0, viewModel.TotalFiles);
        Assert.Equal(string.Empty, viewModel.ImportProgressText);
        Assert.Equal([refreshedFile], viewModel.TelemetryFiles.Select(row => row.File));
        Assert.Contains(viewModel.Notifications, message => message.Contains("lap", StringComparison.Ordinal));
        Assert.Contains(viewModel.Notifications, message => message.Contains("Import finished", StringComparison.Ordinal));
        Assert.Single(viewModel.ErrorMessages);
    }

    [Fact]
    public async Task ImportSessionsCommand_ReportsCommittedButUnpublishedFilesAccurately()
    {
        var harness = new ImportWorkflowHarness();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup();
        harness.SetupQuery.Add(setup, boardId);
        var dataStore = CreateDataStore(boardId: boardId);
        var file = CreateTelemetryFile("lap");
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([file], null)),
                Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([], null)));
        var imported = TestSnapshots.Session(name: "lap");
        var failure = new SessionImportFailure.OperationFailed(
            "lap", SessionImportFailureOperation.Publish, SessionImportFailureReason.Publish, "publish failed");
        harness.ImportSessionsCoordinatorSubstitute.ImportAsync(
                Arg.Any<ITelemetryDataStore>(), Arg.Any<IReadOnlyList<SessionImportItem>>(),
                setup.Id)
            .Returns(callInfo =>
            {
                harness.ImportState.OnNext(new SessionImportState.Completed(
                    new SessionImportResult([imported], [failure]),
                    [new SessionImportEvent.Failed(failure)]));
                return Task.CompletedTask;
            });
        var viewModel = harness.CreateViewModel();

        viewModel.SelectedDataStore = dataStore;
        await viewModel.ImportSessionsCommand.ExecuteAsync(null);

        Assert.NotEmpty(viewModel.ErrorMessages);
        Assert.Contains(
            viewModel.Notifications,
            message => message.Contains("1 committed, 1 unpublished, 0 failed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(OpenStoreResult.Added)]
    [InlineData(OpenStoreResult.AlreadyOpen)]
    public async Task OpenDataStoreCommand_SelectsReturnedStore(OpenStoreResult result)
    {
        var harness = new ImportWorkflowHarness();
        var dataStore = CreateDataStore(name: "store");
        harness.ImportSessionsCoordinatorSubstitute.OpenFolderAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StorageProviderRegistrationResult>(
                result is OpenStoreResult.Added
                    ? new StorageProviderRegistrationResult.Added(dataStore)
                    : new StorageProviderRegistrationResult.AlreadyOpen(dataStore)));

        var viewModel = harness.CreateViewModel();
        await viewModel.OpenDataStoreCommand.ExecuteAsync(null);

        Assert.Same(dataStore, viewModel.SelectedDataStore);
        if (result is OpenStoreResult.AlreadyOpen)
        {
            Assert.NotEmpty(viewModel.Notifications);
        }
        else
        {
            Assert.Empty(viewModel.Notifications);
        }
    }

    [Fact]
    public void LoadedAndUnloaded_StartBrowseTrackSetupChangesAndClearState()
    {
        var harness = new ImportWorkflowHarness();
        var browseLease = Substitute.For<IDisposable>();
        harness.ImportSessionsCoordinatorSubstitute.ObserveSources(Arg.Any<Action<DataStoreDiscoveryIssue>>()).Returns(browseLease);
        var boardId = Guid.NewGuid();
        var dataStore = CreateDataStore(boardId: boardId);
        var viewModel = harness.CreateViewModel();
        viewModel.SelectedDataStore = dataStore;
        viewModel.NewDataStoresAvailable = true;

        viewModel.LoadedCommand.Execute(null);
        harness.SetupQuery.Add(TestSnapshots.Setup(), boardId);

        harness.ImportSessionsCoordinatorSubstitute.Received(1).ObserveSources(Arg.Any<Action<DataStoreDiscoveryIssue>>());
        Assert.NotNull(viewModel.SelectedSetup);

        viewModel.UnloadedCommand.Execute(null);
        harness.SetupQuery.Add(TestSnapshots.Setup(), boardId);

        browseLease.Received(1).Dispose();
        Assert.Empty(viewModel.TelemetryFiles);
        Assert.Null(viewModel.SelectedDataStore);
        Assert.Null(viewModel.SelectedSetup);
        Assert.False(viewModel.NewDataStoresAvailable);
    }

    [Fact]
    public async Task SetupChanges_UpdateSelectionOnUiThread_AndStopAfterUnload()
    {
        var dispatcher = new ManualUiThreadDispatcher();
        var harness = new ImportWorkflowHarness();
        var boardId = Guid.NewGuid();
        var viewModel = harness.CreateViewModel(dispatcher);
        viewModel.SelectedDataStore = CreateDataStore(boardId: boardId);
        viewModel.LoadedCommand.Execute(null);
        await dispatcher.DrainAsync();
        var setup = TestSnapshots.Setup();

        harness.SetupQuery.Add(setup, boardId);
        Assert.Null(viewModel.SelectedSetup);
        await dispatcher.DrainAsync();
        Assert.Equal(setup.Id, viewModel.SelectedSetup);

        harness.SetupQuery.Remove(setup.Id);
        viewModel.UnloadedCommand.Execute(null);
        viewModel.SelectedSetup = setup.Id;
        await dispatcher.DrainAsync();

        Assert.Equal(setup.Id, viewModel.SelectedSetup);
    }

    [Fact]
    public void ShowMalformedMessage_AddsSingleNotification()
    {
        var harness = new ImportWorkflowHarness();
        var file = CreateTelemetryFile(
            name: "trimmed",
            malformedMessage: "trailing chunk was trimmed",
            canImport: true);
        var viewModel = harness.CreateViewModel();

        viewModel.ShowMalformedMessageCommand.Execute(new ImportFileRowViewModel(file));
        viewModel.ShowMalformedMessageCommand.Execute(new ImportFileRowViewModel(file));

        Assert.Single(viewModel.Notifications);
    }

    [Fact]
    public void DriveScanFailure_ShowsSafeDiagnosticsOnlyWhileLoaded()
    {
        var harness = new ImportWorkflowHarness();
        var vm = harness.CreateViewModel();
        vm.LoadedCommand.Execute(null);
        const string details = "drive diagnostic";
        harness.SourceIssues.OnNext(new DataStoreDiscoveryIssue(DataStoreDiscoveryFailureReason.Scan, details));
        var error = Assert.Single(vm.ErrorMessages);
        Assert.Equal(details, error.Details);
        Assert.DoesNotContain(details, error.Summary);

        vm.UnloadedCommand.Execute(null);
        harness.SourceIssues.OnNext(new DataStoreDiscoveryIssue(DataStoreDiscoveryFailureReason.Scan, details));
        Assert.Single(vm.ErrorMessages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ListingUnavailability_IsNotificationRatherThanImportFailure(bool busy)
    {
        var harness = new ImportWorkflowHarness();
        var source = CreateDataStore();
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(source, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryFileListing>(busy
                ? new TelemetryFileListing.Busy(new DaqTransportHolder.Streaming())
                : new TelemetryFileListing.Unavailable()));
        var vm = harness.CreateViewModel();

        vm.SelectedDataStore = source;

        Assert.Single(vm.Notifications);
        Assert.Empty(vm.ErrorMessages);
        Assert.Empty(vm.TelemetryFiles);
        Assert.False(vm.IsLoadingFiles);
    }

    [Fact]
    public void BusyImportStart_DoesNotReportACompletedOrFailedImport()
    {
        var harness = new ImportWorkflowHarness();
        var vm = harness.CreateViewModel();

        harness.ImportState.OnNext(new SessionImportState.SourceBusy(new DaqTransportHolder.Streaming()));

        Assert.Single(vm.Notifications);
        Assert.Empty(vm.ErrorMessages);
        Assert.False(vm.IsImporting);
    }

    [Fact]
    public async Task LegacyListingIdentity_SelectsKnownSetupAndSuggestsItForNewSetup()
    {
        var harness = new ImportWorkflowHarness();
        var source = CreateDataStore();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup();
        harness.SetupQuery.Add(setup, boardId);
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(source, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([], boardId)));
        var vm = harness.CreateViewModel();

        vm.SelectedDataStore = source;
        await vm.AddSetupCommand.ExecuteAsync(null);

        Assert.Equal(setup.Id, vm.SelectedSetup);
        harness.Shell.Received(1).OpenEditor(new EditorKey.NewSetup(boardId));
    }

    [Fact]
    public void ReplacedSource_SelectsItsNewLifetimeAndReplacesFileRows()
    {
        var harness = new ImportWorkflowHarness();
        var original = CreateDataStore("old");
        var replacement = CreateDataStore("new");
        var file = CreateTelemetryFile("current");
        harness.DataStores.Add(original);
        harness.ImportSessionsCoordinatorSubstitute.LoadFilesAsync(replacement, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TelemetryFileListing>(new TelemetryFileListing.Listed([file], null)));
        var vm = harness.CreateViewModel();

        harness.DataStores[0] = replacement;

        Assert.Same(replacement, vm.SelectedDataStore);
        Assert.Same(file, Assert.Single(vm.TelemetryFiles).File);
    }

    [Fact]
    public void AcknowledgementFailure_ReportsSourceCleanupWithSeparateDiagnostics()
    {
        var harness = new ImportWorkflowHarness();
        var vm = harness.CreateViewModel();
        const string details = "acknowledgement diagnostic";

        harness.ImportState.OnNext(new SessionImportState.Completed(new SessionImportResult([], []),
            [new SessionImportEvent.AcknowledgementFailed("ride.SST", details)]));

        var error = Assert.Single(vm.ErrorMessages);
        Assert.Contains("ride.SST", error.Summary);
        Assert.DoesNotContain(details, error.Summary);
        Assert.Equal(details, error.Details);
    }

    [Fact]
    public void ProcessingFailure_ReportsFileWithSeparateDiagnostics()
    {
        var harness = new ImportWorkflowHarness();
        var vm = harness.CreateViewModel();
        const string details = "calibration diagnostic";
        var failure = new SessionImportFailure.ProcessingFailed("ride.SST", SessionProcessingFailureReason.InvalidCalibration, details);

        harness.ImportState.OnNext(new SessionImportState.Completed(new SessionImportResult([], [failure]),
            [new SessionImportEvent.Failed(failure)]));

        var error = Assert.Single(vm.ErrorMessages);
        Assert.Contains("ride.SST", error.Summary);
        Assert.DoesNotContain(details, error.Summary);
        Assert.Equal(details, error.Details);
    }

    [Fact]
    public void NewTab_ObservesActiveImport_WithoutStartingAnotherCommand()
    {
        var harness = new ImportWorkflowHarness();
        harness.ImportState.OnNext(new SessionImportState.Running(2, 3, []));

        var reopened = harness.CreateViewModel();
        reopened.SelectedDataStore = CreateDataStore();
        reopened.SelectedSetup = Guid.NewGuid();

        Assert.True(reopened.IsImporting);
        Assert.False(reopened.ImportSessionsCommand.IsRunning);
        Assert.False(reopened.ImportSessionsCommand.CanExecute(null));
        Assert.Equal(2, reopened.CurrentFileIndex);
        Assert.Equal(3, reopened.TotalFiles);

        harness.ImportState.OnNext(new SessionImportState.Completed(new SessionImportResult([], []), []));

        Assert.False(reopened.IsImporting);
        Assert.True(reopened.ImportSessionsCommand.CanExecute(null));
        Assert.Single(reopened.Notifications);
    }

    public enum OpenStoreResult
    {
        Added,
        AlreadyOpen
    }
}
