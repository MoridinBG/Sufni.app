using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Video.Services;

public class SessionVideoRepositoryTests
{
    [Fact]
    public async Task LinkAsync_LinksFileToEverySession()
    {
        using var tempDatabase = new TempDatabase();
        var repository = new TestPersistenceHarness(tempDatabase.DatabasePath).SessionVideoRepository;
        Guid[] sessionIds = [Guid.NewGuid(), Guid.NewGuid()];

        await repository.LinkAsync("/videos/ride.mp4", 1_000, 60, added: 5, sessionIds);

        var clips = await repository.GetAllAsync();
        Assert.Equal(
            sessionIds.Order(),
            clips.Select(clip => clip.SessionId).Order());
        Assert.All(clips, clip => Assert.Equal("/videos/ride.mp4", clip.Path));
    }

    [Fact]
    public async Task LinkAsync_KeepsIdentityAndAlignment_WhenSessionAlreadyLinksPath()
    {
        using var tempDatabase = new TempDatabase();
        var repository = new TestPersistenceHarness(tempDatabase.DatabasePath).SessionVideoRepository;
        var sessionId = Guid.NewGuid();
        var original = Assert.Single(await repository.LinkAsync("/videos/ride.mp4", 1_000, 60, added: 5, [sessionId]));
        await repository.UpdateAlignmentAsync(original.Id, 2.5);

        var relinked = Assert.Single(await repository.LinkAsync("/videos/ride.mp4", 1_001, 61, added: 9, [sessionId]));

        Assert.Equal(original.Id, relinked.Id);
        Assert.Equal(5, relinked.Added);
        Assert.Equal(2.5, relinked.AlignmentOffsetSeconds);
        Assert.Equal(1_001, relinked.StartTimestamp);
        Assert.Single(await repository.GetAllAsync());
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
