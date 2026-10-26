using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Video.Services;

public class SessionVideoRepositoryTests
{
    [Fact]
    public async Task LinkAsync_LinksFileToEverySession()
    {
        using var tempDatabase = new TempDatabase();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var repository = database.SessionVideoRepository;
        Guid[] sessionIds = [Guid.NewGuid(), Guid.NewGuid()];
        foreach (var sessionId in sessionIds)
        {
            await database.PutSessionAsync(new Session(sessionId, "ride", string.Empty, null));
        }

        await repository.LinkAsync("/videos/ride.mp4", 1_000, 60, added: 5, sessionIds);

        var clips = await repository.GetLiveAsync();
        Assert.Equal(
            sessionIds.Order(),
            clips.Select(clip => clip.SessionId).Order());
        Assert.All(clips, clip => Assert.Equal("/videos/ride.mp4", clip.Path));
    }

    [Fact]
    public async Task LinkAsync_KeepsIdentityAndAlignment_WhenSessionAlreadyLinksPath()
    {
        using var tempDatabase = new TempDatabase();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var repository = database.SessionVideoRepository;
        var sessionId = Guid.NewGuid();
        await database.PutSessionAsync(new Session(sessionId, "ride", string.Empty, null));
        var original = Assert.Single(await repository.LinkAsync("/videos/ride.mp4", 1_000, 60, added: 5, [sessionId]));
        await repository.UpdateAlignmentAsync(original.Id, 2.5);

        var relinked = Assert.Single(await repository.LinkAsync("/videos/ride.mp4", 1_001, 61, added: 9, [sessionId]));

        Assert.Equal(original.Id, relinked.Id);
        Assert.Equal(5, relinked.Added);
        Assert.Equal(2.5, relinked.AlignmentOffsetSeconds);
        Assert.Equal(1_001, relinked.StartTimestamp);
        Assert.Single(await repository.GetLiveAsync());
    }

    [Fact]
    public async Task GetLiveAsync_HidesClipsOfDeletedSessions()
    {
        using var tempDatabase = new TempDatabase();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var liveSession = new Session(Guid.NewGuid(), "live", string.Empty, null);
        var deletedSession = new Session(Guid.NewGuid(), "deleted", string.Empty, null);
        await database.PutSessionAsync(liveSession);
        await database.PutSessionAsync(deletedSession);
        await database.SessionVideoRepository.LinkAsync(
            "/videos/ride.mp4", 1_000, 60, added: 1, [liveSession.Id, deletedSession.Id]);
        await database.DeleteAsync(deletedSession);

        var clips = await database.SessionVideoRepository.GetLiveAsync();

        Assert.Equal(liveSession.Id, Assert.Single(clips).SessionId);
    }

    [Fact]
    public async Task UpdateAlignmentAsync_ReturnsNull_WhenClipIsMissing()
    {
        using var tempDatabase = new TempDatabase();
        var repository = new TestPersistenceHarness(tempDatabase.DatabasePath).SessionVideoRepository;

        var updated = await repository.UpdateAlignmentAsync(Guid.NewGuid(), 1);

        Assert.Null(updated);
    }
}
