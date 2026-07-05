using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Tests.Shell.Coordinators;

public class ShellWorkspaceCoordinatorTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

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

    // ----- OpenOrFocus -----

    [Fact]
    public void OpenOrFocus_ReusesExistingMatchingTab_AndDoesNotInvokeFactory()
    {
        var workspace = CreateWorkspace();
        var existing = new TestTabPageViewModel(id: 1);
        var other = new TestTabPageViewModel(id: 2);
        workspace.OpenOrFocus(other);
        workspace.OpenInBackground(existing);
        var factoryInvoked = false;
        var coordinator = CreateCoordinator(workspace);

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 1,
            create: () =>
            {
                factoryInvoked = true;
                return new TestTabPageViewModel(id: 1);
            });

        Assert.False(factoryInvoked);
        Assert.Same(existing, workspace.CurrentTab);
        Assert.Equal([other, existing], workspace.Tabs);
    }

    [Fact]
    public void OpenOrFocus_InvokesFactory_AndFocusesNewInstance_WhenNoMatch()
    {
        var workspace = CreateWorkspace();
        var newView = new TestTabPageViewModel();
        var coordinator = CreateCoordinator(workspace);

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: _ => true,
            create: () => newView);

        Assert.Same(newView, workspace.CurrentTab);
        Assert.Equal([newView], workspace.Tabs);
    }

    [Fact]
    public void OpenOrFocus_RestoresMatchingClosedEntry_WithFreshInstance()
    {
        var workspace = CreateWorkspace();
        var coordinator = CreateCoordinator(workspace);
        var original = new TestTabPageViewModel(id: 1);
        var restored = new TestTabPageViewModel(id: 1);

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 1,
            create: () => original,
            restoreEntry: RestoreEntry(1, () => restored));
        coordinator.Close(original);
        var factoryInvoked = false;

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 1,
            create: () =>
            {
                factoryInvoked = true;
                return new TestTabPageViewModel(id: 1);
            },
            restoreEntry: RestoreEntry(1, () => restored));

        Assert.False(factoryInvoked);
        Assert.Same(restored, workspace.CurrentTab);
        Assert.NotSame(original, workspace.CurrentTab);
        Assert.Equal([restored], workspace.Tabs);
    }

    [Fact]
    public void OpenOrFocus_PassesMatchPredicate_ThroughToTabsEnumerable()
    {
        var workspace = CreateWorkspace();
        var firstTab = new TestTabPageViewModel(id: 1);
        var secondTab = new TestTabPageViewModel(id: 2);
        workspace.OpenOrFocus(firstTab);
        workspace.OpenInBackground(secondTab);
        var coordinator = CreateCoordinator(workspace);

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 2,
            create: () => throw new InvalidOperationException("factory should not run"));

        Assert.Same(secondTab, workspace.CurrentTab);
    }

    // ----- OpenInBackground -----

    [Fact]
    public void OpenInBackground_AddsNewTabWithoutOpeningIt_WhenNoMatch()
    {
        var workspace = CreateWorkspace();
        var current = new TestTabPageViewModel(id: 1);
        var newView = new TestTabPageViewModel(id: 2);
        workspace.OpenOrFocus(current);
        var coordinator = CreateCoordinator(workspace);

        coordinator.OpenInBackground<TestTabPageViewModel>(
            match: tab => tab.Id == 2,
            create: () => newView);

        Assert.Same(current, workspace.CurrentTab);
        Assert.Equal([current, newView], workspace.Tabs);
    }

    [Fact]
    public void OpenInBackground_RestoresMatchingClosedEntry_WithFreshInstance()
    {
        var workspace = CreateWorkspace();
        var current = new TestTabPageViewModel(id: 1);
        var original = new TestTabPageViewModel(id: 2);
        var restored = new TestTabPageViewModel(id: 2);
        workspace.OpenOrFocus(current);
        var coordinator = CreateCoordinator(workspace);

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 2,
            create: () => original,
            restoreEntry: RestoreEntry(2, () => restored));
        coordinator.Close(original);
        var factoryInvoked = false;

        coordinator.OpenInBackground<TestTabPageViewModel>(
            match: tab => tab.Id == 2,
            create: () =>
            {
                factoryInvoked = true;
                return new TestTabPageViewModel(id: 2);
            },
            restoreEntry: RestoreEntry(2, () => restored));

        Assert.False(factoryInvoked);
        Assert.Same(current, workspace.CurrentTab);
        Assert.NotSame(original, restored);
        Assert.Equal([current, restored], workspace.Tabs);
    }

    [Fact]
    public void OpenInBackground_DoesNothing_WhenMatchingTabIsAlreadyOpen()
    {
        var workspace = CreateWorkspace();
        var existing = new TestTabPageViewModel();
        workspace.OpenOrFocus(existing);
        var coordinator = CreateCoordinator(workspace);

        coordinator.OpenInBackground<TestTabPageViewModel>(
            match: _ => true,
            create: () => throw new InvalidOperationException("factory should not run"));

        Assert.Same(existing, workspace.CurrentTab);
        Assert.Equal([existing], workspace.Tabs);
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
    public void Close_RemembersRestoreEntry_WhenLayoutProfileIsWorkspace()
    {
        var workspace = CreateWorkspace();
        var coordinator = CreateCoordinator(workspace);
        var original = new TestTabPageViewModel(id: 1);
        var restored = new TestTabPageViewModel(id: 1);

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 1,
            create: () => original,
            restoreEntry: RestoreEntry(1, () => restored));
        coordinator.Close(original);
        workspace.Restore();

        Assert.Same(restored, workspace.CurrentTab);
        Assert.NotSame(original, workspace.CurrentTab);
        Assert.Equal([restored], workspace.Tabs);
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
    public void Close_DoesNotRememberClosedTab_WhenLayoutProfileIsCompact()
    {
        var workspace = CreateWorkspace();
        var coordinator = CreateCoordinator(workspace, UiLayoutProfile.Compact);
        var tab = new TestTabPageViewModel(id: 1);

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: candidate => candidate.Id == 1,
            create: () => tab,
            restoreEntry: RestoreEntry(1, () => new TestTabPageViewModel(id: 1)));
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

    // ----- CloseIfOpen -----

    [Fact]
    public async Task CloseIfOpen_ClosesMatchingTab_WhenPresent()
    {
        var workspace = CreateWorkspace();
        var tab = new TestTabPageViewModel();
        workspace.OpenOrFocus(tab);
        var coordinator = CreateCoordinator(workspace);

        await coordinator.CloseIfOpen<TestTabPageViewModel>(_ => true);

        Assert.Null(workspace.CurrentTab);
        Assert.Empty(workspace.Tabs);
    }

    [Fact]
    public async Task CloseIfOpen_RunsCloseCleanup_WhenPresent()
    {
        var workspace = CreateWorkspace();
        var tab = new TestTabPageViewModel();
        workspace.OpenOrFocus(tab);
        var coordinator = CreateCoordinator(workspace);

        await coordinator.CloseIfOpen<TestTabPageViewModel>(_ => true);

        Assert.Equal(1, tab.CloseCount);
    }

    [Fact]
    public async Task CloseIfOpen_ForgetsRestoreHistory_WhenRequested()
    {
        var workspace = CreateWorkspace();
        var closed = new TestTabPageViewModel(id: 1);
        var coordinator = CreateCoordinator(workspace);

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 1,
            create: () => closed,
            restoreEntry: RestoreEntry(1, () => new TestTabPageViewModel(id: 1)));
        await coordinator.CloseIfOpen<TestTabPageViewModel>(
            tab => tab.Id == 1,
            forgetRestoreHistory: true,
            restoreKey: 1);
        var factoryInvoked = false;
        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 1,
            create: () =>
            {
                factoryInvoked = true;
                return new TestTabPageViewModel(id: 1);
            },
            restoreEntry: RestoreEntry(1, () => new TestTabPageViewModel(id: 1)));

        Assert.True(factoryInvoked);
        Assert.NotSame(closed, workspace.CurrentTab);
    }

    [Fact]
    public async Task CloseIfOpen_DoesNotRememberClosedTab_WhenLayoutProfileIsCompact()
    {
        var workspace = CreateWorkspace();
        var closed = new TestTabPageViewModel(id: 1);
        var coordinator = CreateCoordinator(workspace, UiLayoutProfile.Compact);

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 1,
            create: () => closed,
            restoreEntry: RestoreEntry(1, () => new TestTabPageViewModel(id: 1)));
        await coordinator.CloseIfOpen<TestTabPageViewModel>(tab => tab.Id == 1);
        workspace.Restore();

        Assert.Null(workspace.CurrentTab);
        Assert.Empty(workspace.Tabs);
    }

    [Fact]
    public async Task CloseIfOpen_ForgetsRestoreHistory_EvenWhenNoMatchingOpenTab()
    {
        var workspace = CreateWorkspace();
        var closed = new TestTabPageViewModel(id: 1);
        var coordinator = CreateCoordinator(workspace);

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 1,
            create: () => closed,
            restoreEntry: RestoreEntry(1, () => new TestTabPageViewModel(id: 1)));
        coordinator.Close(closed);
        await coordinator.CloseIfOpen<TestTabPageViewModel>(
            tab => tab.Id == 1,
            forgetRestoreHistory: true,
            restoreKey: 1);
        var factoryInvoked = false;
        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: tab => tab.Id == 1,
            create: () =>
            {
                factoryInvoked = true;
                return new TestTabPageViewModel(id: 1);
            },
            restoreEntry: RestoreEntry(1, () => new TestTabPageViewModel(id: 1)));

        Assert.True(factoryInvoked);
        Assert.NotSame(closed, workspace.CurrentTab);
    }

    [Fact]
    public async Task CloseIfOpen_IsNoOp_WhenNoMatchingTab()
    {
        var workspace = CreateWorkspace();
        var tab = new TestTabPageViewModel(id: 1);
        workspace.OpenOrFocus(tab);
        var coordinator = CreateCoordinator(workspace);

        await coordinator.CloseIfOpen<TestTabPageViewModel>(candidate => candidate.Id == 2);

        Assert.Same(tab, workspace.CurrentTab);
        Assert.Equal([tab], workspace.Tabs);
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

    private static ShellWorkspaceViewModel CreateWorkspace() => new(TestDispatcher);

    private static ClosedTabRestoreEntry RestoreEntry(
        int id,
        Func<TestTabPageViewModel?> restore) =>
        ClosedTabRestoreEntry.For(id, restore);

    private static ShellWorkspaceCoordinator CreateCoordinator(
        ShellWorkspaceViewModel workspace,
        UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace) =>
        new(workspace, CreateEnvironment(layoutProfile));

    private static IAppEnvironment CreateEnvironment(UiLayoutProfile layoutProfile) =>
        new AppEnvironment(
            DefaultLayoutProfile: layoutProfile,
            LayoutProfile: layoutProfile,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: true,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: true,
                HasKeyboard: true,
                SupportsLongPressContextMenu: true));

    private sealed class TestTabPageViewModel : TabPageViewModelBase
    {
        public TestTabPageViewModel(int id = 0)
            : base(TestDispatcher)
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
            : base(TestDispatcher)
        {
        }
    }
}
