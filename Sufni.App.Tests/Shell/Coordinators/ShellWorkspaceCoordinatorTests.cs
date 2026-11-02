using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.LiveDaq;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Tests.Shell.Coordinators;

public class ShellWorkspaceCoordinatorTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [Theory]
    [InlineData(UiLayoutProfile.Compact, false)]
    [InlineData(UiLayoutProfile.Workspace, false)]
    [InlineData(UiLayoutProfile.Workspace, true)]
    public async Task OpenEditor_ReplacesClosedDiagnostics_AndOwnsDetachedCleanup(UiLayoutProfile profile, bool background)
    {
        var oldHarness = new LiveDaqDetailHarness();
        var freshHarness = new LiveDaqDetailHarness();
        var old = oldHarness.CreateEditor();
        var fresh = freshHarness.CreateEditor();
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldHarness.StreamLease.DisposeAsync().Returns(new ValueTask(cleanup.Task));
        var key = new EditorKey.LiveDaq(old.IdentityKey);
        var factory = Substitute.For<IEditorFactory>();
        factory.CreateEditor(key, Arg.Any<IShellCoordinator>()).Returns(old, fresh);
        var workspace = CreateWorkspace();
        await using var shell = new ShellWorkspaceCoordinator(workspace, CreateEnvironment(profile), factory,
            Substitute.For<IUiPreferences>(), TestDispatcher, TestShellFeedback.Create());
        var previous = new TestTabPageViewModel();
        shell.Open(previous);
        shell.OpenEditor(key);
        shell.OpenEditor(key);
        factory.Received(1).CreateEditor(key, shell);
        oldHarness.PublishStreamState(LiveDaqSharedStreamState.Empty with { ConnectionState = new LiveConnectionState.Closed(LiveStreamClosedReason.Offline) });

        shell.OpenEditor(key, background);
        shell.OpenEditor(key, background);

        Assert.DoesNotContain(old, workspace.Tabs);
        Assert.Contains(fresh, workspace.Tabs);
        Assert.Same(background ? (TabPageViewModelBase)previous : fresh, workspace.CurrentTab);
        factory.Received(2).CreateEditor(key, shell);
        Assert.False(oldHarness.ManagementAvailability.HasObservers);
        var disposal = shell.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        cleanup.SetResult();
        await disposal.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await fresh.PrepareCloseAsync();
    }

    [Fact]
    public async Task ReplacedDiagnosticsCleanupFailure_GoesToShellFeedback_WithoutBlockingFreshTab()
    {
        var oldHarness = new LiveDaqDetailHarness();
        var old = oldHarness.CreateEditor();
        var fresh = new LiveDaqDetailHarness().CreateEditor();
        oldHarness.StreamLease.DisposeAsync().Returns(ValueTask.FromException(new IOException("cleanup diagnostic")));
        var key = new EditorKey.LiveDaq(old.IdentityKey);
        var factory = Substitute.For<IEditorFactory>();
        factory.CreateEditor(key, Arg.Any<IShellCoordinator>()).Returns(old, fresh);
        var workspace = CreateWorkspace();
        using var feedback = TestShellFeedback.Create();
        await using var shell = new ShellWorkspaceCoordinator(workspace, CreateEnvironment(UiLayoutProfile.Workspace), factory,
            Substitute.For<IUiPreferences>(), TestDispatcher, feedback);
        shell.OpenEditor(key);
        oldHarness.PublishStreamState(LiveDaqSharedStreamState.Empty with { ConnectionState = new LiveConnectionState.Closed(LiveStreamClosedReason.Offline) });

        shell.OpenEditor(key);

        Assert.Same(fresh, Assert.Single(workspace.Tabs));
        Assert.DoesNotContain("cleanup diagnostic", Assert.Single(feedback.ErrorMessages).Summary);
        await fresh.PrepareCloseAsync();
    }

    [Fact]
    public async Task OpenEditor_DeduplicatesBackgroundTabs_AndRestoresCurrentSnapshot()
    {
        var workspace = CreateWorkspace();
        var store = Substitute.For<ISessionStore>();
        var original = TestSnapshots.Session(name: "original");
        store.Get(original.Id).Returns(original);
        var shell = new ShellWorkspaceCoordinator(workspace, CreateEnvironment(UiLayoutProfile.Workspace),
            TestEditorFactories.Create(sessionStore: store), Substitute.For<IUiPreferences>(), TestDispatcher, TestShellFeedback.Create());
        var previous = new TestTabPageViewModel();
        shell.Open(previous);
        var key = new EditorKey.Session(original.Id);

        shell.OpenEditor(key, background: true);
        var editor = Assert.Single(workspace.Tabs.OfType<SessionDetailViewModel>());
        Assert.Same(previous, workspace.CurrentTab);
        shell.OpenEditor(key);
        Assert.Same(editor, workspace.CurrentTab);
        Assert.Equal(2, workspace.Tabs.Count);
        await editor.CloseCommand.ExecuteAsync(null);
        store.Get(original.Id).Returns(original with { Name = "updated", Updated = 42 });

        workspace.Restore();

        var restored = Assert.IsType<SessionDetailViewModel>(workspace.CurrentTab);
        Assert.NotSame(editor, restored);
        Assert.Equal("updated", restored.Name);
        Assert.Equal(42, restored.BaselineUpdated);
        await restored.PrepareCloseAsync();
    }

    [Fact]
    public async Task CloseEditor_ForgetsDeletedEditor_WithoutNavigatingPastPreviousTab()
    {
        var workspace = CreateWorkspace();
        var store = Substitute.For<ISessionStore>();
        var snapshot = TestSnapshots.Session();
        store.Get(snapshot.Id).Returns(snapshot);
        var shell = new ShellWorkspaceCoordinator(workspace, CreateEnvironment(UiLayoutProfile.Workspace),
            TestEditorFactories.Create(sessionStore: store), Substitute.For<IUiPreferences>(), TestDispatcher, TestShellFeedback.Create());
        var previous = new TestTabPageViewModel();
        shell.Open(previous);
        var key = new EditorKey.Session(snapshot.Id);
        shell.OpenEditor(key);

        await shell.CloseEditorAsync(key);
        store.Get(snapshot.Id).Returns((SessionSnapshot?)null);
        workspace.Restore();
        shell.OpenEditor(key);

        Assert.Same(previous, workspace.CurrentTab);
        Assert.Equal([previous], workspace.Tabs);
    }

    [Fact]
    public async Task OpenEditor_ImportReusesOpenTab_AndCreatesFreshTabAfterClose()
    {
        var workspace = CreateWorkspace();
        var shell = new ShellWorkspaceCoordinator(workspace, CreateEnvironment(UiLayoutProfile.Workspace),
            TestEditorFactories.Create(), Substitute.For<IUiPreferences>(), TestDispatcher, TestShellFeedback.Create());
        var key = new EditorKey.ImportSessions();
        shell.OpenEditor(key);
        var first = Assert.IsType<ImportSessionsViewModel>(workspace.CurrentTab);
        shell.OpenEditor(key);
        Assert.Same(first, Assert.Single(workspace.Tabs));
        await first.CloseCommand.ExecuteAsync(null);

        shell.OpenEditor(key);

        var reopened = Assert.IsType<ImportSessionsViewModel>(Assert.Single(workspace.Tabs));
        Assert.NotSame(first, reopened);
        await reopened.PrepareCloseAsync();
    }

    [Fact]
    public async Task OpenEditor_LiveSessionReusesTabAndCaptureService()
    {
        var live = new LiveDaqDetailHarness();
        var snapshot = LiveDaqDetailHarness.DefaultSnapshot();
        live.LiveDaqStore.ReplaceAll([snapshot]);
        var registry = Substitute.For<ILiveDaqSharedStreamRegistry>();
        registry.GetOrCreate(snapshot).Returns(live.SharedStream);
        var services = Substitute.For<ILiveSessionServiceFactory>();
        services.Create(Arg.Any<Sufni.App.LiveDaq.Queries.LiveDaqSessionContext>(), live.SharedStream)
            .Returns(_ => StubLiveSessionService.WithDefaultLiveStream());
        var workspace = CreateWorkspace();
        var shell = new ShellWorkspaceCoordinator(workspace, CreateEnvironment(UiLayoutProfile.Workspace),
            TestEditorFactories.Create(liveDaqStore: live.LiveDaqStore, knownBoards: live.KnownBoardsQuery,
                streamRegistry: registry, liveSessionFactory: services), Substitute.For<IUiPreferences>(), TestDispatcher, TestShellFeedback.Create());
        var key = new EditorKey.LiveSession(snapshot.IdentityKey);

        shell.OpenEditor(key);
        var first = Assert.IsType<LiveSessionDetailViewModel>(workspace.CurrentTab);
        live.PublishStreamState(LiveDaqSharedStreamState.Empty with { ConnectionState = new LiveConnectionState.Closed(LiveStreamClosedReason.Offline) });
        shell.OpenEditor(key);

        Assert.Same(first, Assert.Single(workspace.Tabs));
        services.Received(1).Create(Arg.Any<Sufni.App.LiveDaq.Queries.LiveDaqSessionContext>(), live.SharedStream);
        await first.PrepareCloseAsync();
    }

    // ----- Open -----

    [Fact]
    public void Open_FocusesTabPage()
    {
        var workspace = CreateWorkspace();
        var coordinator = CreateCoordinator(workspace);
        var view = new TestTabPageViewModel();

        coordinator.Open(view);

        Assert.Same(view, workspace.CurrentTab);
        Assert.Equal([view], workspace.Tabs);
    }

    [Fact]
    public void Open_IgnoresNonTabView()
    {
        var workspace = CreateWorkspace();
        var coordinator = CreateCoordinator(workspace);

        coordinator.Open(new TestViewModel());

        Assert.Null(workspace.CurrentTab);
        Assert.Empty(workspace.Tabs);
    }

    // ----- Close -----

    [Fact]
    public void Close_RemovesTabPage()
    {
        var workspace = CreateWorkspace();
        var tab = new TestTabPageViewModel();
        workspace.OpenOrFocus(tab);
        var coordinator = CreateCoordinator(workspace);

        coordinator.Close(tab);

        Assert.Null(workspace.CurrentTab);
        Assert.Empty(workspace.Tabs);
    }

    [Fact]
    public void Close_DoesNothing_WhenViewIsNotTabPageViewModel()
    {
        var workspace = CreateWorkspace();
        var coordinator = CreateCoordinator(workspace);

        coordinator.Close(new TestViewModel());

        Assert.Null(workspace.CurrentTab);
        Assert.Empty(workspace.Tabs);
    }

    [Fact]
    public void Close_DoesNotRememberDirectOpenedTab()
    {
        var workspace = CreateWorkspace();
        var tab = new TestTabPageViewModel();
        var coordinator = CreateCoordinator(workspace);

        coordinator.Open(tab);
        coordinator.Close(tab);
        workspace.Restore();

        Assert.Null(workspace.CurrentTab);
        Assert.Empty(workspace.Tabs);
    }

    [Fact]
    public void CloseTab_DoesNotSelectRemovedPreviousTab_WhenCurrentTabClosesLater()
    {
        var workspace = CreateWorkspace();
        var previous = new TestTabPageViewModel(id: 1);
        var current = new TestTabPageViewModel(id: 2);
        workspace.OpenOrFocus(previous);
        workspace.OpenOrFocus(current);

        workspace.CloseTab(previous);
        workspace.CloseTab(current);

        Assert.Null(workspace.CurrentTab);
        Assert.DoesNotContain(previous, workspace.Tabs);
    }

    [Theory]
    [InlineData(UiLayoutProfile.Compact)]
    [InlineData(UiLayoutProfile.Workspace)]
    public async Task SwitchLayoutProfile_PersistsBeforeApplying_AndKeepsDirtyTabs(UiLayoutProfile target)
    {
        var workspace = CreateWorkspace();
        var initial = target == UiLayoutProfile.Compact ? UiLayoutProfile.Workspace : UiLayoutProfile.Compact;
        var environment = CreateEnvironment(initial);
        var preferences = Substitute.For<IUiPreferences>();
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        preferences.SetLayoutProfileAsync(target).Returns(persisted.Task);
        var shell = new ShellWorkspaceCoordinator(workspace, environment, Substitute.For<IEditorFactory>(),
            preferences, TestDispatcher, TestShellFeedback.Create());
        var background = new TestTabPageViewModel(1) { IsDirty = true };
        var current = new TestTabPageViewModel(2) { IsDirty = true };
        shell.Open(background);
        shell.Open(current);

        var transition = shell.SwitchLayoutProfileAsync(target);
        Assert.Equal(initial, environment.LayoutProfile);
        persisted.SetResult();
        await transition.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(target, environment.LayoutProfile);
        Assert.Equal([background, current], workspace.Tabs);
        Assert.Same(current, workspace.CurrentTab);
        Assert.True(background.IsDirty);
        Assert.True(current.IsDirty);
        Assert.Equal(0, background.CloseCount);
        Assert.Equal(0, current.CloseCount);
    }

    [Theory]
    [InlineData(UiLayoutProfile.Compact)]
    [InlineData(UiLayoutProfile.Workspace)]
    public async Task CompleteEdit_UsesCurrentLayoutProfile(UiLayoutProfile profile)
    {
        var workspace = CreateWorkspace();
        var store = Substitute.For<ISessionStore>();
        var snapshot = TestSnapshots.Session();
        store.Get(snapshot.Id).Returns(snapshot);
        var environment = Substitute.For<IAppEnvironment>();
        environment.LayoutProfile.Returns(UiLayoutProfile.Workspace);
        var coordinator = new ShellWorkspaceCoordinator(workspace, environment,
            TestEditorFactories.Create(sessionStore: store), Substitute.For<IUiPreferences>(), TestDispatcher, TestShellFeedback.Create());
        var previous = new TestTabPageViewModel();
        coordinator.Open(previous);
        var key = new EditorKey.Session(snapshot.Id);
        coordinator.OpenEditor(key);
        var editor = Assert.IsType<SessionDetailViewModel>(workspace.CurrentTab);
        environment.LayoutProfile.Returns(profile);

        coordinator.CompleteEdit(key);

        Assert.Same(profile == UiLayoutProfile.Compact ? previous : editor, workspace.CurrentTab);
        Assert.Equal([previous, editor], workspace.Tabs);
        await editor.PrepareCloseAsync();
    }

    [Fact]
    public void CompleteEdit_DoesNotNavigateAnotherSurface_AfterFocusChanges()
    {
        var workspace = CreateWorkspace();
        var previous = new TestTabPageViewModel(1);
        var current = new TestTabPageViewModel(2);
        workspace.OpenOrFocus(previous);
        workspace.OpenOrFocus(current);
        var coordinator = CreateCoordinator(workspace, UiLayoutProfile.Compact);

        coordinator.CompleteEdit(new EditorKey.Session(Guid.NewGuid()));

        Assert.Same(current, workspace.CurrentTab);
    }

    // ----- GoBack -----

    [Fact]
    public void GoBack_ReturnsToPrimarySurface_WhenThereIsNoPreviousTab()
    {
        var workspace = CreateWorkspace();
        var tab = new TestTabPageViewModel();
        workspace.OpenOrFocus(tab);
        var coordinator = CreateCoordinator(workspace);

        var handled = coordinator.GoBack();

        Assert.True(handled);
        Assert.Null(workspace.CurrentTab);
        Assert.Equal([tab], workspace.Tabs);
    }

    [Fact]
    public void GoBack_ReturnsToPreviouslyFocusedTab()
    {
        var workspace = CreateWorkspace();
        var first = new TestTabPageViewModel(id: 1);
        var second = new TestTabPageViewModel(id: 2);
        workspace.OpenOrFocus(first);
        workspace.OpenOrFocus(second);
        var coordinator = CreateCoordinator(workspace);

        var handled = coordinator.GoBack();

        Assert.True(handled);
        Assert.Same(first, workspace.CurrentTab);
        Assert.Equal([first, second], workspace.Tabs);
    }

    [Fact]
    public void GoBack_WalksFocusHistory_ThenReturnsToPrimarySurface()
    {
        var workspace = CreateWorkspace();
        var first = new TestTabPageViewModel(id: 1);
        var second = new TestTabPageViewModel(id: 2);
        workspace.OpenOrFocus(first);
        workspace.OpenOrFocus(second);
        var coordinator = CreateCoordinator(workspace);

        var firstBackHandled = coordinator.GoBack();
        var secondBackHandled = coordinator.GoBack();

        Assert.True(firstBackHandled);
        Assert.True(secondBackHandled);
        Assert.Null(workspace.CurrentTab);
        Assert.Equal([first, second], workspace.Tabs);
    }

    [Fact]
    public void GoBack_SkipsClosedTabsInFocusHistory()
    {
        var workspace = CreateWorkspace();
        var first = new TestTabPageViewModel(id: 1);
        var second = new TestTabPageViewModel(id: 2);
        var third = new TestTabPageViewModel(id: 3);
        workspace.OpenOrFocus(first);
        workspace.OpenOrFocus(second);
        workspace.OpenOrFocus(third);
        workspace.CloseTab(second);
        var coordinator = CreateCoordinator(workspace);

        var handled = coordinator.GoBack();

        Assert.True(handled);
        Assert.Same(first, workspace.CurrentTab);
        Assert.Equal([first, third], workspace.Tabs);
    }

    [Fact]
    public void GoBack_ReturnsFalse_WhenNoTabIsSelected()
    {
        var workspace = CreateWorkspace();
        var coordinator = CreateCoordinator(workspace);

        var handled = coordinator.GoBack();

        Assert.False(handled);
        Assert.Null(workspace.CurrentTab);
        Assert.Empty(workspace.Tabs);
    }

    private static ShellWorkspaceViewModel CreateWorkspace() => new(TestDispatcher, timeProvider: new FakeTimeProvider());

    private static ShellWorkspaceCoordinator CreateCoordinator(
        ShellWorkspaceViewModel workspace,
        UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace) =>
        new(workspace, CreateEnvironment(layoutProfile), Substitute.For<IEditorFactory>(), Substitute.For<IUiPreferences>(), TestDispatcher, TestShellFeedback.Create());

    private static IAppEnvironment CreateEnvironment(UiLayoutProfile layoutProfile) =>
        new AppEnvironment(
            LayoutProfile: layoutProfile,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: true,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                CanPlayVideo: false),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: true,
                HasKeyboard: true,
                SupportsLongPressContextMenu: true,
                CommandModifier: CommandModifier.Control));

    private sealed class TestTabPageViewModel : TabPageViewModelBase
    {
        public TestTabPageViewModel(int id = 0)
            : base(Substitute.For<IShellCoordinator>(), Substitute.For<IDialogService>(), TestDispatcher, timeProvider: new FakeTimeProvider())
        {
            Id = id;
        }

        public int Id { get; }
        public int CloseCount { get; private set; }

        protected override Task CloseImplementation()
        {
            CloseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestViewModel : ViewModelBase
    {
        public TestViewModel()
            : base(TestDispatcher, timeProvider: new FakeTimeProvider())
        {
        }
    }
}
