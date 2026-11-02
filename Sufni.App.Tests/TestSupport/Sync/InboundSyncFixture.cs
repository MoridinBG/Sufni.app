using NSubstitute;
using Microsoft.Extensions.Time.Testing;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.TestSupport.Sync;

internal sealed class InboundSyncFixture : IAsyncDisposable
{
    private readonly TempDatabase file = new();
    private readonly IUiThreadDispatcher dispatcher;
    private readonly List<InboundSyncCoordinator> coordinators = [];
    public FakeTimeProvider Time { get; } = PersistenceTestData.CreateTimeProvider();
    public ISynchronizationTokenService Tokens { get; } = Substitute.For<ISynchronizationTokenService>();
    public PairedDeviceStore PairedDevices { get; }
    public TestPersistenceHarness Database { get; }
    public StorePublication Publication { get; }
    public BikeStore Bikes { get; }
    public SetupStore Setups { get; }
    public BoardStore Boards { get; }
    public CustomTileLayerStore Layers { get; }
    public SessionStore Sessions { get; }
    public RecordedSessionSourceStore Sources { get; }
    public TrackStore Tracks { get; }
    public SessionSegmentStore Segments { get; }

    public InboundSyncFixture(IUiThreadDispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        Database = new(file.DatabasePath, Time);
        Tokens.CreateAccessTokenAsync(Arg.Any<string>()).Returns("access-token");
        Publication = new(dispatcher);
        PairedDevices = new(Publication);
        Bikes = new(Publication);
        Setups = new(Publication);
        Boards = new(Publication);
        Layers = new(Publication);
        Sessions = new(Publication);
        Sources = new(Publication);
        Tracks = new(Publication);
        Segments = new(Publication);
    }

    public InboundSyncCoordinator CreateCoordinator(ISyncDataStore? dataStore = null, IRecordedSessionSourceRepository? sourceRepository = null)
    {
        var coordinator = new InboundSyncCoordinator(
            dataStore ?? Database.SyncDataStore, CreateRowApplier(dataStore),
            new SessionSyncApplier(Sessions, Database.SessionRepository, new InlineBackgroundTaskRunner(),
                new SessionTelemetryProcessor(), Database.SwapRequests, new ProcessingFingerprintService()),
            sourceRepository ?? Database.SourceRepository, Sources,
            Database.PairedDeviceRepository, PairedDevices, Tokens, Time,
            new InlineBackgroundTaskRunner(), dispatcher);
        coordinators.Add(coordinator);
        return coordinator;
    }

    public SynchronizationApplier CreateRowApplier(ISyncDataStore? dataStore = null) => new(
        dataStore ?? Database.SyncDataStore,
        Database.GetRepository<Bike>(), Database.GetRepository<Setup>(), Database.GetRepository<Board>(),
        Database.GetRepository<CustomTileLayer>(), Database.SessionRepository, Database.TrackRepository,
        Database.SessionSegmentRepository, Bikes, Setups, Boards, Layers, Sessions, Tracks, Segments,
        Publication, new InlineBackgroundTaskRunner(), dispatcher);

    public async ValueTask DisposeAsync()
    {
        foreach (var coordinator in coordinators) coordinator.Dispose();
        await Database.DisposeAsync();
        PairedDevices.Dispose();
        Bikes.Dispose();
        Setups.Dispose();
        Boards.Dispose();
        Layers.Dispose();
        Sessions.Dispose();
        Sources.Dispose();
        Tracks.Dispose();
        Segments.Dispose();
        Publication.Dispose();
        file.Dispose();
    }
}
