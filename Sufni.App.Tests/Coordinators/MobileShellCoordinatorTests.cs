using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.ViewModels;

namespace Sufni.App.Tests.Coordinators;

public class MobileShellCoordinatorTests
{
    private readonly IMainViewShellHost host = Substitute.For<IMainViewShellHost>();

    private MobileShellCoordinator CreateCoordinator() => new(() => host);

    /// <summary>
    /// Minimal test-only `ViewModelBase` subclass used as input data
    /// for the shell coordinator under test. Not a substitute for the
    /// SUT's dependency — the SUT depends on `IMainViewShellHost`,
    /// which is substituted separately.
    /// </summary>
    private sealed class TestViewModel : ViewModelBase;

    // ----- Open -----

    [Fact]
    public void Open_ForwardsToHostOpenView()
    {
        var view = new TestViewModel();
        var coordinator = CreateCoordinator();

        coordinator.Open(view);

        host.Received(1).OpenView(view);
    }

    // ----- OpenOrFocus -----

    [Fact]
    public void OpenOrFocus_AlwaysInvokesFactory_AndForwardsNewInstance()
    {
        var newView = new TestViewModel();
        var coordinator = CreateCoordinator();

        coordinator.OpenOrFocus<TestViewModel>(
            match: _ => true,
            create: () => newView);

        host.Received(1).OpenView(newView);
    }

    [Fact]
    public void OpenOrFocus_DoesNotConsultMatchPredicate_OrInspectAnyExistingState()
    {
        var newView = new TestViewModel();
        var matchInvoked = false;
        var coordinator = CreateCoordinator();

        coordinator.OpenOrFocus<TestViewModel>(
            match: _ =>
            {
                matchInvoked = true;
                return true;
            },
            create: () => newView);

        Assert.False(matchInvoked);
        host.Received(1).OpenView(newView);
    }

    // ----- Close -----

    [Fact]
    public void Close_PopsCurrentView_WhenSuppliedViewIsCurrent()
    {
        var view = new TestViewModel();
        host.CurrentView.Returns(view);
        var coordinator = CreateCoordinator();

        coordinator.Close(view);

        host.Received(1).OpenPreviousView();
    }

    [Fact]
    public void Close_IsNoOp_WhenSuppliedViewIsNotCurrent()
    {
        var current = new TestViewModel();
        var other = new TestViewModel();
        host.CurrentView.Returns(current);
        var coordinator = CreateCoordinator();

        coordinator.Close(other);

        host.DidNotReceive().OpenPreviousView();
    }

    // ----- CloseIfOpen -----

    [Fact]
    public void CloseIfOpen_IsAlwaysNoOp_OnMobile()
    {
        var coordinator = CreateCoordinator();

        coordinator.CloseIfOpen<TestViewModel>(_ => true);

        host.DidNotReceive().OpenPreviousView();
        host.DidNotReceiveWithAnyArgs().OpenView(default!);
        _ = host.DidNotReceive().CurrentView;
    }

    // ----- GoBack -----

    [Fact]
    public void GoBack_ForwardsToOpenPreviousView()
    {
        var coordinator = CreateCoordinator();

        coordinator.GoBack();

        host.Received(1).OpenPreviousView();
    }
}
