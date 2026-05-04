using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Tests.Services;

public class SynchronizationClientServiceTests
{
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly IHttpApiService httpApiService = Substitute.For<IHttpApiService>();

    public SynchronizationClientServiceTests()
    {
        httpApiService.ServerUrl.Returns("https://sync.test");
        httpApiService.GetIncompleteSessionIdsAsync().Returns([]);
        database.GetIncompleteSessionIdsAsync().Returns([]);
        httpApiService.GetIncompleteSessionSourceIdsAsync().Returns([]);
        database.GetSessionIdsMissingRecordedSourceAsync().Returns([]);
    }

    private SynchronizationClientService CreateService()
    {
        return new SynchronizationClientService(database, httpApiService);
    }

    [Fact]
    public async Task SyncAll_PushesSynchronizationDataFromDatabase()
    {
        var trackId = Guid.NewGuid();
        var localChanges = new SynchronizationData
        {
            Sessions =
            [
                new Session(Guid.NewGuid(), "session", "desc", null, 1234)
                {
                    FullTrack = trackId,
                    Updated = 10
                }
            ],
            Tracks =
            [
                new Track
                {
                    Id = trackId,
                    Points =
                    [
                        new TrackPoint(1234, 1, 1, 0),
                        new TrackPoint(1235, 2, 2, 0)
                    ],
                    Updated = 9
                }
            ]
        };

        database.GetLastSyncTimeAsync("https://sync.test").Returns(5);
        database.GetSynchronizationDataAsync(5).Returns(localChanges);
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());

        await CreateService().SyncAll();

        await httpApiService.Received(1).PushSyncAsync(Arg.Is<SynchronizationData>(data => ReferenceEquals(data, localChanges)));
        await database.Received(1).UpdateLastSyncTimeAsync("https://sync.test");
    }

    [Fact]
    public async Task SyncAll_AppliesPulledChangesThroughRemoteSynchronizationPath()
    {
        var trackId = Guid.NewGuid();
        var remoteChanges = new SynchronizationData
        {
            Sessions =
            [
                new Session(Guid.NewGuid(), "remote", "desc", null, 1234)
                {
                    FullTrack = trackId,
                    Updated = 20
                }
            ],
            Tracks =
            [
                new Track
                {
                    Id = trackId,
                    Points =
                    [
                        new TrackPoint(1234, 1, 1, 0),
                        new TrackPoint(1235, 2, 2, 0)
                    ],
                    Updated = 19
                }
            ]
        };

        database.GetLastSyncTimeAsync("https://sync.test").Returns(5);
        database.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(remoteChanges);

        await CreateService().SyncAll();

        await database.Received(1).ApplyRemoteSynchronizationDataAsync(Arg.Is<SynchronizationData>(data =>
            data.Sessions.Count == 1 &&
            data.Tracks.Count == 1 &&
            data.Tracks[0].Id == trackId));
        await database.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
        await database.DidNotReceive().PutAsync(Arg.Any<Track>());
    }

    [Fact]
    public async Task SyncAll_PushesMissingRecordedSourcesToServer()
    {
        var source = CreateRecordedSource();

        database.GetLastSyncTimeAsync("https://sync.test").Returns(5);
        database.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());
        httpApiService.GetIncompleteSessionSourceIdsAsync().Returns([source.SessionId]);
        database.GetRecordedSessionSourceAsync(source.SessionId).Returns(source);

        await CreateService().SyncAll();

        await httpApiService.Received(1).PatchRecordedSessionSourceAsync(Arg.Is<RecordedSessionSourceTransfer>(transfer =>
            transfer.SessionId == source.SessionId &&
            transfer.SourceKind == source.SourceKind &&
            transfer.SourceName == source.SourceName &&
            transfer.SchemaVersion == source.SchemaVersion &&
            transfer.SourceHash == source.SourceHash &&
            transfer.Payload.SequenceEqual(source.Payload)));
    }

    [Fact]
    public async Task SyncAll_PullsMissingRecordedSourcesFromServer()
    {
        var source = CreateRecordedSource();
        var transfer = new RecordedSessionSourceTransfer(
            source.SessionId,
            source.SourceKind,
            source.SourceName,
            source.SchemaVersion,
            source.SourceHash,
            source.Payload);

        database.GetLastSyncTimeAsync("https://sync.test").Returns(5);
        database.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());
        database.GetSessionIdsMissingRecordedSourceAsync().Returns([source.SessionId]);
        httpApiService.GetRecordedSessionSourceAsync(source.SessionId).Returns(transfer);

        await CreateService().SyncAll();

        await database.Received(1).PutRecordedSessionSourceAsync(Arg.Is<RecordedSessionSource>(saved =>
            saved.SessionId == source.SessionId &&
            saved.SourceKind == source.SourceKind &&
            saved.SourceName == source.SourceName &&
            saved.SchemaVersion == source.SchemaVersion &&
            saved.SourceHash == source.SourceHash &&
            saved.Payload.SequenceEqual(source.Payload)));
    }

    private static RecordedSessionSource CreateRecordedSource()
    {
        var payload = new byte[] { 8, 6, 7, 5 };
        return new RecordedSessionSource
        {
            SessionId = Guid.NewGuid(),
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = "sync.SST",
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                "sync.SST",
                1,
                payload),
            Payload = payload
        };
    }
}
