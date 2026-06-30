using NSubstitute;

using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Tests.Coordinators;

public class MobileShellCoordinatorTests
{
    private readonly IMobileNavigationShellHost host = Substitute.For<IMobileNavigationShellHost>();
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    private MobileShellCoordinator CreateCoordinator() => new(host);

    /// <summary>
    /// Minimal test-only `ViewModelBase` subclass used as input data
    /// for the shell coordinator under test. Not a substitute for the
    /// SUT's dependency — the SUT depends on `IMobileNavigationShellHost`,
    /// which is substituted separately.
    /// </summary>
    private sealed class TestViewModel : ViewModelBase
    {
        public TestViewModel()
            : base(TestDispatcher)
        {
        }
    }

    // ----- Open -----

    [Fact]
    public void Open_ForwardsToHostOpenView()
    {
        var view = new TestViewModel();
        var coordinator = CreateCoordinator();

        coordinator.Open(view);

        host.Received(1).Push(view);
    }

    // ----- OpenOrFocus -----

    [Fact]
    public void OpenOrFocus_AlwaysInvokesFactoryWithoutConsultingMatch_AndForwardsNewInstance()
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
        host.Received(1).Push(newView);
    }

    // ----- Close -----

    [Fact]
    public void Close_ForwardsToHostClose()
    {
        var view = new TestViewModel();
        var coordinator = CreateCoordinator();

        coordinator.Close(view);

        host.Received(1).Close(view);
    }

    // ----- CloseIfOpen -----

    [Fact]
    public void CloseIfOpen_IsAlwaysNoOp_OnMobile()
    {
        var coordinator = CreateCoordinator();

        coordinator.CloseIfOpen<TestViewModel>(_ => true);

        host.DidNotReceiveWithAnyArgs().Close(default!);
        host.DidNotReceiveWithAnyArgs().Push(default!);
        host.DidNotReceive().Pop();
    }

    // ----- GoBack -----

    [Fact]
    public void GoBack_ForwardsToHostPop()
    {
        host.Pop().Returns(true);
        var coordinator = CreateCoordinator();

        var handled = coordinator.GoBack();

        Assert.True(handled);
        host.Received(1).Pop();
    }

    [Fact]
    public void GoBack_ReturnsFalse_WhenHostDoesNotPop()
    {
        host.Pop().Returns(false);
        var coordinator = CreateCoordinator();

        var handled = coordinator.GoBack();

        Assert.False(handled);
        host.Received(1).Pop();
    }
}
