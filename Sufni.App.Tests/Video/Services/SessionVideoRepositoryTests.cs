using Sufni.App.Video.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Video.Services;

public class SessionVideoRepositoryTests
{
    [Fact]
    public async Task LinkAsync_CreatesOneLinkPerSource_WithOwnerAbsoluteAlignment()
    {
        using var tempDatabase = new TempDatabase();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var repository = database.SessionVideoRepository;
        SessionVideoOwner[] owners = [new("first-source", 1_010), new("second-source", 990), new("first-source", 1_010)];

        await repository.LinkAsync("/videos/ride.mp4", 1_000, 60, added: 5, owners);

        var clips = await repository.GetLiveAsync();
        Assert.Equal(["first-source", "second-source"], clips.Select(clip => clip.OwnerKey).Order());
        Assert.Equal(10, clips.Single(clip => clip.OwnerKey == "first-source").AlignmentOffsetSeconds);
        Assert.Equal(-10, clips.Single(clip => clip.OwnerKey == "second-source").AlignmentOffsetSeconds);
        Assert.All(clips, clip => Assert.Equal("/videos/ride.mp4", clip.Path));
    }

    [Fact]
    public async Task LinkAsync_KeepsIdentityAndAlignment_WhenSourceAlreadyLinksPath()
    {
        using var tempDatabase = new TempDatabase();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var repository = database.SessionVideoRepository;
        var sessionId = Guid.NewGuid();
        await database.PutSessionAsync(new Session(sessionId, "ride", string.Empty, null));
        var original = Assert.Single(await repository.LinkAsync("/videos/ride.mp4", 1_000, 60, added: 5, [new SessionVideoOwner("source-hash", 1_000)]));
        await repository.UpdateAlignmentAsync(original.Id, 2.5);

        var relinked = Assert.Single(await repository.LinkAsync("/videos/ride.mp4", 1_001, 61, added: 9, [new SessionVideoOwner("source-hash", 1_000)]));

        Assert.Equal(original.Id, relinked.Id);
        Assert.Equal(5, relinked.Added);
        Assert.Equal(2.5, relinked.AlignmentOffsetSeconds);
        Assert.Equal(1_001, relinked.StartTimestamp);
        Assert.Single(await repository.GetLiveAsync());
    }

    [Fact]
    public async Task GetLiveAsync_RetainsSourceClipsAfterSessionDeletion()
    {
        using var tempDatabase = new TempDatabase();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var liveSession = new Session(Guid.NewGuid(), "live", string.Empty, null);
        var deletedSession = new Session(Guid.NewGuid(), "deleted", string.Empty, null);
        await database.PutSessionAsync(liveSession);
        await database.PutSessionAsync(deletedSession);
        await database.SessionVideoRepository.LinkAsync(
            "/videos/ride.mp4", 1_000, 60, added: 1, [new SessionVideoOwner("first-source", 1_000), new SessionVideoOwner("second-source", 1_000)]);
        await database.DeleteAsync(deletedSession);

        var clips = await database.SessionVideoRepository.GetLiveAsync();

        Assert.Equal(2, clips.Count);
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
