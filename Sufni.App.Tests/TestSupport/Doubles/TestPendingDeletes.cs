using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.TestSupport;

internal static class TestPendingDeletes
{
    public static PendingDeletes Create(
        IBikeCoordinator? bikes = null,
        ISetupCoordinator? setups = null,
        ISessionCoordinator? sessions = null,
        IPairedDeviceCoordinator? pairedDevices = null,
        IShellCoordinator? shell = null,
        TimeProvider? timeProvider = null) =>
        new(bikes ?? TestCoordinatorSubstitutes.Bike(), setups ?? TestCoordinatorSubstitutes.Setup(),
            sessions ?? TestCoordinatorSubstitutes.Session(), pairedDevices ?? Substitute.For<IPairedDeviceCoordinator>(),
            shell ?? Substitute.For<IShellCoordinator>(), new InlineUiThreadDispatcher(),
            new InlineBackgroundTaskRunner(), timeProvider ?? new FakeTimeProvider());
}
