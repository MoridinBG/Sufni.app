using Sufni.App.Video.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Video.Services;

public class SessionVideoRepositoryTests
{
    [Fact]
    public async Task GetAllAsync_ReportsCurrentPresenceWithoutDroppingMissingClips()
    {
        using var tempDatabase = new TempDatabase();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var repository = database.SessionVideoRepository;
        const string path = "/videos/presence.mp4";
        await repository.LinkAsync(path, 1_000, 60, 1, [new SessionVideoOwner("unreferenced-source", 1_000)]);
        Assert.False(Assert.Single(await repository.GetAllAsync()).FileExists);

        database.VideoPresence.SetPresent(path, true);

        Assert.True(Assert.Single(await repository.GetAllAsync()).FileExists);
    }

    [Fact]
    public async Task LinkAsync_CreatesOneLinkPerSource_WithOwnerAbsoluteAlignment()
    {
        using var tempDatabase = new TempDatabase();
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var repository = database.SessionVideoRepository;
        SessionVideoOwner[] owners = [new("first-source", 1_010), new("second-source", 990), new("first-source", 1_010)];

        await repository.LinkAsync("/videos/ride.mp4", 1_000, 60, added: 5, owners);

        var clips = await repository.GetAllAsync();
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
        Assert.Single(await repository.GetAllAsync());
    }

    [Fact]
    public async Task GetAllAsync_RetainsSourceClipsAfterSessionDeletion()
    {
        using var tempDatabase = new TempDatabase();
        await using var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", string.Empty, null);
        var source = PersistenceTestData.CreateRecordedSessionSource(session.Id);
        await database.PutSessionWithRecordedSourceAsync(session, source);
        var original = Assert.Single(await database.SessionVideoRepository.LinkAsync(
            "/videos/ride.mp4", 1_000, 60, added: 1, [new SessionVideoOwner(source.SourceHash, 1_000)]));
        Assert.Equal(source.SourceHash, Assert.Single(await database.SessionVideoRepository.GetAllAsync()).OwnerKey);

        await database.SessionTransactions.DeleteSessionAsync(session.Id, TestContext.Current.CancellationToken);

        Assert.Null(await database.GetSessionAsync(session.Id));
        Assert.Equal(original, Assert.Single(await database.SessionVideoRepository.GetAllAsync()));
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
