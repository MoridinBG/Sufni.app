using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.ViewModels;

namespace Sufni.App.Tests.Coordinators;

public class DesktopShellCoordinatorTests
{
    private readonly IMainWindowShellHost host = Substitute.For<IMainWindowShellHost>();

    private DesktopShellCoordinator CreateCoordinator() => new(() => host);

    /// <summary>
    /// Minimal test-only `TabPageViewModelBase` subclass used as input
    /// data for the shell coordinator under test. Not a substitute for
    /// the SUT's dependency — the SUT depends on the host interface,
    /// which is substituted separately.
    /// </summary>
    private sealed class TestTabPageViewModel : TabPageViewModelBase;

    /// <summary>
    /// Minimal test-only `ViewModelBase` subclass used to exercise the
    /// non-tab branches of `Close()`.
    /// </summary>
    private sealed class TestViewModel : ViewModelBase;

    // ----- Open -----

    [Fact]
    public void Open_ForwardsToHostOpenView()
    {
        var view = new TestTabPageViewModel();
        var coordinator = CreateCoordinator();

        coordinator.Open(view);

        host.Received(1).OpenView(view);
    }

    // ----- OpenOrFocus -----

    [Fact]
    public void OpenOrFocus_ReusesExistingMatchingTab_AndDoesNotInvokeFactory()
    {
        var existing = new TestTabPageViewModel();
        host.Tabs.Returns(new TabPageViewModelBase[] { existing });
        var factoryInvoked = false;
        var coordinator = CreateCoordinator();

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: _ => true,
            create: () =>
            {
                factoryInvoked = true;
                return new TestTabPageViewModel();
            });

        Assert.False(factoryInvoked);
        host.Received(1).OpenView(existing);
    }

    [Fact]
    public void OpenOrFocus_InvokesFactory_AndForwardsNewInstance_WhenNoMatch()
    {
        host.Tabs.Returns(Array.Empty<TabPageViewModelBase>());
        var newView = new TestTabPageViewModel();
        var coordinator = CreateCoordinator();

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: _ => true,
            create: () => newView);

        host.Received(1).OpenView(newView);
    }

    [Fact]
    public void OpenOrFocus_PassesMatchPredicate_ThroughToTabsEnumerable()
    {
        var firstTab = new TestTabPageViewModel();
        var secondTab = new TestTabPageViewModel();
        host.Tabs.Returns(new TabPageViewModelBase[] { firstTab, secondTab });
        var coordinator = CreateCoordinator();

        coordinator.OpenOrFocus<TestTabPageViewModel>(
            match: t => ReferenceEquals(t, secondTab),
            create: () => throw new InvalidOperationException("factory should not run"));

        host.Received(1).OpenView(secondTab);
    }

    // ----- Close -----

    [Fact]
    public void Close_ForwardsToCloseTabPage_WhenViewIsTabPageViewModel()
    {
        var tab = new TestTabPageViewModel();
        var coordinator = CreateCoordinator();

        coordinator.Close(tab);

        host.Received(1).CloseTabPage(tab);
    }

    [Fact]
    public void Close_DoesNothing_WhenViewIsNotTabPageViewModel()
    {
        var view = new TestViewModel();
        var coordinator = CreateCoordinator();

        coordinator.Close(view);

        host.DidNotReceiveWithAnyArgs().CloseTabPage(default!);
    }

    // ----- CloseIfOpen -----

    [Fact]
    public void CloseIfOpen_ClosesMatchingTab_WhenPresent()
    {
        var tab = new TestTabPageViewModel();
        host.Tabs.Returns(new TabPageViewModelBase[] { tab });
        var coordinator = CreateCoordinator();

        coordinator.CloseIfOpen<TestTabPageViewModel>(_ => true);

        host.Received(1).CloseTabPage(tab);
    }

    [Fact]
    public void CloseIfOpen_IsNoOp_WhenNoMatchingTab()
    {
        host.Tabs.Returns(Array.Empty<TabPageViewModelBase>());
        var coordinator = CreateCoordinator();

        coordinator.CloseIfOpen<TestTabPageViewModel>(_ => true);

        host.DidNotReceiveWithAnyArgs().CloseTabPage(default!);
    }

    // ----- GoBack -----

    [Fact]
    public void GoBack_DoesNotTouchHost()
    {
        var coordinator = CreateCoordinator();

        coordinator.GoBack();

        host.DidNotReceiveWithAnyArgs().OpenView(default!);
        host.DidNotReceiveWithAnyArgs().CloseTabPage(default!);
        _ = host.DidNotReceiveWithAnyArgs().Tabs;
    }
}
