using System.Collections.ObjectModel;
using System.Threading;
using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.ViewModels;

public class ImportSessionsViewModelTests
{
    private readonly ITelemetryDataStoreService telemetryDataStoreService = Substitute.For<ITelemetryDataStoreService>();
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly ISetupCoordinator setupCoordinator = Substitute.For<ISetupCoordinator>();
    private readonly IImportSessionsCoordinator importSessionsCoordinator = Substitute.For<IImportSessionsCoordinator>();
    private readonly ISetupStore setupStore = Substitute.For<ISetupStore>();

    private readonly ObservableCollection<ITelemetryDataStore> dataStores = [];
    private readonly SourceCache<SetupSnapshot, Guid> setupCache = new(s => s.Id);

    public ImportSessionsViewModelTests()
    {
        telemetryDataStoreService.DataStores.Returns(dataStores);
        telemetryDataStoreService.LoadFilesAsync(Arg.Any<ITelemetryDataStore>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(Array.Empty<ITelemetryFile>()));

        importSessionsCoordinator.ImportAsync(
                Arg.Any<IReadOnlyList<ITelemetryFile>>(),
                Arg.Any<Guid>(),
                Arg.Any<IProgress<SessionImportEvent>?>())
            .Returns(Task.FromResult(new SessionImportResult(
                Array.Empty<SessionSnapshot>(),
                Array.Empty<(string FileName, string ErrorMessage)>())));

        setupStore.Connect().Returns(setupCache.Connect());
        setupStore.FindByBoardId(Arg.Any<Guid>())
            .Returns(callInfo => setupCache.Items.FirstOrDefault(s => s.BoardId == callInfo.Arg<Guid>()));
    }

    private ImportSessionsViewModel CreateViewModel() => new(
        telemetryDataStoreService,
        filesService,
        shell,
        dialogService,
        setupCoordinator,
        importSessionsCoordinator,
        setupStore);

    [Fact]
    public void SelectingNull_ClearsFilesAndSetup_WithoutClearingNewDataStoresAvailable()
    {
        using var _ = new TestSynchronizationContextScope();
        var viewModel = CreateViewModel();
        var dataStore = CreateDataStore();

        viewModel.SelectedDataStore = dataStore;
        viewModel.TelemetryFiles.Add(CreateTelemetryFile("one"));
        viewModel.SelectedSetup = Guid.NewGuid();
        viewModel.NewDataStoresAvailable = true;

        viewModel.SelectedDataStore = null;

        Assert.Empty(viewModel.TelemetryFiles);
        Assert.Null(viewModel.SelectedSetup);
        Assert.True(viewModel.NewDataStoresAvailable);
    }

    [Fact]
    public void SelectingDataStore_ClearsNotification_ResolvesSetup_AndLoadsFiles()
    {
        using var _ = new TestSynchronizationContextScope();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(boardId: boardId);
        setupCache.AddOrUpdate(setup);

        var dataStore = CreateDataStore(boardId: boardId);
        var files = new[] { CreateTelemetryFile("one"), CreateTelemetryFile("two") };
        telemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(files));

        var viewModel = CreateViewModel();
        viewModel.NewDataStoresAvailable = true;

        viewModel.SelectedDataStore = dataStore;

        Assert.False(viewModel.NewDataStoresAvailable);
        Assert.Equal(setup.Id, viewModel.SelectedSetup);
        Assert.Equal(files, viewModel.TelemetryFiles);
    }

    [Fact]
    public void PendingSelectionLoad_KeepsExistingFilesVisible_UntilReplacementCompletes()
    {
        using var _ = new TestSynchronizationContextScope();
        var firstStore = CreateDataStore(name: "first");
        var secondStore = CreateDataStore(name: "second");
        var oldFiles = new[] { CreateTelemetryFile("old") };
        var newFiles = new[] { CreateTelemetryFile("new") };
        var pendingLoad = new TaskCompletionSource<IReadOnlyList<ITelemetryFile>>();

        telemetryDataStoreService.LoadFilesAsync(firstStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(oldFiles));
        telemetryDataStoreService.LoadFilesAsync(secondStore, Arg.Any<CancellationToken>())
            .Returns(pendingLoad.Task);

        var viewModel = CreateViewModel();
        viewModel.SelectedDataStore = firstStore;

        Assert.Equal(oldFiles, viewModel.TelemetryFiles);

        viewModel.SelectedDataStore = secondStore;

        Assert.Equal(oldFiles, viewModel.TelemetryFiles);

        pendingLoad.SetResult(newFiles);

        Assert.Equal(newFiles, viewModel.TelemetryFiles);
    }

    [Fact]
    public void StaleSelectionLoad_IsIgnored()
    {
        using var _ = new TestSynchronizationContextScope();
        var firstStore = CreateDataStore(name: "first");
        var secondStore = CreateDataStore(name: "second");
        var firstLoad = new TaskCompletionSource<IReadOnlyList<ITelemetryFile>>();
        var secondLoad = new TaskCompletionSource<IReadOnlyList<ITelemetryFile>>();

        telemetryDataStoreService.LoadFilesAsync(firstStore, Arg.Any<CancellationToken>())
            .Returns(firstLoad.Task);
        telemetryDataStoreService.LoadFilesAsync(secondStore, Arg.Any<CancellationToken>())
            .Returns(secondLoad.Task);

        var secondFiles = new[] { CreateTelemetryFile("second") };
        var firstFiles = new[] { CreateTelemetryFile("first") };

        var viewModel = CreateViewModel();
        viewModel.SelectedDataStore = firstStore;
        viewModel.SelectedDataStore = secondStore;

        secondLoad.SetResult(secondFiles);
        firstLoad.SetResult(firstFiles);

        Assert.Equal(secondFiles, viewModel.TelemetryFiles);
    }

    [Fact]
    public async Task StalePostImportRefresh_IsIgnored_AfterLaterSelectionChange()
    {
        using var _ = new TestSynchronizationContextScope();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(boardId: boardId);
        setupCache.AddOrUpdate(setup);

        var firstStore = CreateDataStore(name: "first", boardId: boardId);
        var secondStore = CreateDataStore(name: "second");
        var initialFiles = new[] { CreateTelemetryFile("initial") };
        var refreshLoad = new TaskCompletionSource<IReadOnlyList<ITelemetryFile>>();
        var secondFiles = new[] { CreateTelemetryFile("second") };

        telemetryDataStoreService.LoadFilesAsync(firstStore, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<ITelemetryFile>>(initialFiles),
                refreshLoad.Task);
        telemetryDataStoreService.LoadFilesAsync(secondStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(secondFiles));

        var viewModel = CreateViewModel();
        viewModel.SelectedDataStore = firstStore;

        var importTask = viewModel.ImportSessionsCommand.ExecuteAsync(null);
        Assert.True(viewModel.ImportSessionsCommand.IsRunning);

        viewModel.SelectedDataStore = secondStore;
        refreshLoad.SetResult(new[] { CreateTelemetryFile("refresh") });

        await importTask;

        Assert.False(viewModel.ImportSessionsCommand.IsRunning);
        Assert.Equal(secondFiles, viewModel.TelemetryFiles);
    }

    [Fact]
    public void DataStoreCollectionChanges_UpdateSelectionAndAvailability()
    {
        using var _ = new TestSynchronizationContextScope();
        var firstStore = CreateDataStore(name: "first");
        var secondStore = CreateDataStore(name: "second");
        var viewModel = CreateViewModel();

        dataStores.Add(firstStore);

        Assert.Same(firstStore, viewModel.SelectedDataStore);
        Assert.False(viewModel.NewDataStoresAvailable);

        dataStores.Add(secondStore);

        Assert.Same(firstStore, viewModel.SelectedDataStore);
        Assert.True(viewModel.NewDataStoresAvailable);

        dataStores.Remove(firstStore);

        Assert.Same(secondStore, viewModel.SelectedDataStore);
    }

    [Fact]
    public async Task OpeningDuplicateDataStore_ReportsNotification_AndSelectsExistingStore()
    {
        using var _ = new TestSynchronizationContextScope();
        var folder = Substitute.For<Avalonia.Platform.Storage.IStorageFolder>();
        var existingStore = CreateDataStore(name: "existing");
        filesService.OpenDataStoreFolderAsync().Returns(Task.FromResult<Avalonia.Platform.Storage.IStorageFolder?>(folder));
        telemetryDataStoreService.TryAddStorageProviderAsync(folder, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StorageProviderRegistrationResult>(
                new StorageProviderRegistrationResult.AlreadyOpen(existingStore)));

        var viewModel = CreateViewModel();
        await viewModel.OpenDataStoreCommand.ExecuteAsync(null);

        Assert.NotEmpty(viewModel.Notifications);
        Assert.Same(existingStore, viewModel.SelectedDataStore);
    }

    [Fact]
    public async Task OpeningDataStore_SelectsReturnedStore()
    {
        using var _ = new TestSynchronizationContextScope();
        var folder = Substitute.For<Avalonia.Platform.Storage.IStorageFolder>();
        var addedStore = CreateDataStore(name: "added");
        filesService.OpenDataStoreFolderAsync().Returns(Task.FromResult<Avalonia.Platform.Storage.IStorageFolder?>(folder));
        telemetryDataStoreService.TryAddStorageProviderAsync(folder, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StorageProviderRegistrationResult>(
                new StorageProviderRegistrationResult.Added(addedStore)));

        var viewModel = CreateViewModel();
        await viewModel.OpenDataStoreCommand.ExecuteAsync(null);

        Assert.Same(addedStore, viewModel.SelectedDataStore);
    }

    [Fact]
    public async Task ImportProgress_UpdatesNotificationsAndErrors()
    {
        using var _ = new TestSynchronizationContextScope();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(boardId: boardId);
        setupCache.AddOrUpdate(setup);

        var dataStore = CreateDataStore(boardId: boardId);
        var file = CreateTelemetryFile("lap");
        telemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { file }));

        importSessionsCoordinator.ImportAsync(
                Arg.Any<IReadOnlyList<ITelemetryFile>>(),
                setup.Id,
                Arg.Any<IProgress<SessionImportEvent>?>())
            .Returns(callInfo =>
            {
                var progress = callInfo.ArgAt<IProgress<SessionImportEvent>?>(2);
                progress?.Report(new SessionImportEvent.Imported(TestSnapshots.Session(name: "lap")));
                progress?.Report(new SessionImportEvent.Failed("broken.SST", "boom"));
                return Task.FromResult(new SessionImportResult(
                    Array.Empty<SessionSnapshot>(),
                    new[] { ("broken.SST", "boom") }));
            });

        var viewModel = CreateViewModel();
        viewModel.SelectedDataStore = dataStore;

        await viewModel.ImportSessionsCommand.ExecuteAsync(null);

        Assert.NotEmpty(viewModel.Notifications);
        Assert.Single(viewModel.ErrorMessages);
    }

    [Fact]
    public async Task ImportSessions_AddsFinalSummaryNotification_FromCoordinatorResult()
    {
        using var _ = new TestSynchronizationContextScope();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(boardId: boardId);
        setupCache.AddOrUpdate(setup);

        var dataStore = CreateDataStore(boardId: boardId);
        var file = CreateTelemetryFile("lap");
        telemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { file }));

        importSessionsCoordinator.ImportAsync(
                Arg.Any<IReadOnlyList<ITelemetryFile>>(),
                setup.Id,
                Arg.Any<IProgress<SessionImportEvent>?>())
            .Returns(Task.FromResult(new SessionImportResult(
                new[] { TestSnapshots.Session() },
                new[] { ("broken.SST", "boom") })));

        var viewModel = CreateViewModel();
        viewModel.SelectedDataStore = dataStore;

        await viewModel.ImportSessionsCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Notifications);
        Assert.Empty(viewModel.ErrorMessages);
    }

    [Fact]
    public void SelectingMalformedFileSource_DoesNotNotifyBeforeUserOpensReason()
    {
        using var _ = new TestSynchronizationContextScope();
        var dataStore = CreateDataStore();
        var malformedFile = CreateTelemetryFile(
            name: "bad",
            shouldBeImported: false,
            malformedMessage: "telemetry payload is invalid");

        telemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { malformedFile }));

        var viewModel = CreateViewModel();

        viewModel.SelectedDataStore = dataStore;

        Assert.Empty(viewModel.Notifications);

        viewModel.SelectedDataStore = null;
        viewModel.SelectedDataStore = dataStore;

        Assert.Empty(viewModel.Notifications);
        Assert.Single(viewModel.TelemetryFiles);
    }

    [Fact]
    public void ShowMalformedMessage_AddsSingleNotification()
    {
        using var _ = new TestSynchronizationContextScope();
        var file = CreateTelemetryFile(
            name: "trimmed",
            malformedMessage: "trailing chunk was trimmed",
            canImport: true);
        var viewModel = CreateViewModel();

        viewModel.ShowMalformedMessageCommand.Execute(file);
        viewModel.ShowMalformedMessageCommand.Execute(file);

        Assert.Single(viewModel.Notifications);
    }

    [Fact]
    public void Loaded_StartsBrowse_AndSubscribesToSetupStoreChanges()
    {
        using var _ = new TestSynchronizationContextScope();
        var boardId = Guid.NewGuid();
        var dataStore = CreateDataStore(boardId: boardId);
        var viewModel = CreateViewModel();
        viewModel.SelectedDataStore = dataStore;

        viewModel.LoadedCommand.Execute(null);
        setupCache.AddOrUpdate(TestSnapshots.Setup(boardId: boardId));

        telemetryDataStoreService.Received(1).StartBrowse();
        Assert.NotNull(viewModel.SelectedSetup);
    }

    [Fact]
    public void Unloaded_ClearsState_StopsBrowse_AndDisposesSubscriptions()
    {
        using var _ = new TestSynchronizationContextScope();
        var boardId = Guid.NewGuid();
        var setup = TestSnapshots.Setup(boardId: boardId);
        setupCache.AddOrUpdate(setup);

        var dataStore = CreateDataStore(boardId: boardId);
        var file = CreateTelemetryFile("one");
        telemetryDataStoreService.LoadFilesAsync(dataStore, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITelemetryFile>>(new[] { file }));

        var viewModel = CreateViewModel();
        viewModel.SelectedDataStore = dataStore;
        viewModel.NewDataStoresAvailable = true;
        viewModel.LoadedCommand.Execute(null);

        viewModel.UnloadedCommand.Execute(null);
        setupCache.AddOrUpdate(TestSnapshots.Setup(boardId: boardId));

        telemetryDataStoreService.Received(1).StopBrowse();
        Assert.Empty(viewModel.TelemetryFiles);
        Assert.Null(viewModel.SelectedDataStore);
        Assert.Null(viewModel.SelectedSetup);
        Assert.False(viewModel.NewDataStoresAvailable);
    }
}