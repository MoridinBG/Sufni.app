using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.Management;
using Sufni.App.SessionGraph;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Coordinators;

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
    public void CloseBikeEditor_ClosesMatchingEditorAndForgetsRestoreHistory()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Bike();
        var factory = CreateFactory(shell);

        factory.CloseBikeEditor(snapshot.Id);

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
    public void CloseSetupEditor_ClosesMatchingEditorAndForgetsRestoreHistory()
    {
        var shell = new CapturingShellCoordinator();
        var snapshot = TestSnapshots.Setup();
        var factory = CreateFactory(shell);

        factory.CloseSetupEditor(snapshot.Id);

        Assert.Equal(typeof(SetupEditorViewModel), shell.CloseIfOpenType);
        Assert.True(shell.CloseIfOpenForgetRestoreHistory);
        var match = Assert.IsType<Func<SetupEditorViewModel, bool>>(shell.CloseIfOpenMatch);
        Assert.True(match(factory.CreateSetupEditor(snapshot, isNew: false)));
        Assert.False(match(factory.CreateSetupEditor(TestSnapshots.Setup(), isNew: false)));
    }

    private static EditorFactory CreateFactory(CapturingShellCoordinator shell) =>
        new(
            TestCoordinatorSubstitutes.Bike(),
            Substitute.For<IBikeDependencyQuery>(),
            Substitute.For<IBikeStore>(),
            TestCoordinatorSubstitutes.Setup(),
            TestCoordinatorSubstitutes.Session(),
            Substitute.For<ISessionStore>(),
            Substitute.For<IRecordedSessionGraph>(),
            Substitute.For<ISessionPresentationService>(),
            Substitute.For<ISessionAnalysisService>(),
            Substitute.For<ITileLayerService>(),
            Substitute.For<ISessionPreferences>(),
            TestCoordinatorSubstitutes.LiveDaq(),
            Substitute.For<IDaqManagementService>(),
            Substitute.For<IFilesService>(),
            Substitute.For<ILiveDaqKnownBoardsQuery>(),
            Substitute.For<ILiveDaqStore>(),
            shell,
            Substitute.For<IDialogService>(),
            new InlineUiThreadDispatcher(),
            new DesktopSessionLayoutStrategy(),
            Array.Empty<IRecordedSessionExtensionFactory>(),
            Substitute.For<IExtensionDatabaseConnection>(),
            Substitute.For<IRecordedSessionDataReader>(),
            new InlineBackgroundTaskRunner());

    private sealed class CapturingShellCoordinator : IShellCoordinator
    {
        public ViewModelBase? OpenedView { get; private set; }
        public Type? OpenOrFocusType { get; private set; }
        public object? OpenOrFocusMatch { get; private set; }
        public object? OpenOrFocusCreate { get; private set; }
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

        public void Close(ViewModelBase view)
        {
        }

        public void CloseIfOpen<T>(Func<T, bool> match, bool forgetRestoreHistory = false)
            where T : ViewModelBase
        {
            CloseIfOpenType = typeof(T);
            CloseIfOpenMatch = match;
            CloseIfOpenForgetRestoreHistory = forgetRestoreHistory;
        }

        public void GoBack()
        {
        }
    }
}
