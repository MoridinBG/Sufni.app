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
        var firstLoad = new TaskCompletionSource<IReadOnlyList<ITelemetryFile>>();
        harness.TelemetryDataStoreService.LoadFilesAsync(firstStore, Arg.Any<CancellationToken>())
            .Returns(firstLoad.Task);
        harness.TelemetryDataStoreService.LoadFilesAsync(secondStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(secondFiles));

        var viewModel = harness.CreateViewModel();
        viewModel.NewDataStoresAvailable = true;
        viewModel.SelectedDataStore = firstStore;

        Assert.Equal(setup.Id, viewModel.SelectedSetup);

        viewModel.SelectedDataStore = secondStore;
        firstLoad.SetResult(firstFiles);
        await Task.Yield();

        Assert.False(viewModel.NewDataStoresAvailable);
        Assert.Null(viewModel.SelectedSetup);
        Assert.Equal(secondFiles, viewModel.TelemetryFiles);
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
        harness.TelemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { initialFile }),
                Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { refreshedFile }));
        var failure = new SessionImportFailure.OperationFailed("broken.SST", SessionImportFailureOperation.Import, SessionImportFailureReason.Import, "boom");
        var capturedProgressText = new List<string>();
        var viewModel = harness.CreateViewModel();
        harness.ImportSessionsCoordinatorSubstitute.ImportAsync(
                Arg.Any<IReadOnlyList<ITelemetryFile>>(),
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
            Arg.Is<IReadOnlyList<ITelemetryFile>>(files => files.Count == 1 && ReferenceEquals(files[0], initialFile)),
            setup.Id);
        Assert.Equal(["File 1/2"], capturedProgressText);
        Assert.Equal(0, viewModel.CurrentFileIndex);
        Assert.Equal(0, viewModel.TotalFiles);
        Assert.Equal(string.Empty, viewModel.ImportProgressText);
        Assert.Equal([refreshedFile], viewModel.TelemetryFiles);
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
        harness.TelemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<ITelemetryFile>>([file]),
                Task.FromResult<IReadOnlyList<ITelemetryFile>>([]));
        var imported = TestSnapshots.Session(name: "lap");
        var failure = new SessionImportFailure.OperationFailed(
            "lap", SessionImportFailureOperation.Publish, SessionImportFailureReason.Publish, "publish failed");
        harness.ImportSessionsCoordinatorSubstitute.ImportAsync(
                Arg.Any<IReadOnlyList<ITelemetryFile>>(),
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
        var folder = Substitute.For<IStorageFolder>();
        var dataStore = CreateDataStore(name: "store");
        harness.FilesService.OpenDataStoreFolderAsync()
            .Returns(Task.FromResult<IStorageFolder?>(folder));
        harness.TelemetryDataStoreService.TryAddStorageProviderAsync(folder, Arg.Any<CancellationToken>())
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
        var boardId = Guid.NewGuid();
        var dataStore = CreateDataStore(boardId: boardId);
        var viewModel = harness.CreateViewModel();
        viewModel.SelectedDataStore = dataStore;
        viewModel.NewDataStoresAvailable = true;

        viewModel.LoadedCommand.Execute(null);
        harness.SetupQuery.Add(TestSnapshots.Setup(), boardId);

        harness.TelemetryDataStoreService.Received(1).StartBrowse();
        Assert.NotNull(viewModel.SelectedSetup);

        viewModel.UnloadedCommand.Execute(null);
        harness.SetupQuery.Add(TestSnapshots.Setup(), boardId);

        harness.TelemetryDataStoreService.Received(1).StopBrowse();
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

        viewModel.ShowMalformedMessageCommand.Execute(file);
        viewModel.ShowMalformedMessageCommand.Execute(file);

        Assert.Single(viewModel.Notifications);
    }

    [Fact]
    public void DiscoveryIssue_SeparatesUnavailableNotificationsFromFailureDiagnostics_WhileLoaded()
    {
        var harness = new ImportWorkflowHarness();
        var vm = harness.CreateViewModel();
        vm.LoadedCommand.Execute(null);
        harness.TelemetryDataStoreService.DiscoveryIssueReported += Raise.Event<EventHandler<DataStoreDiscoveryIssue>>(
            harness.TelemetryDataStoreService, new DataStoreDiscoveryIssue.DaqUnavailable());
        Assert.Single(vm.Notifications);
        Assert.Empty(vm.ErrorMessages);

        const string details = "drive diagnostic";
        harness.TelemetryDataStoreService.DiscoveryIssueReported += Raise.Event<EventHandler<DataStoreDiscoveryIssue>>(
            harness.TelemetryDataStoreService, new DataStoreDiscoveryIssue.Failed(DataStoreDiscoveryFailureReason.Scan, details));
        var error = Assert.Single(vm.ErrorMessages);
        Assert.Equal(details, error.Details);
        Assert.DoesNotContain(details, error.Summary);

        vm.UnloadedCommand.Execute(null);
        harness.TelemetryDataStoreService.DiscoveryIssueReported += Raise.Event<EventHandler<DataStoreDiscoveryIssue>>(
            harness.TelemetryDataStoreService, new DataStoreDiscoveryIssue.DaqUnavailable());
        Assert.Single(vm.Notifications);
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
