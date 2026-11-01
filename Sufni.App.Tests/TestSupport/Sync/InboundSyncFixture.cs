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

internal sealed class InboundSyncFixture : IDisposable
{
    private readonly TempDatabase file = new();
    private readonly IUiThreadDispatcher dispatcher;
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
        Database = new(file.DatabasePath);
        Publication = new(dispatcher);
        Bikes = new(Publication);
        Setups = new(Publication);
        Boards = new(Publication);
        Layers = new(Publication);
        Sessions = new(Publication);
        Sources = new(Publication);
        Tracks = new(Publication);
        Segments = new(Publication);
    }

    public InboundSyncCoordinator CreateCoordinator(ISyncDataStore? dataStore = null, IRecordedSessionSourceRepository? sourceRepository = null) => new(
        dataStore ?? Database.SyncDataStore,
        Database.GetRepository<Bike>(), Database.GetRepository<Setup>(), Database.GetRepository<Board>(),
        Database.GetRepository<CustomTileLayer>(), Database.SessionRepository,
        new SessionSyncApplier(Sessions, Database.SessionRepository, new InlineBackgroundTaskRunner(),
            new SessionTelemetryProcessor(), Database.SwapRequests, new ProcessingFingerprintService()),
        sourceRepository ?? Database.SourceRepository, Database.TrackRepository,
        Database.SessionSegmentRepository, Bikes, Setups, Boards, Layers, Sessions, Sources, Tracks, Segments,
        Publication, new InlineBackgroundTaskRunner(), dispatcher);

    public void Dispose()
    {
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
