using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Tests.Shell.ViewModels;

internal static class ShellRootViewModelTestFactory
{
    public static ShellRootViewModel Create(
        MainPagesViewModel pages,
        ShellWorkspaceViewModel workspace,
        IAppEnvironment appEnvironment,
        IPlotZoomState plotZoomState,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider)
    {
        var startup = Substitute.For<IAppStartup>();
        startup.Observe().Returns(Observable.Return<ShellStartupState>(new ShellStartupState.Ready()));
        return new ShellRootViewModel(startup, workspace, appEnvironment, plotZoomState, uiThreadDispatcher, timeProvider)
        {
            Pages = pages
        };
    }
}
