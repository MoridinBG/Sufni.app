using System.Reactive.Linq;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Sync;

namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class InboundSyncActivityTests
{
    [Fact]
    public async Task BeginRun_RequiresCurrentIncarnation_BeforePublishingActivity()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        var observed = new List<PeerSyncActivity>();
        using var subscription = coordinator.Activity.Subscribe(observed.Add);
        var request = new SynchronizationRunStart(Guid.NewGuid(), "previous-database", SyncTestServerHarness.TransferPhases);

        var response = await coordinator.BeginRunAsync("phone", request);

        Assert.Equal(await fixture.Database.SyncDataStore.GetDatabaseIncarnationAsync(), response.DatabaseIncarnation);
        Assert.IsType<PeerSyncActivity.Idle>(Assert.Single(observed));
        await coordinator.BeginRunAsync("phone", request with { DatabaseIncarnation = response.DatabaseIncarnation });
        var running = Assert.IsType<PeerSyncActivity.Running>(observed.Last());
        Assert.Equal(request.RunId, running.RunId);
        Assert.Equal(request.Phases[0], running.Phase);
        Assert.Equal(6, running.PhaseCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestsRenewSilenceDeadline_AndCompletionStopsInterruption(bool complete)
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        PeerSyncActivity? state = null;
        using var subscription = coordinator.Activity.Subscribe(value => state = value);
        var request = new SynchronizationRunStart(Guid.NewGuid(),
            await fixture.Database.SyncDataStore.GetDatabaseIncarnationAsync(), SyncTestServerHarness.TransferPhases);
        await coordinator.BeginRunAsync("phone", request);
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await coordinator.ObserveRequestAsync("phone", request.RunId, request.Phases[3]);
        fixture.Time.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1));
        var running = Assert.IsType<PeerSyncActivity.Running>(state);
        Assert.Equal(4, running.Step);
        Assert.Equal(request.Phases[3], running.Phase);

        if (complete) await coordinator.CompleteRunAsync("phone", request.RunId);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));

        if (complete) Assert.Equal(new PeerSyncActivity.Completed(request.RunId), state);
        else Assert.Equal(new PeerSyncActivity.Interrupted(request.RunId), state);
    }

    [Fact]
    public async Task NewRunSupersedesInterruptedRun_AndLateOrForeignRequestsCannotChangeIt()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        PeerSyncActivity? state = null;
        using var subscription = coordinator.Activity.Subscribe(value => state = value);
        var old = new SynchronizationRunStart(Guid.NewGuid(),
            await fixture.Database.SyncDataStore.GetDatabaseIncarnationAsync(), SyncTestServerHarness.TransferPhases);
        await coordinator.BeginRunAsync("phone", old);
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(new PeerSyncActivity.Interrupted(old.RunId), state);
        var next = old with { RunId = Guid.NewGuid(), Phases = old.Phases.Reverse().ToArray() };
        await coordinator.BeginRunAsync("phone", next);
        var current = Assert.IsType<PeerSyncActivity.Running>(state);

        await coordinator.ObserveRequestAsync("phone", old.RunId, old.Phases[5]);
        await coordinator.CompleteRunAsync("phone", old.RunId);
        await coordinator.ObserveRequestAsync("another-phone", next.RunId, next.Phases[2]);
        await coordinator.CompleteRunAsync("another-phone", next.RunId);

        Assert.Same(current, state);
        Assert.Equal(next.Phases[0], current.Phase);
        await coordinator.CompleteRunAsync("phone", next.RunId);
        Assert.Equal(new PeerSyncActivity.Completed(next.RunId), state);
    }
}
