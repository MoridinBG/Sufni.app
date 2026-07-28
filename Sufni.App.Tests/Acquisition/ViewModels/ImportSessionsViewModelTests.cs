using Avalonia.Platform.Storage;
using DynamicData;
using NSubstitute;
using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Sessions.Store;
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
        using var _ = new TestSynchronizationContextScope();
        var harness = new ImportWorkflowHarness();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(boardId: boardId);
        harness.SetupCache.AddOrUpdate(setup);
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
        using var _ = new TestSynchronizationContextScope();
        var harness = new ImportWorkflowHarness();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(boardId: boardId);
        harness.SetupCache.AddOrUpdate(setup);
        var dataStore = CreateDataStore(boardId: boardId);
        var initialFile = CreateTelemetryFile("lap");
        var refreshedFile = CreateTelemetryFile("after-import");
        harness.TelemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { initialFile }),
                Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { refreshedFile }));
        var failure = new SessionImportFailure("broken.SST", "boom", SessionImportFailureOperation.Import);
        var capturedProgressText = new List<string>();
        var viewModel = harness.CreateViewModel();
        harness.ImportSessionsCoordinatorSubstitute.ImportAsync(
                Arg.Any<IReadOnlyList<ITelemetryFile>>(),
                setup.Id,
                Arg.Any<IProgress<SessionImportEvent>?>())
            .Returns(callInfo =>
            {
                var progress = callInfo.ArgAt<IProgress<SessionImportEvent>?>(2);
                progress?.Report(new SessionImportEvent.Progress(1, 2));
                capturedProgressText.Add(viewModel.ImportProgressText);
                progress?.Report(new SessionImportEvent.Imported(TestSnapshots.Session(name: "lap")));
                progress?.Report(new SessionImportEvent.ImportFailed(failure.FileName, failure.ErrorMessage));
                return Task.FromResult(new SessionImportResult(
                    [TestSnapshots.Session(name: "lap")],
                    [failure]));
            });

        viewModel.SelectedDataStore = dataStore;
        await viewModel.ImportSessionsCommand.ExecuteAsync(null);

        await harness.ImportSessionsCoordinatorSubstitute.Received(1).ImportAsync(
            Arg.Is<IReadOnlyList<ITelemetryFile>>(files => files.Count == 1 && ReferenceEquals(files[0], initialFile)),
            setup.Id,
            Arg.Any<IProgress<SessionImportEvent>?>());
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
        using var _ = new TestSynchronizationContextScope();
        var harness = new ImportWorkflowHarness();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(boardId: boardId);
        harness.SetupCache.AddOrUpdate(setup);
        var dataStore = CreateDataStore(boardId: boardId);
        var file = CreateTelemetryFile("lap");
        harness.TelemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<ITelemetryFile>>([file]),
                Task.FromResult<IReadOnlyList<ITelemetryFile>>([]));
        var imported = TestSnapshots.Session(name: "lap");
        var failure = new SessionImportFailure(
            "lap",
            "publish failed",
            SessionImportFailureOperation.Publish);
        harness.ImportSessionsCoordinatorSubstitute.ImportAsync(
                Arg.Any<IReadOnlyList<ITelemetryFile>>(),
                setup.Id,
                Arg.Any<IProgress<SessionImportEvent>?>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<IProgress<SessionImportEvent>?>(2)?.Report(
                    new SessionImportEvent.PublicationFailed(failure.FileName, failure.ErrorMessage));
                return Task.FromResult(new SessionImportResult([imported], [failure]));
            });
        var viewModel = harness.CreateViewModel();

        viewModel.SelectedDataStore = dataStore;
        await viewModel.ImportSessionsCommand.ExecuteAsync(null);

        Assert.Contains(
            viewModel.ErrorMessages,
            message => message.Contains("left unacknowledged", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.Notifications,
            message => message.Contains("1 committed, 1 unpublished, 0 failed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(OpenStoreResult.Added)]
    [InlineData(OpenStoreResult.AlreadyOpen)]
    public async Task OpenDataStoreCommand_SelectsReturnedStore(OpenStoreResult result)
    {
        using var _ = new TestSynchronizationContextScope();
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
        using var _ = new TestSynchronizationContextScope();
        var harness = new ImportWorkflowHarness();
        var boardId = Guid.NewGuid();
        var dataStore = CreateDataStore(boardId: boardId);
        var viewModel = harness.CreateViewModel();
        viewModel.SelectedDataStore = dataStore;
        viewModel.NewDataStoresAvailable = true;

        viewModel.LoadedCommand.Execute(null);
        harness.SetupCache.AddOrUpdate(TestSnapshots.Setup(boardId: boardId));

        harness.TelemetryDataStoreService.Received(1).StartBrowse();
        Assert.NotNull(viewModel.SelectedSetup);

        viewModel.UnloadedCommand.Execute(null);
        harness.SetupCache.AddOrUpdate(TestSnapshots.Setup(boardId: boardId));

        harness.TelemetryDataStoreService.Received(1).StopBrowse();
        Assert.Empty(viewModel.TelemetryFiles);
        Assert.Null(viewModel.SelectedDataStore);
        Assert.Null(viewModel.SelectedSetup);
        Assert.False(viewModel.NewDataStoresAvailable);
    }

    [Fact]
    public void ShowMalformedMessage_AddsSingleNotification()
    {
        using var _ = new TestSynchronizationContextScope();
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

    public enum OpenStoreResult
    {
        Added,
        AlreadyOpen
    }
}
