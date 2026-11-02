using Microsoft.Extensions.Time.Testing;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.ViewModels;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Sessions.Coordination;

namespace Sufni.App.Tests.TestSupport;

internal static class TestShellFeedback
{
    public static ShellFeedbackViewModel Create(ISyncCoordinator? sync = null, IAppEnvironment? environment = null, ISessionCoordinator? sessions = null) =>
        new(sync ?? TestCoordinatorSubstitutes.Sync(), sessions ?? TestCoordinatorSubstitutes.Session(), environment ?? TestAppEnvironment.Create(UiLayoutProfile.Workspace),
            new InlineUiThreadDispatcher(), new FakeTimeProvider());
}
