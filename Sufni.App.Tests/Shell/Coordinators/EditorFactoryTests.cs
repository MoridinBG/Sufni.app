using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;

namespace Sufni.App.Tests.Shell.Coordinators;

public class EditorFactoryTests
{
    [Fact]
    public void OpenNewBikeEditor_CreatesDirtyEditorAndOpensShell()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Bike();
        var factory = CreateFactory(shell);

        factory.OpenNewBikeEditor(snapshot);

        var editor = Assert.IsType<BikeEditorViewModel>(shell.OpenedView);
        Assert.Equal(snapshot.Id, editor.Id);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void OpenBikeEditor_UsesSnapshotIdForDeduplication()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Bike();
        var otherSnapshot = TestSnapshots.Bike();
        var factory = CreateFactory(shell);

        factory.OpenBikeEditor(snapshot);

        Assert.Equal(typeof(BikeEditorViewModel), shell.OpenOrFocusType);
        var match = Assert.IsType<Func<BikeEditorViewModel, bool>>(shell.OpenOrFocusMatch);
        var create = Assert.IsType<Func<BikeEditorViewModel>>(shell.OpenOrFocusCreate);

        Assert.True(match(factory.CreateBikeEditor(snapshot, isNew: false)));
        Assert.False(match(factory.CreateBikeEditor(otherSnapshot, isNew: false)));
        var created = create();
        Assert.Equal(snapshot.Id, created.Id);
        Assert.False(created.IsDirty);
    }

    [Fact]
    public async Task CloseBikeEditor_ClosesMatchingEditorAndForgetsRestoreHistory()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Bike();
        var factory = CreateFactory(shell);

        await factory.CloseBikeEditor(snapshot.Id);

        Assert.Equal(typeof(BikeEditorViewModel), shell.CloseIfOpenType);
        Assert.True(shell.CloseIfOpenForgetRestoreHistory);
        var match = Assert.IsType<Func<BikeEditorViewModel, bool>>(shell.CloseIfOpenMatch);
        Assert.True(match(factory.CreateBikeEditor(snapshot, isNew: false)));
        Assert.False(match(factory.CreateBikeEditor(TestSnapshots.Bike(), isNew: false)));
    }

    [Fact]
    public void OpenNewSetupEditor_CreatesDirtyEditorAndOpensShell()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Setup();
        var factory = CreateFactory(shell);

        factory.OpenNewSetupEditor(snapshot);

        var editor = Assert.IsType<SetupEditorViewModel>(shell.OpenedView);
        Assert.Equal(snapshot.Id, editor.Id);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void OpenSetupEditor_UsesSnapshotIdForDeduplication()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Setup();
        var otherSnapshot = TestSnapshots.Setup();
        var factory = CreateFactory(shell);

        factory.OpenSetupEditor(snapshot);

        Assert.Equal(typeof(SetupEditorViewModel), shell.OpenOrFocusType);
        var match = Assert.IsType<Func<SetupEditorViewModel, bool>>(shell.OpenOrFocusMatch);
        var create = Assert.IsType<Func<SetupEditorViewModel>>(shell.OpenOrFocusCreate);

        Assert.True(match(factory.CreateSetupEditor(snapshot, isNew: false)));
        Assert.False(match(factory.CreateSetupEditor(otherSnapshot, isNew: false)));
        var created = create();
        Assert.Equal(snapshot.Id, created.Id);
        Assert.False(created.IsDirty);
    }

    [Fact]
    public async Task CloseSetupEditor_ClosesMatchingEditorAndForgetsRestoreHistory()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Setup();
        var factory = CreateFactory(shell);

        await factory.CloseSetupEditor(snapshot.Id);

        Assert.Equal(typeof(SetupEditorViewModel), shell.CloseIfOpenType);
        Assert.True(shell.CloseIfOpenForgetRestoreHistory);
        var match = Assert.IsType<Func<SetupEditorViewModel, bool>>(shell.CloseIfOpenMatch);
        Assert.True(match(factory.CreateSetupEditor(snapshot, isNew: false)));
        Assert.False(match(factory.CreateSetupEditor(TestSnapshots.Setup(), isNew: false)));
    }

    [Fact]
    public void OpenImportSessions_OpensSingletonThroughShell()
    {
        var shell = new CapturingShellCoordinator();
        var factory = CreateFactory(shell);

        factory.OpenImportSessions();

        Assert.Equal(typeof(ImportSessionsViewModel), shell.OpenOrFocusType);
        var match = Assert.IsType<Func<ImportSessionsViewModel, bool>>(shell.OpenOrFocusMatch);
        Assert.True(match(null!));
    }

    [Fact]
    public void OpenSessionDetail_UsesSnapshotIdForDeduplication()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Session();
        var otherSnapshot = TestSnapshots.Session();
        var factory = CreateFactory(shell);

        factory.OpenSessionDetail(snapshot);

        Assert.Equal(typeof(SessionDetailViewModel), shell.OpenOrFocusType);
        var match = Assert.IsType<Func<SessionDetailViewModel, bool>>(shell.OpenOrFocusMatch);
        var create = Assert.IsType<Func<SessionDetailViewModel>>(shell.OpenOrFocusCreate);

        Assert.True(match(factory.CreateSessionDetail(snapshot)));
        Assert.False(match(factory.CreateSessionDetail(otherSnapshot)));
        var created = create();
        Assert.Equal(snapshot.Id, created.Id);
    }

    [Fact]
    public void OpenSessionDetailInBackground_UsesSnapshotIdForDeduplication()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Session();
        var otherSnapshot = TestSnapshots.Session();
        var factory = CreateFactory(shell);

        factory.OpenSessionDetailInBackground(snapshot);

        Assert.Equal(typeof(SessionDetailViewModel), shell.OpenInBackgroundType);
        var match = Assert.IsType<Func<SessionDetailViewModel, bool>>(shell.OpenInBackgroundMatch);
        var create = Assert.IsType<Func<SessionDetailViewModel>>(shell.OpenInBackgroundCreate);

        Assert.True(match(factory.CreateSessionDetail(snapshot)));
        Assert.False(match(factory.CreateSessionDetail(otherSnapshot)));
        var created = create();
        Assert.Equal(snapshot.Id, created.Id);
    }

    [Fact]
    public async Task CloseSessionDetail_ClosesMatchingEditorAndForgetsRestoreHistory()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Session();
        var factory = CreateFactory(shell);

        await factory.CloseSessionDetail(snapshot.Id);

        Assert.Equal(typeof(SessionDetailViewModel), shell.CloseIfOpenType);
        Assert.True(shell.CloseIfOpenForgetRestoreHistory);
        var match = Assert.IsType<Func<SessionDetailViewModel, bool>>(shell.CloseIfOpenMatch);
        Assert.True(match(factory.CreateSessionDetail(snapshot)));
        Assert.False(match(factory.CreateSessionDetail(TestSnapshots.Session())));
    }

    [Fact]
    public void OpenLiveDaqDetail_UsesIdentityKeyForDeduplication()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = CreateLiveDaqSnapshot("board-a");
        var otherSnapshot = CreateLiveDaqSnapshot("board-b");
        var sharedStream = Substitute.For<ILiveDaqSharedStream>();
        sharedStream.RequestedConfiguration.Returns(LiveDaqStreamConfiguration.Default);
        sharedStream.CurrentState.Returns(LiveDaqSharedStreamState.Empty);
        var factory = CreateFactory(shell);

        factory.OpenLiveDaqDetail(snapshot, sharedStream);

        Assert.Equal(typeof(LiveDaqDetailViewModel), shell.OpenOrFocusType);
        var match = Assert.IsType<Func<LiveDaqDetailViewModel, bool>>(shell.OpenOrFocusMatch);
        var create = Assert.IsType<Func<LiveDaqDetailViewModel>>(shell.OpenOrFocusCreate);

        Assert.True(match(factory.CreateLiveDaqDetail(snapshot, sharedStream)));
        Assert.False(match(factory.CreateLiveDaqDetail(otherSnapshot, sharedStream)));
        Assert.Equal(snapshot.IdentityKey, create().IdentityKey);
    }

    [Fact]
    public void OpenLiveSessionDetail_UsesIdentityKeyForDeduplication()
    {
        var shell = new CapturingShellCoordinator();
        var context = CreateLiveSessionContext("board-a");
        var liveSessionService = Substitute.For<ILiveSessionService>();
        liveSessionService.Current.Returns(LiveSessionPresentationSnapshot.Empty);
        var factory = CreateFactory(shell);

        factory.OpenLiveSessionDetail(context.IdentityKey, context, liveSessionService);

        Assert.Equal(typeof(LiveSessionDetailViewModel), shell.OpenOrFocusType);
        var match = Assert.IsType<Func<LiveSessionDetailViewModel, bool>>(shell.OpenOrFocusMatch);
        var create = Assert.IsType<Func<LiveSessionDetailViewModel>>(shell.OpenOrFocusCreate);

        Assert.True(match(factory.CreateLiveSessionDetail(context, liveSessionService)));
        Assert.False(match(factory.CreateLiveSessionDetail(
            CreateLiveSessionContext("board-b"),
            liveSessionService)));
        Assert.Equal(context.IdentityKey, create().IdentityKey);
    }

    private static LiveDaqSnapshot CreateLiveDaqSnapshot(string identityKey) => new(
        identityKey,
        "Board",
        BoardId: null,
        Host: null,
        Port: null,
        IsOnline: true,
        SetupName: null,
        BikeName: null);

    private static LiveDaqSessionContext CreateLiveSessionContext(string identityKey)
    {
        var bikeId = Guid.NewGuid();
        return new LiveDaqSessionContext(
            identityKey,
            BoardId: Guid.NewGuid(),
            DisplayName: "Board",
            SetupId: Guid.NewGuid(),
            SetupName: "race",
            BikeId: bikeId,
            BikeName: "demo",
            BikeData: new BikeData(180, 170, measurement => measurement, measurement => measurement),
            TravelCalibration: new LiveDaqTravelCalibration(null, null),
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            DampingSpeedCutoffOwner: new DampingSpeedCutoffOwner(bikeId, 0));
    }

    private static EditorFactory CreateFactory(CapturingShellCoordinator shell)
    {
        var sessionPresentationService = Substitute.For<ISessionPresentationService>();
        var sessionAnalysisService = Substitute.For<ISessionInsightsService>();
        var uiThreadDispatcher = new InlineUiThreadDispatcher();
        var backgroundTaskRunner = new InlineBackgroundTaskRunner();
        var analysisResultStateFactory = new RecordedSessionAnalysisResultStateFactory(
            new RecordedSessionAnalysisComputer(sessionAnalysisService),
            backgroundTaskRunner,
            uiThreadDispatcher);

        return new EditorFactory(
            TestCoordinatorSubstitutes.Bike(),
            Substitute.For<IBikeDependencyQuery>(),
            Substitute.For<IBikeStore>(),
            TestCoordinatorSubstitutes.Setup(),
            TestCoordinatorSubstitutes.Session(),
            TestCoordinatorSubstitutes.Track(),
            Substitute.For<ISessionStore>(),
            Substitute.For<IRecordedSessionProjection>(),
            sessionPresentationService,
            analysisResultStateFactory,
            new TestMapViewModelFactory(Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges()),
            Substitute.For<ISessionPreferences>(),
            Substitute.For<IRecordedSessionProcessingOptionCache>(),
            new TestSessionProcessedTelemetryReader(),
            Substitute.For<IRecordedSessionDerivationWindowCache>(),
            TestCoordinatorSubstitutes.LiveDaq(),
            Substitute.For<IDaqManagementService>(),
            Substitute.For<IFilesService>(),
            Substitute.For<ILiveDaqKnownBoardsQuery>(),
            Substitute.For<ILiveDaqStore>(),
            shell,
            Substitute.For<IDialogService>(),
            uiThreadDispatcher,
            new AppEnvironment(
                UiLayoutProfile.Workspace,
                UiLayoutProfile.Workspace,
                new AppCapabilities(
                    CanHostSyncServer: true,
                    CanPairAsClient: false,
                    HasHaptics: false,
                    SupportsMassStorageImport: true,
                    SupportsStorageProviderImport: true,
                    SupportsNativeWindowing: true),
                new InputCapabilities(
                    HasPointer: true,
                    HasTouch: false,
                    HasKeyboard: true,
                    SupportsPinch: false,
                    SupportsLongPressContextMenu: false)),
            new LayoutProfileTransitionState(),
            Array.Empty<IRecordedSessionExtensionFactory>(),
            Substitute.For<IExtensionDatabaseConnection>(),
            Substitute.For<IRecordedSessionDataReader>(),
            backgroundTaskRunner,
            () => throw new InvalidOperationException("The import-sessions resolver should not run in these tests."));
    }

    private sealed class CapturingShellCoordinator : IShellCoordinator
    {
        public ViewModelBase? OpenedView { get; private set; }
        public Type? OpenOrFocusType { get; private set; }
        public object? OpenOrFocusMatch { get; private set; }
        public object? OpenOrFocusCreate { get; private set; }
        public Type? OpenInBackgroundType { get; private set; }
        public object? OpenInBackgroundMatch { get; private set; }
        public object? OpenInBackgroundCreate { get; private set; }
        public Type? CloseIfOpenType { get; private set; }
        public object? CloseIfOpenMatch { get; private set; }
        public bool CloseIfOpenForgetRestoreHistory { get; private set; }

        public void Open(ViewModelBase view) => OpenedView = view;

        public void OpenOrFocus<T>(Func<T, bool> match, Func<T> create)
            where T : ViewModelBase
        {
            OpenOrFocusType = typeof(T);
            OpenOrFocusMatch = match;
            OpenOrFocusCreate = create;
        }

        public void OpenInBackground<T>(Func<T, bool> match, Func<T> create)
            where T : ViewModelBase
        {
            OpenInBackgroundType = typeof(T);
            OpenInBackgroundMatch = match;
            OpenInBackgroundCreate = create;
        }

        public void Close(ViewModelBase view)
        {
        }

        public Task CloseIfOpen<T>(Func<T, bool> match, bool forgetRestoreHistory = false)
            where T : ViewModelBase
        {
            CloseIfOpenType = typeof(T);
            CloseIfOpenMatch = match;
            CloseIfOpenForgetRestoreHistory = forgetRestoreHistory;
            return Task.CompletedTask;
        }

        public bool GoBack()
        {
            return false;
        }
    }
}
