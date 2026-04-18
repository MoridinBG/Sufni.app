using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Tests.Services;

public class SynchronizationClientServiceTests
{
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly IHttpApiService httpApiService = Substitute.For<IHttpApiService>();

    private SynchronizationClientService CreateService()
    {
        httpApiService.ServerUrl.Returns("https://sync.test");
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
        httpApiService.GetIncompleteSessionIdsAsync().Returns([]);
        database.GetIncompleteSessionIdsAsync().Returns([]);

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
        httpApiService.GetIncompleteSessionIdsAsync().Returns([]);
        database.GetIncompleteSessionIdsAsync().Returns([]);

        await CreateService().SyncAll();

        await database.Received(1).ApplyRemoteSynchronizationDataAsync(Arg.Is<SynchronizationData>(data =>
            data.Sessions.Count == 1 &&
            data.Tracks.Count == 1 &&
            data.Tracks[0].Id == trackId));
        await database.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
        await database.DidNotReceive().PutAsync(Arg.Any<Track>());
    }
}