using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Infrastructure;

public class AppCapabilityEagerResolutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Startup_AttachesSessionArrivalsBeforeFirstServerEvent_WhenHostingIsAvailable(bool client, bool host)
    {
        var dispatcher = new InlineUiThreadDispatcher();
        using var publication = new StorePublication(dispatcher);
        using var sessions = new SessionStore(publication);
        using var sources = new RecordedSessionSourceStore(publication);
        var repository = new InMemorySessionRepository(new FakeTimeProvider());
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        repository.Seed(snapshot);
        var server = Substitute.For<ISynchronizationServerService>();
        using var applier = new SessionSyncApplier(sessions, sources, dispatcher, Substitute.For<IRecordedSessionSourceRepository>(),
            repository.Repository, new InlineBackgroundTaskRunner(), new SessionTelemetryProcessor(), Substitute.For<ISessionBlobSwapRequestStore>());
        using var provider = new ServiceCollection()
            .AddSingleton(applier)
            .AddSingleton(server)
            .AddSingleton(Substitute.For<IPairingClientCoordinator>())
            .AddSingleton(Substitute.For<IPairingServerCoordinator>())
            .AddSingleton(Substitute.For<IInboundSyncCoordinator>())
            .BuildServiceProvider();

        App.ResolveCapabilityEagerServices(provider, new AppCapabilities(host, client, false, true, false));
        server.SynchronizationDataArrived += Raise.EventWith(server,
            new SynchronizationDataArrivedEventArgs(new SynchronizationData { Sessions = [new Session { Id = snapshot.Id }] }));

        Assert.Equal(host ? snapshot : null, sessions.Get(snapshot.Id));
    }
}
